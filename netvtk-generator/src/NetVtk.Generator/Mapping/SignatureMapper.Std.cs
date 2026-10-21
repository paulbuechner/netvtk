// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetVtk.Generator.Model;

namespace NetVtk.Generator.Mapping;

// the part of SignatureMapper that maps the standard library types the common typemaps cover
internal sealed partial class SignatureMapper
{
  // the width of a bitset a ulong holds (N <= 64), as C++ spells it, or null
  private static string? BitsetWidth(NamedType n) =>
    n.TemplateArguments is [ConstantArgument { Text: var bits }]
    && int.TryParse(bits, out var count)
    && count <= 64
      ? bits
      : null;

  /// <summary>
  /// A standard library type netvtk-core's typemaps cover (Strings.i: strings and string views by
  /// value or const&amp;, a const&amp; string stream's text; Std.i: stream positions by value,
  /// bitsets, optional values, arrays of numbers and complex numbers by value or const&amp;, the
  /// last two by non-const &amp; too, vectors (<see cref="Vector"/>) by value or reference). A
  /// bitset, array, optional or vector needs its instantiation's macro in the module
  /// (<see cref="MappedType.Typemap"/>).
  /// </summary>
  private TypeMapping Std(NamedType n, bool byMutableReference, bool reference)
  {
    var (konst, suffix) = (reference && !byMutableReference ? "const " : "", reference ? "&" : "");
    // libstdc++ declares the strings in an inline namespace
    var narrow = n.TemplateArguments is [BuiltinType { Name: "char" }, ..];
    switch (n.Name.Replace("::__cxx11::", "::", StringComparison.Ordinal))
    {
      case "std::basic_string" when narrow && !byMutableReference:
        return TypeMapping.Of($"{konst}std::string{suffix}", "string", "string");
      case "std::basic_string_view" when narrow && !byMutableReference:
        return TypeMapping.Of($"{konst}std::string_view{suffix}", "string", "string");
      case "std::basic_stringstream" when narrow && reference && !byMutableReference:
        return TypeMapping.Of("const std::stringstream&", "string", "string");
      case "std::fpos" when !reference:
        return TypeMapping.Of("std::streampos", "long", "long");
      case "std::complex" when n.TemplateArguments is [BuiltinType { Name: "double" }]:
        return byMutableReference
          ? TypeMapping.Of("std::complex<double>&", "ref Complex", "ref global::Vtk.Complex")
          : TypeMapping.Of($"{konst}std::complex<double>{suffix}", "Complex",
                           reference ? "in global::Vtk.Complex" : "global::Vtk.Complex");
      case "std::bitset" when !byMutableReference && BitsetWidth(n) is { } bits:
        return new TypeMapping(
          new MappedType($"{konst}std::bitset< {bits} >{suffix}", [], "ulong", "ulong",
                         $"%netvtk_bitset({bits})"), null);
      case "std::array"
        when n.TemplateArguments is [BuiltinType element, ConstantArgument { Text: var length }]
             && ArrayElement(element) is { } cs:
        return new TypeMapping(
          new MappedType($"{konst}std::array< {element.Name}, {length} >{suffix}", [], $"{cs}[]",
                         $"{cs}[]", $"%netvtk_std_array(%arg({element.Name}), {length}, {cs})"),
          null);
      case "std::optional" when !byMutableReference
                                && n.TemplateArguments is [var value]
                                && OptionalValue(value) is var (type, csType, uses):
        return new TypeMapping(
          new MappedType($"{konst}std::optional< {type} >{suffix}", uses, $"{csType}?",
                         $"{csType}?", $"%netvtk_optional(%arg({type}), {csType})"), null);
      case "std::vector":
        return Vector(n, konst, suffix, writesBack: byMutableReference);
      default:
        return TypeMapping.Skipped($"std type {n.Name}");
    }
  }

  // what a vector's C# array holds, for the skip log
  private const string VectorKinds =
    "C# arrays of vectors hold numbers, strings, structs, value classes and VTK objects";

  /// <param name="Spelling">The elements as the vector spells them.</param>
  /// <param name="Typemap">
  /// The macro of the vector's typemaps; none for bools and strings, which Std.i has.
  /// </param>
  /// <param name="IsRawObject">
  /// VTK objects by pointer, whose references a field holding them wouldn't count.
  /// </param>
  private sealed record VectorElements(string Spelling, string CsType, string? Typemap,
                                       IReadOnlyList<TypeUse> Uses, bool IsRawObject = false);

  /// <summary>
  /// A <c>std::vector</c> as Std.i's typemaps pass it: a C# array of its elements, copied both
  /// ways; spelled <c>std::vector&lt; T &gt;</c>, <paramref name="konst"/> before it,
  /// <paramref name="declarator"/> (<c>&amp;</c>, <c>*</c> or none) after. A parameter C++ writes
  /// back (a non-const reference or pointer) is a ref of the array.
  /// </summary>
  private TypeMapping Vector(NamedType vector, string konst, string declarator, bool writesBack)
  {
    var (elements, skip) = ElementsOf(vector);
    if (elements is null)
    {
      return TypeMapping.Skipped(skip!);
    }

    var csType = writesBack ? $"ref {elements.CsType}[]" : $"{elements.CsType}[]";
    return new TypeMapping(
      new MappedType($"{konst}std::vector< {elements.Spelling} >{declarator}", elements.Uses,
                     csType, csType, elements.Typemap), null);
  }

