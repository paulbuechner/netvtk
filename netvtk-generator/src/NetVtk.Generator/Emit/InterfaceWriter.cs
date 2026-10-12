// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
using NetVtk.Generator.Config;
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;

// static usings
using static NetVtk.Generator.Emit.ClassRules;
using static NetVtk.Generator.Emit.MemberRules;

namespace NetVtk.Generator.Emit;

/// <summary>
/// Writes one package's SWIG interface in netvtk-core's conventions (declarations only,
/// deterministic).
/// </summary>
/// <param name="preludeOf">
/// A package's configured prelude: headers its own headers need first.
/// </param>
/// <param name="handWrittenOf">
/// The hand-written partial of a value-type struct (package, type), whose members aren't generated.
/// </param>
/// <param name="classOf">
/// Any parsed class by name: the bases of an argument's class in other packages
/// (<see cref="IsKept"/>), what a returned class refers to (<see cref="View"/>), the virtual
/// members a director class inherits.
/// </param>
/// <param name="directorClasses">
/// The classes C# may subclass, every package's <c>directors</c>: SWIG directors, and the bases
/// and derived classes around them.
/// </param>
internal sealed partial class InterfaceWriter(TypeRegistry registry, SignatureMapper mapper,
                                              string vtkVersion,
                                              Func<string, IReadOnlyList<string>>? preludeOf = null,
                                              Func<string, string, HandWritten>? handWrittenOf =
                                                null, Func<string, ClassModel?>? classOf = null,
                                              IReadOnlyCollection<string>? directorClasses = null)
{
  private readonly ValueTypeWriter _structs = new(registry, mapper, vtkVersion);

  private readonly HashSet<string> directors = [.. directorClasses ?? []];

  // members every SWIG C# proxy already has. Not GetType: VTK's (vtkVariant::GetType) hides
  // object's, which isn't virtual, and the proxy's own code never calls it
  private static readonly HashSet<string> ProxyMembers = ["Dispose", "Finalize", "MemberwiseClone"];

  // the root of VTK's objects
  private const string RootTransient = "vtkObjectBase";

  // the attribute as a SWIG string, for %csattributes and the csattributes typemap
  internal static readonly string SwigObsolete = $"\"{CsObsolete.Replace("\"", "\\\"")}\"";

  /// <param name="hasExtras">
  /// netvtk-core has a hand-written companion, <c>src/SWIG_files/extras/&lt;Pkg&gt;.i</c>, for what
  /// the generator can't derive (lifetime typemaps, value-type thunks, %extend). It is included
  /// after the enums, before the classes.
  /// </param>
  /// <param name="basesOf">
  /// The other packages a package's classes derive from, which orders the imports.
  /// </param>
  public GeneratedModule Write(PackageModel package, PackageConfig config,
                               Func<string, IReadOnlyList<string>> importsOf,
                               bool hasExtras = false,
                               Func<string, IReadOnlyCollection<string>>? basesOf = null)
  {
    var own = package.Name;
    var module = new ModuleContext(own, config);
    module.Skipped.AddRange([
      .. (package.ExcludedHeaders ?? []).Select(h => $"header {h}"), .. package.ExcludedTypes ?? []
    ]);
    var uses = module.Uses;
    var valueTypes = ValueTypes(package, config);
    var classes = Classes(package, config);
    var macros = ClassMacros(valueTypes, classes);
    var enums = EnumDeclarations(package);

    var body = new StringBuilder();
    foreach (var c in classes)
    {
      WriteClass(body, c, module);
    }

    // value types: C# structs whose native members call thunks, declared after the classes their
    // signatures may use
    List<GeneratedStruct> structs =
    [
      .. valueTypes.Select(v => _structs.Write(v, module,
                                               handWrittenOf?.Invoke(own, v.Name)
                                               ?? HandWritten.None))
    ];
    var thunks = string.Concat(structs.Select(s => s.Thunks));
    if (thunks.Length > 0)
    {
      body.AppendLine($"// the native members of the C# structs (src/NetVtk/{own}/*.g.cs)")
        .AppendLine("%inline %{")
        .Append(thunks)
        .AppendLine("%}")
        .AppendLine();
    }

    var functions = NamespaceFunctions(package, module);

    var direct = uses.Select(u => u.Package).ToHashSet();
    // VTK objects: their root's module (SafeDownCast takes a vtkObjectBase*)
    if (classes.Any(c => Kind(c) == WrapKind.Transient)
        && registry.Class(RootTransient) is { Package: var rootModule }
        && rootModule != own)
    {
      direct.Add(rootModule);
    }

    var imports = ImportOrder.Closure(own, direct, importsOf, basesOf);

    var text = new StringBuilder();
    text.Append(GeneratedText.Header)
      .AppendLine(
        $"/* {own}: generated by netvtk-gen from VTK {vtkVersion}. Don't edit; change the generator or its config. */")
      // a module with director classes: SWIG's director support, protected members included
      .AppendLine(classes.Any(IsDirector)
                    ? $"%module(directors=\"1\", dirprot=\"1\") {own}Module"
                    : $"%module {own}Module")
      .AppendLine()
      .AppendLine("%include \"NetVtk.i\"")
      .AppendLine()
      .AppendLine("%{")
      .AppendLine($"#include <{own}_module.hxx>")
      .AppendLine("%}")
      .AppendLine();
    // one flat list: a module SWIG imports doesn't import again, so SWIG meets the packages in this
    // order, the bases of a class before it
    if (imports.Count > 0)
    {
      text.AppendLine("#ifndef SWIGIMPORTED");
      foreach (var import in imports)
      {
        text.AppendLine($"%import \"{import}.i\"");
      }

      text.AppendLine("#endif");
    }

    // every module is in the C# namespace Vtk: no usings to add
    text.AppendLine();
    // the macros its declarations need (address typemaps, constructor keeps): before every
    // declaration that uses them
    foreach (var typemap in module.Typemaps)
    {
      macros.AppendLine(typemap);
    }

    if (macros.Length > 0)
    {
      text.Append(macros).AppendLine();
    }

    if (valueTypes.Count > 0)
    {
      text.Append(LayoutGuards(own, valueTypes)).AppendLine();
    }

    text.Append(enums);
    if (hasExtras)
    {
      text.AppendLine($"%include \"extras/{own}.i\"").AppendLine();
    }

    text.Append(body).Append(functions);
    // the preludes of the packages it uses too: their headers miss the same includes here
    List<string> preludes =
    [
      .. config.Prelude,
      .. uses.Select(u => u.Package)
        .Where(p => p != own)
        .Distinct()
        .Order(StringComparer.Ordinal)
        .SelectMany(p => preludeOf?.Invoke(p) ?? [])
    ];
    var nested = ModuleHeaders.Nested(package);
    var header = ModuleHeaders.Module(package, preludes,
                                      uses.Where(u => u.Package != own).Select(u => u.Header),
                                      nested is not null);
    return new GeneratedModule(GeneratedText.Lf(text.ToString().TrimEnd()) + "\n",
                               GeneratedText.Lf(header), imports, module.Skipped, structs,
                               module.BasePackages, module.Namespaces,
                               nested is null ? null : GeneratedText.Lf(nested));
  }

  private WrapKind Kind(ClassModel c) => registry.Class(c.Name)?.Kind ?? WrapKind.Plain;

  // the typemap macros and features of the package's structs and classes, which go before every
  // declaration
  private StringBuilder ClassMacros(List<ClassModel> valueTypes, List<ClassModel> classes)
  {
    var macros = new StringBuilder();
    foreach (var v in valueTypes)
    {
      macros.AppendLine($"%netvtk_valuetype({v.Name})");
    }

    foreach (var c in classes)
    {
      var kind = Kind(c);
      if (kind == WrapKind.Transient)
      {
        macros.AppendLine($"%vtk_object({c.Name})");
        // a C# constructor for a class VTK's New() creates (VtkObjects.i), unless it has public
        // ones of its own, or is a director, whose C# subclasses call its own constructors
        if (!c.Traits.IsAbstract
            && c.Constructors.Count == 0
            && !IsDirector(c)
            && c.Methods.Any(m => m is { IsStatic: true, Name: "New", Parameters.Count: 0 }))
        {
          macros.AppendLine($"%vtk_new({c.Name})");
        }

        // a director class or one of its bases: a pointer to it may be a C# subclass's object,
        // which C# gets back as itself and keeps alive while VTK holds it (Directors.i)
        if (IsDirected(c.Name))
        {
          macros.AppendLine($"%netvtk_directed({c.Name})");
        }

        if (IsDirector(c))
        {
          macros.AppendLine($"%netvtk_director({c.Name})");
        }
      }

      if (IsConcreteDirected(c))
      {
        macros.AppendLine($"%feature(\"notabstract\") {c.Name};");
      }
      // a const& return is a copy, whatever the class declares: a static member may return one
      // without an object, a class C# may get only that way
      else if (kind == WrapKind.ValueClass)
      {
        macros.AppendLine($"%netvtk_valueclass({c.Name})");
      }

      if (c.IsDeprecated)
      {
        macros.AppendLine($"%typemap(csattributes) {c.Name} {SwigObsolete}");
      }

      // a VTK object's destructor is protected: its proxy drops a reference instead
      // (VtkObjects.i's unref), which needs SWIG's destructor wrapper
      if (!c.Traits.HasPublicDestructor && kind != WrapKind.Transient)
      {
        macros.AppendLine($"%nodefaultdtor {c.Name};");
      }

      // SWIG gives a class that declares no constructor a default one, and default-constructs
      // by-value results; without a usable T(), it must know (the value wrapper copies results
      // instead)
      if (!c.Traits.HasDefaultConstructor)
      {
        macros.AppendLine($"%nodefaultctor {c.Name};")
          .AppendLine($"%feature(\"valuewrapper\") {c.Name};");
      }
      // a move-only class's by-value results go through the value wrapper, which moves them
      else if (IsMoveOnly(c))
      {
        macros.AppendLine($"%feature(\"valuewrapper\") {c.Name};");
      }
      else if (c.Traits.IsAbstract || !c.Traits.IsCreatable)
      {
        macros.AppendLine($"%nodefaultctor {c.Name};");
      }
    }

    return macros;
  }

  private static StringBuilder EnumDeclarations(PackageModel package)
  {
    var enums = new StringBuilder();
    foreach (var e in package.Enums)
    {
      // the C# enum has C++'s underlying type (structs hold it)
      if (e.Underlying is { } underlying && SignatureMapper.CsBuiltin(underlying) is { } csBase)
      {
        enums.AppendLine($"%typemap(csbase) {e.Name} \"{csBase}\"");
      }

      // a nested enum's constants are only unique in its scope: scoped in the .i, where SWIG sees
      // all of them
      enums.AppendLine(
        $"enum {(e.QualifiedName is null && !e.IsScoped ? "" : "class ")}{e.Name} {{");
      foreach (var constant in e.Constants)
      {
        enums.AppendLine($"  {constant.Name} = {constant.Value},");
      }

      enums.AppendLine("};").AppendLine();
    }

    return enums;
  }

  // the package's proxied classes, bases before derived classes
  private List<ClassModel> Classes(PackageModel package, PackageConfig config)
  {
    List<ClassModel> selected =
    [
      .. package.Classes.Where(c => IsSelected(c, config) && Kind(c) != WrapKind.ValueType)
    ];

    List<ClassModel> ordered = [];
    HashSet<string> placed = [];

    void Place(ClassModel c)
    {
      if (!placed.Add(c.Name))
      {
        return;
      }

      foreach (var ancestor in c.Ancestors)
      {
        if (selected.FirstOrDefault(s => s.Name == ancestor) is { } local)
        {
          Place(local);
        }
      }

      ordered.Add(c);
    }

    foreach (var c in selected)
    {
      Place(c);
    }

    return ordered;
  }

  // every field type below the fields (array elements, members of nested classes)
  private static IEnumerable<CppType> FieldTypes(IEnumerable<FieldModel> fields) =>
    fields.SelectMany(f => FieldTypes(f.Members ?? [])
                        .Prepend(f.Type is ArrayType array ? array.Innermost : f.Type));

  // libclang's layout, checked by the C++ compiler; the managed layout tests compare the C# structs
  // with NetVtk_SizeOf_*
  private string LayoutGuards(string own, List<ClassModel> valueTypes)
  {
    var text = new StringBuilder().AppendLine("%{")
      .AppendLine(
        $"// Layout guards for the C# structs in src/NetVtk/{own}/: a mismatch fails the native build.");
    foreach (var v in valueTypes)
    {
      var layout = v.Layout
                   ?? throw new InvalidOperationException(
                     $"{own}: no layout for value type {v.Name}");
      text.AppendLine(
        $"static_assert(sizeof({v.Name}) == {layout.Size} && alignof({v.Name}) == {layout.Align} && std::is_trivially_copyable<{v.Name}>::value, \"{v.Name} layout\");");
    }

    // the structs hold enums as C# enums (their underlying type, int by default) and bool as byte
    foreach (var e in valueTypes.SelectMany(v => FieldTypes(v.Fields ?? []))
               .OfType<NamedType>()
               .Where(t => t.Kind == NamedKind.Enum)
               .Select(t => t.Name)
               .Distinct()
               .Order(StringComparer.Ordinal))
    {
      var underlying = registry.Enum(e)?.Underlying ?? "int";
      text.AppendLine(
        $"static_assert(sizeof({e}) == sizeof({underlying}), \"{e}: a{(underlying == "int" ? "n" : "")} {SignatureMapper.CsBuiltin(underlying) ?? underlying} in C#\");");
    }

    if (valueTypes.SelectMany(v => FieldTypes(v.Fields ?? []))
        .Any(t => t is BuiltinType { Name: "bool" }))
    {
      text.AppendLine("static_assert(sizeof(bool) == 1, \"bool: a byte in C#\");");
    }

    text.AppendLine("%}")
      .AppendLine()
      .AppendLine("%inline %{")
      .AppendLine("// sizes for the managed layout tests");
    foreach (var v in valueTypes)
    {
      text.AppendLine($"inline int NetVtk_SizeOf_{v.Name}() {{ return (int)sizeof({v.Name}); }}");
    }

    return text.AppendLine("%}").ToString();
  }
}
