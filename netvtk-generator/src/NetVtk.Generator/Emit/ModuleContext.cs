// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

//
using NetVtk.Generator.Config;
using NetVtk.Generator.Mapping;

namespace NetVtk.Generator.Emit;

/// <summary>
/// A generated module: its .i, its aggregate header, its value-type structs, the modules it imports
/// and what it left out.
/// </summary>
/// <param name="Structs">
/// Its value types' C# structs (src/NetVtk/&lt;Pkg&gt;/*.g.cs), whose thunks are in the .i.
/// </param>
/// <param name="BasePackages">
/// The other packages its classes derive from, which its importers import first.
/// </param>
/// <param name="Namespaces">The C++ namespaces whose functions C# gets as a static class.</param>
/// <param name="NestedHeader">
/// The C++ aliases of the package's nested and namespace types
/// (<see cref="ModuleHeaders.NestedHeaderOf"/>), if it has any.
/// </param>
internal sealed record GeneratedModule(string Interface, string ModuleHeader,
                                       IReadOnlyList<string> Imports, IReadOnlyList<string> Skipped,
                                       IReadOnlyList<GeneratedStruct> Structs,
                                       IReadOnlyCollection<string> BasePackages,
                                       IReadOnlyList<string> Namespaces,
                                       string? NestedHeader = null);

/// <summary>
/// What writing one module collects: the types it uses, the typemap macros it calls, the members it
/// leaves out.
/// </summary>
internal sealed class ModuleContext(string package, PackageConfig config)
{
  public string Package { get; } = package;

  public PackageConfig Config { get; } = config;

  /// <summary>
  /// The types the module uses: its imports and the headers of its module header.
  /// </summary>
  public HashSet<TypeUse> Uses { get; } = [];

  /// <summary>
  /// Macros the module calls before its declarations: typemaps its types need
  /// (<see cref="MappedType.Typemap"/>: addresses) and the keeps of constructors
  /// (<c>%netvtk_keep_construct</c>).
  /// </summary>
  public SortedSet<string> Typemaps { get; } = new(StringComparer.Ordinal);

  /// <summary>The skip log: what the module leaves out, and why.</summary>
  public List<string> Skipped { get; } = [];

  /// <summary>
  /// The other packages the module's classes derive from, which its importers must import first.
  /// </summary>
  public SortedSet<string> BasePackages { get; } = new(StringComparer.Ordinal);

  /// <summary>The C++ namespaces whose functions C# gets as a static class.</summary>
  public List<string> Namespaces { get; } = [];

  /// <summary>Records what a type in an emitted declaration needs.</summary>
  public void Use(MappedType type)
  {
    Uses.UnionWith(type.Uses);
    if (type.Typemap is { } typemap)
    {
      Typemaps.Add(typemap);
    }
  }
}
