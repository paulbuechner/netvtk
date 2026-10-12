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
  /// last two by non-const &amp; too). A bitset, array or optional needs its instantiation's macro
  /// in the module (<see cref="MappedType.Typemap"/>).
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
      default:
        return TypeMapping.Skipped($"std type {n.Name}");
    }
  }

  // a number C# arrays hold as C++ does (pinned, copied as bytes; Types.i's %netvtk_array list):
  // not bool, which marshals differently, nor a 64-bit integer, whose canonical spelling (template
  // arguments are canonical) differs per platform
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
