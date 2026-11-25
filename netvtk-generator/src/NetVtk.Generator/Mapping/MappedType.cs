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
/// <param name="Sized">
/// A <c>T*&amp;</c> parameter a VTK size hint names: the typemaps the declaration makes for it by
/// its name (<c>%netvtk_sized_ref</c>) before, and clears after.
/// </param>
internal sealed record MappedType(string Spelling, IReadOnlyList<TypeUse> Uses, string CsKey,
                                  string CsType, string? Typemap = null, CppType? Declared = null,
                                  string? Apply = null, SizedReference? Sized = null)
{
  /// <summary>The parameter declaration in the .i.</summary>
  public string Declare(string name) => Declared?.Declare(name) ?? $"{Spelling} {name}";
}

/// <summary>
/// A pointer C++ hands back through a <c>T*&amp;</c> parameter, as many elements as a VTK size hint
/// counts (<c>VTK_SIZEHINT(pts, npts)</c>): a C# array, copied right after the call
/// (VtkObjects.i's <c>%netvtk_sized_ref</c>).
/// </summary>
/// <param name="Pointee">
/// The pointer's target as the declaration spells it: <c>const vtkIdType</c>.
/// </param>
/// <param name="CsElement">The C# type of an element.</param>
/// <param name="Size">
/// The count in the wrapper's terms: its arguments (<c>(*arg3)</c> for a reference), its result,
/// numbers.
/// </param>
internal sealed record SizedReference(string Pointee, string CsElement, string Size)
{
  /// <summary>The macro before the declaration, the parameter by its declared name.</summary>
  public string Macro(string name) => $"%netvtk_sized_ref({Pointee}, {name}, {CsElement}, {Size})";

  /// <summary>What clears the macro's typemaps after the declaration.</summary>
  public string Clear(string name) => $"{Pointee} *&{name}";
}

/// <summary>Either a mapped type or the reason it can't be wrapped (for the skip log).</summary>
internal readonly record struct TypeMapping(MappedType? Type, string? Skip)
{
  public static TypeMapping Of(string spelling, string csKey, string csType,
                               params TypeUse[] uses) =>
    new(new MappedType(spelling, uses, csKey, csType), null);

  public static TypeMapping Skipped(string reason) => new(null, reason);
}
