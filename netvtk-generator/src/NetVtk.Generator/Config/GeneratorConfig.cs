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
  /// The native libraries (<c>NetVtk&lt;Library&gt;</c>) and the VTK modules whose wrappers each
  /// holds, in build order: what generate writes. A Windows DLL exports at most 65535 functions.
  /// </summary>
  public Dictionary<string, List<string>> Libraries { get; set; } = [];

  /// <summary>
  /// Header patterns (<c>*</c> any characters) left out of every module: one platform's classes
  /// (VTK installs only the platform's, and the output must build everywhere).
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

  public bool IsExcludedHeader(string header) =>
    ExcludeHeaders.Any(p => Regex.IsMatch(
                         header, "^" + Regex.Escape(p).Replace(@"\*", ".*") + "$"));

  public static GeneratorConfig Load(string path)
  {
    var config =
      new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build()
        .Deserialize<GeneratorConfig>(File.ReadAllText(path))
      ?? new GeneratorConfig();
    var twice = config.Generate.GroupBy(m => m)
      .Where(g => g.Count() > 1)
      .Select(g => g.Key)
      .ToList();
    return twice.Count == 0
      ? config
      : throw new InvalidDataException(
        $"{path}: modules in more than one library: {string.Join(", ", twice)}");
  }
}

internal sealed class PackageConfig
{
  /// <summary>
  /// Only these classes; null means every class of the module, an empty list means enums only.
  /// </summary>
  public List<string>? Classes { get; set; }

  public List<string> ExcludeClasses { get; set; } = [];

  /// <summary>
  /// <c>Class::Method</c> and <c>Namespace::Function</c> entries left out, all overloads.
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