  // a vector's elements: numbers C# arrays hold as C++ does (the vector spells a width typedef as
  // written: std::vector<size_t>, whose width differs per platform, isn't one), bools, strings,
  // structs, value classes and VTK objects, by pointer or vtkSmartPointer
  private (VectorElements? Elements, string? Skip) ElementsOf(NamedType vector) =>
    vector.TemplateArguments.FirstOrDefault() switch
    {
      BuiltinType { Name: "bool" } => (new VectorElements("bool", "bool", null, []), null),
      BuiltinType b when ArrayElements.TryGetValue(b.Name, out var cs) => (
        new VectorElements(b.Written ?? b.Name, cs, $"%netvtk_vector({b.Written ?? b.Name}, {cs})",
                           []), null),
      NamedType s when IsNarrowString(s) => (new VectorElements("std::string", "string", null, []),
        null),
      NamedType
      {
        Name: "vtkSmartPointer", TemplateArguments: [NamedType { TemplateArguments.Count: 0 } t]
      } when registry.Class(t.Name) is { Kind: WrapKind.VtkObject } known => (
        new VectorElements($"vtkSmartPointer< {t.Name} >", t.Name,
                           $"%netvtk_object_vector({t.Name})", [known.Use]), null),
      PointerType
      {
        Pointee: NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0, IsConst: false } t
      } when registry.Class(t.Name) is { Kind: WrapKind.VtkObject } known => (
        new VectorElements($"{t.Name}*", t.Name, $"%netvtk_object_vector({t.Name})", [known.Use],
                           IsRawObject: true), null),
      NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0, IsConst: false } c when
        registry.Class(c.Name) is { Kind: WrapKind.ValueType } known => (
          new VectorElements(c.Name, c.Name, $"%netvtk_vector({c.Name}, {c.Name})", [known.Use]),
          null),
      NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0, IsConst: false } c when
        registry.Class(c.Name) is { Kind: WrapKind.ValueClass } known => (
          new VectorElements(c.Name, c.Name, $"%netvtk_valueclass_vector({c.Name})", [known.Use]),
          null),
      { } element => (null, $"a vector of {element.Spelling}: {VectorKinds}"),
      null => (null, $"std type {vector.Name}"),
    };

  // std::string, as libclang spells it (libstdc++ in an inline namespace)
  private static bool IsNarrowString(NamedType n) =>
    n.Name.Replace("::__cxx11::", "::", StringComparison.Ordinal) == "std::basic_string"
    && n.TemplateArguments is [BuiltinType { Name: "char" }, ..];

  // what a value class's field becomes, for the skip log
  private const string FieldKinds =
    "C# properties are numbers, enums, strings, structs, value classes, VTK objects, addresses, arrays of numbers or structs and vectors";

  /// <summary>
  /// A value class's field of a standard library type (<see cref="Field"/>): a string, read and
  /// written (Strings.i), a const one read only; an array of numbers, a C# array of its length
  /// through its macro, as for parameters; a vector, a C# array (<see cref="Vector"/>) read and
  /// written, a const one, and one of VTK objects by pointer, whose references the field wouldn't
  /// count, read only. Others are skipped by their templates' names, as their parameters' skips
  /// name them.
  /// </summary>
  private TypeMapping StdField(NamedType n, string constant)
  {
    if (IsNarrowString(n))
    {
      return TypeMapping.Of($"{constant}std::string", "string", "string");
    }

    if (n is
        {
          Name: "std::array", TemplateArguments: [BuiltinType element, ConstantArgument length]
        }
        && ArrayElement(element) is { } cs)
    {
      return new TypeMapping(
        new MappedType($"{constant}std::array< {element.Name}, {length.Text} >", [], $"{cs}[]",
                       $"{cs}[]", $"%netvtk_std_array(%arg({element.Name}), {length.Text}, {cs})"),
        null);
    }

    if (n.Name != "std::vector")
    {
      return TypeMapping.Skipped($"a field of {n.Name}: {FieldKinds}");
    }

    var raw = ElementsOf(n).Elements is { IsRawObject: true };
    var mapped = Vector(n, raw ? "const " : constant, "", writesBack: false);
    return mapped.Type is null ? TypeMapping.Skipped($"a field of {mapped.Skip}") : mapped;
  }

  // a number C# arrays hold as C++ does (pinned, copied as bytes; Types.i's %netvtk_array list):
  // not bool, which marshals differently, nor a 64-bit integer, which an instance spells by its
  // canonical name, not the platform's
  private static string? ArrayElement(BuiltinType element) =>
    BlittableBuiltins.Contains(element.Name) ? Builtins[element.Name] : null;

  // the value of an optional Std.i covers: a number, an enum or a struct. A nested enum goes by
  // C++'s name: in template arguments SWIG writes "enum X", which a flat alias can't follow.
  private (string Type, string CsType, TypeUse[] Uses)? OptionalValue(CppType value) => value switch
  {
    BuiltinType b when ArrayElement(b) is { } cs => (b.Name, cs, []),
    NamedType { Kind: NamedKind.Enum } e when registry.Enum(e.Name) is { } known => (
      known.QualifiedName ?? e.Name, e.Name, [known.Use]),
    NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } c when registry.Class(c.Name) is
      { Kind: WrapKind.ValueType } known => (c.Name, c.Name, [known.Use]),
    _ => null,
  };
}
