// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

//
using NetVtk.Generator.Config;
using NetVtk.Generator.Emit;
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;
using NetVtk.Generator.Parsing;

namespace NetVtk.Generator;

/// <summary>
/// <c>netvtk-gen generate</c>: parse the configured VTK modules, map them to netvtk-core's contract
/// and write <c>src/SWIG_files/{wrapper,headers}</c> plus <c>src/SWIG_files/modules.json</c> (the
/// modules per native library), <c>platforms.json</c> (the libraries of one platform's classes)
/// and <c>classes.json</c> (the types per module, for the documentation's class index). Modules a
/// run doesn't write (another run's, or hand-written ones) tell it through their .i files which
/// types exist.
/// </summary>
internal static class Generate
{
  /// <param name="only">Modules or libraries to write; empty: the config's.</param>
  /// <param name="check">
  /// Compare instead of writing: 1 if any output file would change. Writes nothing, the skip logs
  /// neither.
  /// </param>
  public static int Run(Workspace workspace, string core, IReadOnlyList<string> only,
                        TextWriter output, bool check = false)
  {
    var config = workspace.Config;
    var packages = only.Count > 0 ? workspace.Expand(only) : workspace.Configured;
    foreach (var package in packages.Where(p => !workspace.Configured.Contains(p)))
    {
      output.WriteLine(
        $"warning: {package} isn't in a configured library: build.py won't build it (modules.json)");
    }

    var paths = new Paths(core, workspace.ConfigDirectory);
    var registry = ScanOthers(paths, packages, workspace.Configured, output);

    var exports = LibraryExports.Load(workspace.Source.Libraries);
    output.WriteLine(exports is null
                       ? $"warning: no VTK DLLs (vtk*.dll) in {workspace.Source.Libraries}: members declared exported but never defined won't link"
                       : $"exports: {exports.Count} symbols from {exports.Libraries} VTK libraries");
    var parser = new PackageParser(workspace.Source.IncludeDirectories, PackageParser.VtkArguments,
                                   exports);
    // every module's value types: a plain-data struct may hold another module's
    HashSet<string> valueTypes = [.. config.Modules.Values.SelectMany(p => p.ValueTypes)];
    var parsed = Workspace.ParseAll(packages, p => Parse(workspace, parser, valueTypes, p));

    // a module written without a package it uses would lack that package's types and imports: a
    // package that doesn't parse fails the run, which writes nothing
    List<string> failed = [.. parsed.Select(p => p.Error).OfType<string>()];
    if (failed.Count > 0)
    {
      foreach (var message in failed)
      {
        output.WriteLine($"error: {message}");
      }

      output.WriteLine(
        $"generate: {failed.Count} of {packages.Count} packages failed to parse; nothing written");
      return 1;
    }

    var models = LeaveDeclaredInstances(OwnInstances([.. parsed.Select(p => p.Result!)]), registry);
    foreach (var warning in StaleConfig(workspace, models))
    {
      output.WriteLine($"warning: config: {warning}");
    }

    // every package's types before any module is written: packages use each other
    foreach (var model in models)
    {
      Register(registry, model, config.For(model.Name));
    }

    var outputs = new Outputs(check);
    var namespaces = WriteModules(models, workspace, registry, paths, outputs, output);

    // the modules build.py runs SWIG over, per native library, in the config's order; and the
    // libraries it builds on one platform only
    outputs.Emit(Path.Combine(paths.SwigFiles, "modules.json"),
                 ModulesJson(workspace.Configured, m => config.LibraryOf(m)!,
                             [.. config.Libraries.Keys]));
    outputs.Emit(Path.Combine(paths.SwigFiles, "platforms.json"), PlatformsJson(config));
    // the types each package gives C#, which the documentation links to VTK's reference; a run over
    // some packages keeps the others' entries
    var classIndex = Path.Combine(paths.SwigFiles, "classes.json");
    outputs.Emit(classIndex,
                 ClassIndex.Json(workspace.Configured,
                                 models.ToDictionary(m => m.Name,
                                                     m => ClassIndex.Of(
                                                       m, config.For(m.Name), namespaces[m.Name])),
                                 File.Exists(classIndex) ? File.ReadAllText(classIndex) : null));

    if (!check)
    {
      output.WriteLine($"skip logs: {Path.GetFullPath(paths.Skips)}");
      return 0;
    }

    foreach (var path in outputs.Changed)
    {
      output.WriteLine($"out of date: {Path.GetFullPath(path)}");
    }

    output.WriteLine(outputs.Changed.Count == 0
                       ? "check: netvtk-core is up to date"
                       : $"check: {outputs.Changed.Count} file(s) differ; run generate");
    return outputs.Changed.Count == 0 ? 0 : 1;
  }

