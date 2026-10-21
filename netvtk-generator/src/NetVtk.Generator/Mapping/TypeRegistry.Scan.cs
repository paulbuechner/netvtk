// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetVtk.Generator.Mapping;

// the part of TypeRegistry that reads the modules a run doesn't write from their .i files
internal sealed partial class TypeRegistry
{
  /// <summary>
  /// Reads what a module this run doesn't write provides, as a run writing it would know it:
  /// classes with their kind and move-only status (%nodefaultctor, the value wrapper), value types,
  /// enums with their underlying type (csbase), the flat names of nested types with the header that
  /// declares them (<c>&lt;Pkg&gt;_nested.hxx</c> next to the .i's <c>headers</c>), namespace
  /// shims, imports and bases.
  /// </summary>
  public void ScanModule(string interfaceFile)
  {
    var package = Path.GetFileNameWithoutExtension(interfaceFile);
    var text = File.ReadAllText(interfaceFile);
    _imports[package] = [.. ImportPattern().Matches(text).Select(m => m.Groups[1].Value)];
    _bases[package] = [.. BasePattern().Matches(text).Select(m => m.Groups[1].Value)];

    // the nested header's aliases: flat name -> C++ name, declared there
    var nestedHeader = $"{package}_nested.hxx";
    var nestedPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(interfaceFile))!, "..",
                                  "headers", nestedHeader);
    Dictionary<string, string> nested = File.Exists(nestedPath)
      ? NestedPattern()
        .Matches(File.ReadAllText(nestedPath))
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value)
      : [];
    string? HeaderOf(string name) => nested.ContainsKey(name) ? nestedHeader : null;

    HashSet<string> Named(string macro) =>
    [
      .. MacroPattern()
        .Matches(text)
        .Where(m => m.Groups[1].Value == macro)
        .Select(m => m.Groups[2].Value)
    ];

    var vtkObjects = Named("vtk_object").ToHashSet();
    var valueClasses = Named("netvtk_valueclass");
    HashSet<string> noDefault =
      [.. NoDefaultPattern().Matches(text).Select(m => m.Groups[1].Value)];
    HashSet<string> wrapped =
      [.. ValueWrapperPattern().Matches(text).Select(m => m.Groups[1].Value)];
    foreach (var name in Named("netvtk_valuetype"))
    {
      AddClass(new KnownClass(name, package, WrapKind.ValueType, HeaderOf(name)));
    }

    foreach (Match m in ClassPattern().Matches(text))
    {
      var name = m.Groups[1].Value;
      var kind = vtkObjects.Contains(name) ? WrapKind.VtkObject :
        valueClasses.Contains(name) ? WrapKind.ValueClass : WrapKind.Plain;
      // InterfaceWriter.ClassMacros: both without a default constructor, the value wrapper alone
      // for a move-only class
      AddClass(new KnownClass(name, package, kind, HeaderOf(name),
                              IsMoveOnly: wrapped.Contains(name) && !noDefault.Contains(name)));
    }

    Dictionary<string, string> csBases = CsBasePattern()
      .Matches(text)
      .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    foreach (Match m in EnumPattern().Matches(text))
    {
      var name = m.Groups[1].Value;
      AddEnum(name, package, HeaderOf(name),
              csBases.TryGetValue(name, out var csBase) ? SignatureMapper.CppBuiltin(csBase) : null,
              nested.GetValueOrDefault(name));
    }

    // %rename(vtk) vtk::NetVtk_vtk: the static shim for a namespace's functions
    foreach (Match m in ShimPattern().Matches(text))
    {
      AddClass(new KnownClass(m.Groups[1].Value, package, WrapKind.Plain));
    }
  }

  [GeneratedRegex(@"^%import ""(\w+)\.i""", RegexOptions.Multiline)]
  private static partial Regex ImportPattern();

  [GeneratedRegex(@"^class (\w+)\b", RegexOptions.Multiline)]
  private static partial Regex ClassPattern();

  [GeneratedRegex(@"^class \w+ : public (\w+) \{", RegexOptions.Multiline)]
  private static partial Regex BasePattern();

  // enum X, and a nested one's enum class X
  [GeneratedRegex(@"^enum (?:class )?(\w+)\b", RegexOptions.Multiline)]
  private static partial Regex EnumPattern();

  [GeneratedRegex(@"^%rename\((\w+)\) (?:\w+::)*NetVtk_\w+;", RegexOptions.Multiline)]
  private static partial Regex ShimPattern();

  // %vtk_object(X), %netvtk_valueclass(X), ...: the macro and the class it's called for
  [GeneratedRegex(@"^%((?:vtk|netvtk)_\w+)\((\w+)[,)]", RegexOptions.Multiline)]
  private static partial Regex MacroPattern();

  [GeneratedRegex(@"^%nodefaultctor (\w+);", RegexOptions.Multiline)]
  private static partial Regex NoDefaultPattern();

  [GeneratedRegex(@"^%feature\(""valuewrapper""\) (\w+);", RegexOptions.Multiline)]
  private static partial Regex ValueWrapperPattern();

  [GeneratedRegex(@"^%typemap\(csbase\) (\w+) ""(\w+)""", RegexOptions.Multiline)]
  private static partial Regex CsBasePattern();

  // using vtkCommand_EventIds = enum vtkCommand::EventIds; (an unscoped enum elaborated, which a
  // run knows as vtkCommand::EventIds)
  [GeneratedRegex(@"^using (\w+) = (?:enum )?(.+);$", RegexOptions.Multiline)]
  private static partial Regex NestedPattern();
}
