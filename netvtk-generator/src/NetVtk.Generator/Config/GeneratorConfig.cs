// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// YamlDotNet
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetVtk.Generator.Config;

/// <summary>config/modules.yaml: which VTK modules to generate and how. Hand-edited.</summary>
internal sealed class GeneratorConfig
{
  /// <summary>
  /// The operating systems a library may be one platform's, as .NET's RIDs name them.
  /// </summary>
  public static readonly IReadOnlyList<string> OperatingSystems = ["win", "linux", "osx"];

  /// <summary>
  /// The native libraries (<c>NetVtk&lt;Library&gt;</c>) and the VTK modules whose wrappers each
  /// holds, in build order: what generate writes. A Windows DLL exports at most 65535 functions.
  /// </summary>
  public Dictionary<string, List<string>> Libraries { get; set; } = [];

  /// <summary>
  /// The libraries of one platform's classes, by library, and the platform's OS as .NET's RIDs name
  /// it (<see cref="OperatingSystems"/>): netvtk-core builds and packs them there only.
  /// </summary>
  public Dictionary<string, string> Platforms { get; set; } = [];

  /// <summary>
  /// Header patterns (<c>*</c> any characters) left out of every module: one platform's classes
  /// (VTK installs only the platform's, and the output must build everywhere). A header a module
  /// takes from another (<see cref="PackageConfig.Headers"/>) is that module's.
  /// </summary>
  public List<string> ExcludeHeaders { get; set; } = [];

  /// <summary>Per module settings, by module name.</summary>
  public Dictionary<string, PackageConfig> Modules { get; set; } = [];

  /// <summary>The configured modules, in the libraries' order.</summary>
  public IReadOnlyList<string> Generate => [.. Libraries.Values.SelectMany(m => m)];

  public PackageConfig For(string module) =>
    Modules.TryGetValue(module, out var config) ? config : new PackageConfig();

  /// <summary>The library a configured module's wrappers go into, or null.</summary>
  public string? LibraryOf(string module) =>
    Libraries.FirstOrDefault(l => l.Value.Contains(module)).Key;

  /// <summary>
  /// The VTK module whose headers a module parses: its <see cref="PackageConfig.VtkModule"/>, or
  /// the module itself.
  /// </summary>
  public string VtkModuleOf(string module) => For(module).VtkModule ?? module;

  public bool IsExcludedHeader(string header) => ExcludeHeaders.Any(p => Matches(p, header));

  /// <summary>
  /// A header of <paramref name="vtkModule"/> that a configured module takes
  /// (<see cref="PackageConfig.Headers"/>): its own module leaves it out.
  /// </summary>
  public bool IsTakenHeader(string vtkModule, string header) =>
    Generate.Any(m => For(m) is { VtkModule: { } from, Headers: var headers }
                      && from == vtkModule
                      && headers.Any(p => Matches(p, header)));

  /// <summary>A header name against a pattern, <c>*</c> any characters.</summary>
  public static bool Matches(string pattern, string header) =>
    Regex.IsMatch(header, "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$");

  public static GeneratorConfig Load(string path)
  {
    var config =
      new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build()
        .Deserialize<GeneratorConfig>(File.ReadAllText(path))
      ?? new GeneratorConfig();
    var problems = Problems(config).ToList();
    return problems.Count == 0
      ? config
      : throw new InvalidDataException($"{path}: {string.Join("; ", problems)}");
  }

  private static IEnumerable<string> Problems(GeneratorConfig config)
  {
    var twice = config.Generate.GroupBy(m => m)
      .Where(g => g.Count() > 1)
      .Select(g => g.Key)
      .ToList();
    if (twice.Count > 0)
    {
      yield return $"modules in more than one library: {string.Join(", ", twice)}";
    }

    foreach (var (library, os) in config.Platforms)
    {
      if (!config.Libraries.ContainsKey(library))
      {
        yield return $"platforms: {library} isn't a library";
      }

      if (!OperatingSystems.Contains(os))
      {
        yield return
          $"platforms: {library}: {os} isn't one of {string.Join(", ", OperatingSystems)}";
      }
    }

    foreach (var (module, settings) in config.Modules)
    {
      if ((settings.VtkModule is null) != (settings.Headers.Count == 0))
      {
        yield return $"modules: {module}: vtk_module and headers go together";
      }
    }
  }
}

internal sealed class PackageConfig
{
  /// <summary>
  /// The VTK module whose <see cref="Headers"/> the module takes, for a module VTK doesn't have:
  /// those headers leave that module (one platform's classes, in a library of
  /// <see cref="GeneratorConfig.Platforms"/>). Null: the module is VTK's, all its headers.
  /// </summary>
  public string? VtkModule { get; set; }

  /// <summary>
  /// The header patterns (<c>*</c> any characters) of <see cref="VtkModule"/> the module takes.
  /// </summary>
  public List<string> Headers { get; set; } = [];

  /// <summary>
  /// Only these classes; null means every class of the module, an empty list means enums only.
  /// </summary>
  public List<string>? Classes { get; set; }

  public List<string> ExcludeClasses { get; set; } = [];

  /// <summary>
  /// <c>Class::Method</c> and <c>Namespace::Function</c> entries left out, all overloads; and a
  /// value class's fields, <c>Class::Field</c>.
  /// </summary>
  public List<string> ExcludeMethods { get; set; } = [];

  /// <summary>
  /// C++ namespaces whose types and functions are left out (implementation details in public
  /// headers).
  /// </summary>
  public List<string> ExcludeNamespaces { get; set; } = [];

  /// <summary>
  /// Declared in one of <see cref="ExcludeNamespaces"/>: a qualified name like
  /// <c>vtk::detail::x</c>, or a namespace.
  /// </summary>
  public bool IsExcludedScope(string? qualifiedName) =>
    qualifiedName is not null
    && ExcludeNamespaces.Any(ns => qualifiedName == ns
                                   || qualifiedName.StartsWith(
                                     $"{ns}::", StringComparison.Ordinal));

  /// <summary>
  /// Headers included before the module's own, for module headers that miss an include.
  /// </summary>
  public List<string> Prelude { get; set; } = [];

  /// <summary>
  /// Classes that are C# structs (generated into netvtk-core's src/NetVtk/&lt;Module&gt;/, where a
  /// hand-written partial may add managed members): %netvtk_valuetype and layout guards, no proxy.
  /// </summary>
  public List<string> ValueTypes { get; set; } = [];

  /// <summary>
  /// VTK object classes C# may subclass (SWIG directors): VTK calls a C# override of their virtual
  /// members, inherited and protected ones too, and their constructors are wrapped, protected ones
  /// and those of an abstract class too.
  /// </summary>
  public List<string> Directors { get; set; } = [];
}