  /// <summary>
  /// One module as generate parses it: its headers (<c>exclude_headers</c>' out), its prelude, the
  /// config's classes, the value types; what <c>exclude_namespaces</c> names is gone before
  /// anything sees it, the nested header included.
  /// </summary>
  internal static PackageModel Parse(Workspace workspace, PackageParser parser,
                                     IReadOnlyCollection<string> valueTypes, string package)
  {
    var config = workspace.Config.For(package);
    var (headers, excluded) = workspace.Headers(package);
    var model = parser.Parse(package, headers, config.Prelude, valueTypes, config.Classes);
    return model with
    {
      ExcludedHeaders =
      [
        .. excluded.Select(h => $"{h}: exclude_headers (one platform's)"),
        .. model.ExcludedHeaders ?? []
      ],
      Enums = [.. model.Enums.Where(e => !config.IsExcludedScope(e.QualifiedName))],
      Classes = [.. model.Classes.Where(c => !config.IsExcludedScope(c.QualifiedName))],
      NamespaceFunctions =
      [.. model.NamespaceFunctions.Where(f => !config.IsExcludedScope(f.Namespace))],
    };
  }

  // a run over some packages: a template instance a module it doesn't write declares stays that
  // module's class, which the run's packages use under its name
  private static List<PackageModel> LeaveDeclaredInstances(List<PackageModel> models,
                                                           TypeRegistry registry)
  {
    List<PackageModel> left = [];
    foreach (var model in models)
    {
      List<ClassModel> declared =
      [
        .. model.Classes.Where(c => c.Instance is not null
                                    && registry.Class(c.Name) is { } known
                                    && known.Package != model.Name)
      ];
      foreach (var c in declared)
      {
        registry.AddInstance(c.Instance!.Type, c.Name);
      }

      left.Add(declared.Count == 0
                 ? model
                 : model with { Classes = [.. model.Classes.Except(declared)] });
    }

    return left;
  }

  // config entries that name nothing (any more): a VTK upgrade renamed or dropped what they name
  private static IEnumerable<string> StaleConfig(Workspace workspace, List<PackageModel> models)
  {
    var byName = models.ToDictionary(m => m.Name);
    foreach (var (package, config) in workspace.Config.Modules.OrderBy(
               p => p.Key, StringComparer.Ordinal))
    {
      if (!workspace.Configured.Contains(package))
      {
        yield return $"modules: {package} isn't generated";
        continue;
      }

      if (!byName.TryGetValue(package, out var model))
      {
        continue;
      }

      HashSet<string> classes = [.. model.Classes.Select(c => c.Name)];
      foreach (var name in (config.Classes ?? []).Concat(config.ExcludeClasses)
               .Where(n => !classes.Contains(n)))
      {
        yield return $"{package}: no class {name}";
      }

      // Class::Method (a constructor as Class::Class, a value class's field as Class::Field), or
      // Namespace::Function
      HashSet<string> members =
      [
        .. model.Classes.SelectMany(c => c.Methods.Select(m => $"{c.Name}::{m.Name}")
                                      .Concat((c.Fields ?? []).Select(f => $"{c.Name}::{f.Name}"))
                                      .Append($"{c.Name}::{c.Name}")),
        .. model.NamespaceFunctions.Select(f => $"{f.Namespace}::{f.Name}")
      ];
      foreach (var label in config.ExcludeMethods.Where(l => !members.Contains(l)))
      {
        yield return $"{package}: exclude_methods names nothing: {label}";
      }
    }
  }

