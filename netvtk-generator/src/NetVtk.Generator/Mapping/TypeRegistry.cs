// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

//
using NetVtk.Generator.Model;

namespace NetVtk.Generator.Mapping;

/// <summary>How netvtk-core wraps a class (see its CLAUDE.md, "Wrapper contract").</summary>
internal enum WrapKind
{
  /// <summary>vtkObjectBase subclass: %vtk_object, the proxy owns a reference.</summary>
  Transient,

  /// <summary>Copyable class: %netvtk_valueclass, const&amp; returns are owned copies.</summary>
  ValueClass,

  /// <summary>
  /// C# struct with the C++ layout (%netvtk_valuetype): generated, with a hand-written partial for
  /// managed members.
  /// </summary>
  ValueType,

  /// <summary>Other classes (not copyable, or abstract): plain proxies.</summary>
  Plain,
}

/// <param name="Header">The header that declares the class; null: the one named after it.</param>
/// <param name="IsMoveOnly">
/// Not copyable, but movable: a by-value return moves into the proxy (SwigValueWrapper).
/// </param>
internal sealed record KnownClass(string Name, string Package, WrapKind Kind, string? Header = null,
                                  bool IsMoveOnly = false)
{
  public string DeclaringHeader => Header ?? $"{Name}.h";

  /// <summary>What a declaration using the class needs: its module and header.</summary>
  public TypeUse Use => new(Package, DeclaringHeader);
}

/// <param name="Header">The header that declares the enum; null: the one named after it.</param>
/// <param name="Underlying">
/// The underlying type when it isn't int (unsigned char), otherwise null.
/// </param>
/// <param name="QualifiedName">
/// C++'s name of a nested or namespace enum, whose flat name is an alias.
/// </param>
internal sealed record KnownEnum(string Name, string Package, string? Header = null,
                                 string? Underlying = null, string? QualifiedName = null)
{
  public string DeclaringHeader => Header ?? $"{Name}.h";

  /// <summary>What a declaration using the enum needs: its module and header.</summary>
  public TypeUse Use => new(Package, DeclaringHeader);
}

/// <summary>
/// Every type the generated code may use, and where it lives. Filled from the packages being
/// generated and from the .i files of the modules a run doesn't write (<see cref="ScanModule"/>).
/// </summary>
internal sealed partial class TypeRegistry
{
  private readonly Dictionary<string, KnownClass> _classes = [];
  private readonly Dictionary<string, KnownEnum> _enums = [];
  private readonly Dictionary<string, string> _instances = [];
  private readonly Dictionary<string, List<string>> _imports = [];
  private readonly Dictionary<string, List<string>> _bases = [];

  public void AddClass(KnownClass known) => _classes[known.Name] = known;

  public void AddEnum(string name, string package, string? header = null, string? underlying = null,
                      string? qualifiedName = null) =>
    _enums[name] = new KnownEnum(name, package, header, underlying, qualifiedName);

  /// <summary>
  /// Registers a class template instance as the class of that name (<see cref="ClassInstance"/>).
  /// </summary>
  public void AddInstance(NamedType type, string name) =>
    _instances[(type with { Const = false }).Spelling] = name;

  /// <summary>
  /// A signature type with every class template instance that is a class of its own
  /// (<see cref="AddInstance"/>) named by that class, whose C++ alias the generated code spells:
  /// SWIG takes the alias and the template for different types.
  /// </summary>
  public CppType Resolve(CppType type) => type switch
  {
    NamedType { TemplateArguments.Count: > 0 } n when _instances.TryGetValue(
      (n with { Const = false }).Spelling,
      out var name) => new NamedType(name, n.Kind, [], n.Const),
    NamedType { TemplateArguments.Count: > 0 } n => n with
    {
      TemplateArguments = [.. n.TemplateArguments.Select(Resolve)]
    },
    PointerType p => p with { Pointee = Resolve(p.Pointee) },
    ReferenceType r => r with { Referee = Resolve(r.Referee) },
    ArrayType a => a with { Element = Resolve(a.Element) },
    FunctionType f => f with
    {
      Return = Resolve(f.Return), Parameters = [.. f.Parameters.Select(Resolve)]
    },
    _ => type,
  };

  public KnownClass? Class(string name) => _classes.GetValueOrDefault(name);

  public KnownEnum? Enum(string name) => _enums.GetValueOrDefault(name);

  /// <summary>The %import list of a module this run doesn't write (its dependencies).</summary>
  public IReadOnlyList<string> ImportsOf(string package) =>
    _imports.GetValueOrDefault(package) ?? [];

  /// <summary>
  /// The other packages the classes of a module this run doesn't write derive from.
  /// </summary>
  public IReadOnlyCollection<string> BasePackagesOf(string package) =>
  [
    .. (_bases.GetValueOrDefault(package) ?? []).Select(b => Class(b)?.Package)
    .OfType<string>()
    .Where(p => p != package)
    .Distinct()
  ];
}
