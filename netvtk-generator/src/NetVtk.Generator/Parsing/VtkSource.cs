// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetVtk.Generator.Parsing;

/// <summary>
/// An installed VTK as the generator needs it (the install prefix: <c>include/vtk-X.Y</c>,
/// <c>share/vtk</c>, <c>bin</c>): its modules and their public headers, from the module properties
/// VTK installs (<c>share/vtk/VTK-vtk-module-properties.cmake</c>), and its version
/// (<c>vtkVersionMacros.h</c>).
/// </summary>
internal sealed partial class VtkSource
{
  private readonly Dictionary<string, Module> _modules = new(StringComparer.Ordinal);

  /// <param name="prefix">The install prefix vcpkg (or VTK's own install) made.</param>
  public VtkSource(string prefix)
  {
    Prefix = Path.GetFullPath(prefix);
    var properties = Path.Combine(Prefix, "share", "vtk", "VTK-vtk-module-properties.cmake");
    if (!File.Exists(properties))
    {
      throw new FileNotFoundException(
        $"{properties} missing: --vtk names VTK's install prefix (include/vtk-X.Y, share/vtk)",
        properties);
    }

    foreach (Match match in Property().Matches(File.ReadAllText(properties)))
    {
      var module = match.Groups["module"].Value;
      if (!_modules.TryGetValue(module, out var entry))
      {
        _modules[module] = entry = new Module();
      }

      var values = match.Groups["value"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
      switch (match.Groups["property"].Value)
      {
        case "headers":
          entry.Headers =
            [.. values.Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
          break;
        case "depends":
          entry.Depends = [.. values.Select(Unqualified)];
          break;
      }
    }

    var include =
      Directory.EnumerateDirectories(Path.Combine(Prefix, "include"), "vtk-*")
        .Order(StringComparer.Ordinal)
        .LastOrDefault()
      ?? throw new DirectoryNotFoundException(
        $"no include/vtk-X.Y under {Prefix}: --vtk names VTK's install prefix");
    Include = include;
    var macros = Path.Combine(include, "vtkVersionMacros.h");
    Version =
      File.Exists(macros)
      && VersionMacro().Match(File.ReadAllText(macros)) is { Success: true } version
        ? version.Groups[1].Value
        : throw new InvalidDataException($"{macros}: no VTK_VERSION");
  }

  public string Prefix { get; }

  /// <summary>VTK's headers: <c>include/vtk-X.Y</c>.</summary>
  public string Include { get; }

  /// <summary>
  /// What a parse includes from: VTK's headers, and the include directories of the third-party
  /// libraries VTK builds in (<c>vtkscn/include</c>), which its CMake targets add as theirs.
  /// </summary>
  public IReadOnlyList<string> IncludeDirectories =>
  [
    Include,
    .. Directory.EnumerateDirectories(Include)
      .Select(d => Path.Combine(d, "include"))
      .Where(Directory.Exists)
      .Order(StringComparer.Ordinal)
  ];

  /// <summary>
  /// VTK's shared libraries, whose export tables tell which members exist (Windows).
  /// </summary>
  public string Libraries => Path.Combine(Prefix, "bin");

  /// <summary>The VTK version the headers are (<c>9.7.1</c>).</summary>
  public string Version { get; }

  /// <summary>Every module the install has, by its name without <c>VTK::</c>.</summary>
  public IReadOnlyCollection<string> Modules => _modules.Keys;

  public bool Has(string module) => _modules.ContainsKey(module);

  /// <summary>
  /// A module's public headers, sorted: file names in the flat include directory.
  /// </summary>
  public IReadOnlyList<string> Headers(string module) => Locate(module).Headers;

  /// <summary>The modules a module's public API depends on.</summary>
  public IReadOnlyList<string> Depends(string module) => Locate(module).Depends;

  private Module Locate(string module) =>
    _modules.TryGetValue(module, out var entry)
      ? entry
      : throw new KeyNotFoundException(
        $"VTK has no module {module} (its install lists {_modules.Count}; the port builds the configured ones)");

  private static string Unqualified(string module) =>
    module.StartsWith("VTK::", StringComparison.Ordinal) ? module[5..] : module;

  // set_property(TARGET "VTK::CommonCore" PROPERTY "INTERFACE_vtk_module_headers" "...")
  [GeneratedRegex(
    """set_property\(TARGET "VTK::(?<module>\w+)" PROPERTY "INTERFACE_vtk_module_(?<property>\w+)" "(?<value>[^"]*)"\)""")]
  private static partial Regex Property();

  [GeneratedRegex("""#define VTK_VERSION "(\d+\.\d+\.\d+)""")]
  private static partial Regex VersionMacro();

  private sealed class Module
  {
    public IReadOnlyList<string> Headers { get; set; } = [];

    public IReadOnlyList<string> Depends { get; set; } = [];
  }
}