  // modules the run doesn't write: another run's or hand-written; their .i files tell what they
  // provide and import
  private static TypeRegistry ScanOthers(Paths paths, IReadOnlyList<string> packages,
                                         IReadOnlyList<string> configured, TextWriter output)
  {
    var registry = new TypeRegistry();
    foreach (var file in Directory.EnumerateFiles(paths.Wrapper, "*.i")
               .Order(StringComparer.Ordinal))
    {
      var package = Path.GetFileNameWithoutExtension(file);
      if (packages.Contains(package))
      {
        continue;
      }

      registry.ScanModule(file);
      if (!configured.Contains(package))
      {
        output.WriteLine(
          $"warning: {package}.i isn't in a configured library, so build.py won't build it (modules.json)");
      }
    }

    return registry;
  }

  /// <summary>
  /// The two passes over the packages, the second one writing (or comparing) each module's files
  /// and skip log. Returns the namespaces each package gives a static class.
  /// </summary>
  private static Dictionary<string, IReadOnlyList<string>> WriteModules(
    List<PackageModel> models, Workspace workspace, TypeRegistry registry, Paths paths,
    Outputs outputs, TextWriter output)
  {
    var config = workspace.Config;
    // value-type structs: the generated part next to the hand-written partial
    // (src/NetVtk/<Pkg>/<Type>.cs)
    Dictionary<string, HandWritten> partials = [];

    HandWritten HandWrittenOf(string package, string type)
    {
      var path = Path.Combine(paths.Structs, package, $"{type}.cs");
      if (!partials.TryGetValue(path, out var partial))
      {
        partial = partials[path] = HandWritten.Load(path);
      }

      return partial;
    }

    var classes = models.SelectMany(m => m.Classes)
      .GroupBy(c => c.Name)
      .ToDictionary(g => g.Key, g => g.First());
    var writer = new InterfaceWriter(registry, new SignatureMapper(registry),
                                     workspace.Source.Version, p => config.For(p).Prelude,
                                     HandWrittenOf, classes.GetValueOrDefault,
                                     Directors(models, config));

    bool HasExtras(PackageModel model) =>
      File.Exists(Path.Combine(paths.SwigFiles, "extras", $"{model.Name}.i"));

    // a module imports what it uses and, transitively, what those use; packages may use each other
    // (SWIG is fine with cycles). A first pass collects every module's direct imports, and the
    // packages its classes derive from, which its importers import first.
    var first = models.ToDictionary(m => m.Name,
                                    m => writer.Write(m, config.For(m.Name), _ => [],
                                                      HasExtras(m)));

    IReadOnlyList<string> ImportsOf(string package) => first.TryGetValue(package, out var module)
      ? module.Imports
      : registry.ImportsOf(package);

    IReadOnlyCollection<string> BasesOf(string package) =>
      first.TryGetValue(package, out var module)
        ? module.BasePackages
        : registry.BasePackagesOf(package);

    Dictionary<string, IReadOnlyList<string>> namespaces = [];
    foreach (var model in models)
    {
      var module =
        writer.Write(model, config.For(model.Name), ImportsOf, HasExtras(model), BasesOf);
      outputs.Emit(paths.Interface(model.Name), module.Interface);
      outputs.Emit(Path.Combine(paths.Headers, $"{model.Name}_module.hxx"), module.ModuleHeader);
      var nestedHeader = Path.Combine(paths.Headers, ModuleHeaders.NestedHeaderOf(model.Name));
      if (module.NestedHeader is { } nested)
      {
        outputs.Emit(nestedHeader, nested);
      }

      var directory = Path.Combine(paths.Structs, model.Name);
      foreach (var generated in module.Structs)
      {
        outputs.Emit(Path.Combine(directory, $"{generated.Name}.g.cs"), generated.Code);
      }

      // what the package no longer has: a struct, its nested types
      IEnumerable<string> owned = Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*.g.cs")
        : [];
      foreach (var path in owned.Append(nestedHeader))
      {
        outputs.RemoveUnlessWritten(path);
      }

      outputs.Log(Path.Combine(paths.Skips, $"{model.Name}.txt"),
                  string.Concat(module.Skipped.Select(s => s + "\n")));
      namespaces[model.Name] = module.Namespaces;
      output.WriteLine(
        $"{model.Name}: {model.Enums.Count} enums, {model.Classes.Count} classes, {module.Skipped.Count} members skipped"
        + (module.Imports.Count > 0 ? $"; imports {string.Join(" ", module.Imports)}" : ""));
    }

    return namespaces;
  }

