// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//
using NetVtk.Generator.Config;
using NetVtk.Generator.Parsing;

namespace NetVtk.Generator;

/// <summary>
/// What generate, docs and dump share: config/modules.yaml, the installed VTK (its modules and
/// their headers), the modules the config names, and the parallel parse.
/// </summary>
internal sealed class Workspace
{
  /// <param name="vtkPrefix">VTK's install prefix (see <see cref="VtkSource"/>).</param>
  public Workspace(string configPath, string vtkPrefix)
  {
    ConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
    Config = GeneratorConfig.Load(configPath);
    Source = new VtkSource(vtkPrefix);
    MsvcEnvironment.Ensure();
    foreach (var module in Config.Generate.Where(m => !Source.Has(m)))
    {
      throw new KeyNotFoundException(
        $"config: VTK has no module {module} (the port's NETVTK_MODULES builds it?)");
    }

    Configured = [.. Config.Generate];
  }

  public string ConfigDirectory { get; }

  public GeneratorConfig Config { get; }

  public VtkSource Source { get; }

  /// <summary>The modules the config generates, in its libraries' order.</summary>
  public List<string> Configured { get; }

  /// <summary>
  /// Names as modules, each once, in the order they come: a library stands for its modules.
  /// </summary>
  public List<string> Expand(IEnumerable<string> entries) =>
  [
    .. entries.SelectMany(e => Config.Libraries.TryGetValue(e, out var modules) ? modules :
                            Source.Has(e) ? [e] :
                            throw new KeyNotFoundException(
                              $"{e}: neither a configured library nor a VTK module"))
      .Distinct()
  ];

  /// <summary>
  /// The headers a module's parse includes, and the ones <c>exclude_headers</c> leaves out (one
  /// platform's classes: VTK installs the platform's only, and the output builds everywhere).
  /// </summary>
  public (IReadOnlyList<string> Headers, IReadOnlyList<string> Excluded) Headers(string module)
  {
    var all = Source.Headers(module);
    return ([.. all.Where(h => !Config.IsExcludedHeader(h))],
      [.. all.Where(Config.IsExcludedHeader)]);
  }

  /// <summary>
  /// <paramref name="parse"/> over the modules in parallel (each parse has its own libclang
  /// index), the results in module order. A module whose parse throws gets its error instead;
  /// progress goes to stderr.
  /// </summary>
  public static (T? Result, string? Error)[] ParseAll<T>(IReadOnlyList<string> modules,
                                                         Func<string, T> parse) where T : class
  {
    var results = new (T? Result, string? Error)[modules.Count];
    var done = 0;
    Parallel.For(0, modules.Count,
                 new ParallelOptions
                   { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, i =>
                 {
                   try
                   {
                     results[i] = (parse(modules[i]), null);
                   }
                   catch (Exception e)
                   {
                     // the parse's own errors say what failed; anything else is a bug there, named
                     // by its type
                     results[i] = (null,
                       e is InvalidOperationException or IOException
                         ? e.Message
                         : $"{modules[i]}: {e.GetType().Name}: {e.Message}");
                   }

                   Console.Error.WriteLine(
                     $"parsed {modules[i]} ({Interlocked.Increment(ref done)}/{modules.Count})");
                 });
    return results;
  }
}
