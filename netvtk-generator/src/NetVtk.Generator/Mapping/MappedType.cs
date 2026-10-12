// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

//
using NetVtk.Generator.Model;

namespace NetVtk.Generator.Mapping;

/// <summary>
/// A type the generated code needs: its module (for %import) and header (for the module header).
/// </summary>
internal sealed record TypeUse(string Package, string Header);

/// <param name="Spelling">
/// The type as the .i declares it (C++ spelling the netvtk-core typemaps match).
/// </param>
/// <param name="CsKey">What the type becomes in a C# signature, for overload collisions.</param>
/// <param name="CsType">
/// The type in a C# signature as the typemaps produce it, parameter modifier included: <c>in
/// vtkVector3d</c>, <c>ref double</c>, <c>string</c>. Returns never have a modifier.
/// </param>
/// <param name="Typemap">
/// A typemap macro the module calls before its declarations, for a type the typemap library can't
/// cover generically: <c>%netvtk_address(%arg(double*))</c>.
/// </param>
/// <param name="Declared">
/// The type a parameter is declared with when that isn't <c>Spelling name</c>: a C array, <c>const
/// double matrix[3][3]</c>.
/// </param>
/// <param name="Apply">
/// A named typemap the declaration applies to its return (<c>double *NETVTK_SIZE6</c>, a VTK size
/// hint): <c>%apply</c> before the declaration, <c>%clear</c> after.
/// </param>
internal sealed record MappedType(string Spelling, IReadOnlyList<TypeUse> Uses, string CsKey,
                                  string CsType, string? Typemap = null, CppType? Declared = null,
                                  string? Apply = null)
{
  /// <summary>The parameter declaration in the .i.</summary>
  public string Declare(string name) => Declared?.Declare(name) ?? $"{Spelling} {name}";
}

/// <summary>Either a mapped type or the reason it can't be wrapped (for the skip log).</summary>
internal readonly record struct TypeMapping(MappedType? Type, string? Skip)
{
  public static TypeMapping Of(string spelling, string csKey, string csType,
                               params TypeUse[] uses) =>
    new(new MappedType(spelling, uses, csKey, csType), null);

  public static TypeMapping Skipped(string reason) => new(null, reason);
}