  // the SWIG modules of each configured library, which build.py compiles into one native library
  // each (a Windows DLL exports at most 65535 functions); libraries and modules in the config's
  // order
  private static string ModulesJson(IReadOnlyList<string> packages, Func<string, string> moduleOf,
                                    IReadOnlyList<string> moduleOrder)
  {
    List<string> order = [.. moduleOrder];
    var groups = packages.GroupBy(moduleOf)
      .OrderBy(g => order.IndexOf(g.Key) is var index and >= 0 ? index : int.MaxValue)
      .Select(g => $"  \"{g.Key}\": [\n{string.Join(",\n", g.Select(p => $"    \"{p}\""))}\n  ]");
    return "{\n" + string.Join(",\n", groups) + "\n}\n";
  }

  // the libraries of one platform's classes and that platform's OS (.NET's RID names: win, linux,
  // osx), which netvtk-core builds and packs there only; in the config's library order
  internal static string PlatformsJson(GeneratorConfig config)
  {
    List<string> entries =
    [
      .. config.Libraries.Keys.Where(config.Platforms.ContainsKey)
        .Select(l => $"  \"{l}\": \"{config.Platforms[l]}\"")
    ];
    return entries.Count == 0 ? "{}\n" : "{\n" + string.Join(",\n", entries) + "\n}\n";
  }

  /// <summary>
  /// A class template instance a signature uses is a class of its first user, in package order, the
  /// only one that keeps it: an instance several packages use is one class. An alias anywhere names
  /// it (the first in package order; <c>typedef vtkFoo&lt;double&gt; vtkFooD</c>), otherwise its
  /// flat name does; the owner's nested header declares the name, the same alias again where VTK
  /// has one.
  /// </summary>
  private static List<PackageModel> OwnInstances(List<PackageModel> models)
  {
    Dictionary<string, string> aliases = [];
    foreach (var typedef in models.SelectMany(m => m.Typedefs))
    {
      if (typedef.Target is NamedType { TemplateArguments.Count: > 0 } target)
      {
        aliases.TryAdd((target with { Const = false }).Spelling, typedef.Name);
      }
    }

    ClassModel Aliased(ClassModel c) =>
      c.Instance is { } instance && aliases.TryGetValue(instance.Type.Spelling, out var alias)
        ? c with { Name = alias }
        : c;

    List<PackageModel> owners = [];
    HashSet<string> owned = [];
    HashSet<string> ownedEnums = [];
    foreach (var m in models)
    {
      List<ClassModel> classes = [];
      foreach (var c in m.Classes)
      {
        if (c.Instance is null || owned.Add(c.Instance.Type.Spelling))
        {
          classes.Add(Aliased(c));
        }
      }

      // an enum in an instance (vtkSOADataArrayTemplate<float>::StorageTypeEnum) is its first
      // user's too
      owners.Add(m with
      {
        Classes = classes,
        Enums =
        [
          .. m.Enums.Where(e => e.QualifiedName is not { } qualified
                                || !qualified.Contains('<')
                                || ownedEnums.Add(qualified))
        ],
      });
    }

    return owners;
  }

  /// <summary>
  /// Every module's <c>directors</c>: classes of the module C# may subclass, VTK objects
  /// (Directors.i keeps a C# one alive while VTK holds a reference). A class that isn't one fails
  /// the run, whose modules would otherwise lack it.
  /// </summary>
  private static List<string> Directors(IReadOnlyList<PackageModel> models, GeneratorConfig config)
  {
    List<string> directors = [];
    foreach (var model in models)
    {
      foreach (var name in config.For(model.Name).Directors)
      {
        var c = model.Classes.FirstOrDefault(c => c.Name == name)
                ?? throw new InvalidOperationException(
                  $"{model.Name}: director {name} is not a class of the package");
        if (!c.Traits.IsVtkObject)
        {
          throw new InvalidOperationException($"{model.Name}: director {name} is not a VTK object");
        }

        directors.Add(name);
      }
    }

    return directors;
  }

  // what a generated package provides, registered before any module is written so packages can use
  // each other
  private static void Register(TypeRegistry registry, PackageModel model, PackageConfig config)
  {
    // a nested or namespace type is known by its flat name, which the package's nested header
    // declares, and so is a class template instance no alias names
    string HeaderOf(string header, string? qualifiedName, ClassInstance? instance = null) =>
      qualifiedName is null && instance is null ? header : ModuleHeaders.NestedHeaderOf(model.Name);

    foreach (var e in model.Enums)
    {
      registry.AddEnum(e.Name, model.Name, HeaderOf(e.Header, e.QualifiedName), e.Underlying,
                       e.QualifiedName);
    }

    // the configured value types whatever `classes` lists, plain data where listed
    List<ClassModel> registered = [];
    foreach (var c in ClassRules.ValueTypes(model, config))
    {
      registry.AddClass(new KnownClass(c.Name, model.Name, WrapKind.ValueType,
                                       HeaderOf(c.Header, c.QualifiedName, c.Instance)));
      registered.Add(c);
    }

    foreach (var c in ClassRules.Proxied(model, config))
    {
      registry.AddClass(new KnownClass(c.Name, model.Name, ClassRules.KindOf(c),
                                       HeaderOf(c.Header, c.QualifiedName, c.Instance),
                                       ClassRules.IsMoveOnly(c)));
      registered.Add(c);
    }

    foreach (var c in registered.Where(c => c.Instance is not null))
    {
      registry.AddInstance(c.Instance!.Type, c.Name);
    }
  }

  /// <summary>
  /// Where a run reads and writes: netvtk-core's generated files, the generator's skip logs.
  /// </summary>
  private sealed class Paths(string core, string configDirectory)
  {
    public string SwigFiles { get; } = Path.Combine(core, "src", "SWIG_files");

    public string Wrapper => Path.Combine(SwigFiles, "wrapper");

    public string Headers => Path.Combine(SwigFiles, "headers");

    /// <summary>The value-type structs, src/NetVtk/&lt;Pkg&gt;/&lt;Type&gt;.g.cs.</summary>
    public string Structs { get; } = Path.Combine(core, "src", "NetVtk");

    public string Skips { get; } = Path.Combine(configDirectory, "..", "log", "skips");

    public string Interface(string package) => Path.Combine(Wrapper, $"{package}.i");
  }

  /// <summary>
  /// The files a run writes (generated files are identical on every OS: LF), or in a check
  /// compares: the ones that differ, and the generator's own it no longer writes.
  /// </summary>
  private sealed class Outputs(bool check)
  {
    // case-insensitive: on Windows and macOS a stale path differing in case from a written one is
    // that file
    private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Changed { get; } = [];

    public void Emit(string path, string text)
    {
      _written.Add(Path.GetFullPath(path));
      var lf = GeneratedText.Lf(text);
      if (File.Exists(path) && File.ReadAllText(path) == lf)
      {
        return;
      }

      Changed.Add(path);
      if (!check)
      {
        Write(path, lf);
      }
    }

    /// <summary>
    /// Deletes a generated file the run didn't write (in a check, reports it): its type went away.
    /// </summary>
    public void RemoveUnlessWritten(string path)
    {
      if (!File.Exists(path) || _written.Contains(Path.GetFullPath(path)))
      {
        return;
      }

      Changed.Add(path);
      if (!check)
      {
        File.Delete(path);
      }
    }

    /// <summary>A log the run writes whatever changed, and a check doesn't.</summary>
    public void Log(string path, string text)
    {
      if (!check)
      {
        Write(path, GeneratedText.Lf(text));
      }
    }

    private static void Write(string path, string lf)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
      File.WriteAllText(path, lf);
    }
  }
}
