// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;

namespace NetVtk.Viewer;

/// <summary>An open's outcome: what the status line says, and the document when it read.</summary>
public sealed record OpenResult(string Status, ResultDocument? Document);

/// <summary>
/// Opens results, one at a time: reads them off the UI thread. A file that doesn't read is a
/// status, not an exception. Call it from the UI thread.
/// </summary>
public sealed class FileOpener
{
  /// <summary>How the mouse moves the view.</summary>
  public const string Controls = "Left button: rotate, middle: pan, right or wheel: zoom.";

  // per process: two viewers don't write the same sample file
  private static readonly string SamplePath =
    Path.Combine(Path.GetTempPath(), $"netvtk-sample-{Environment.ProcessId}.vtu");

  /// <summary>An open is running; another one is refused until it ends.</summary>
  public bool IsBusy { get; private set; }

  /// <summary>Reads <paramref name="path"/>.</summary>
  /// <param name="status">Takes the progress for the status line.</param>
  public Task<OpenResult> OpenAsync(string path, Action<string> status) =>
    Exclusive(() => ReadAsync(path, status));

  /// <summary>Writes the sample beam as a .vtu, then reads it like any other file.</summary>
  public Task<OpenResult> OpenSampleAsync(Action<string> status) =>
    Exclusive(async () =>
    {
      status("Writing the sample...");
      try
      {
        await Task.Run(() => SampleResult.Write(SamplePath));
      }
      catch (IOException exception)
      {
        return new OpenResult(exception.Message, null);
      }

      return await ReadAsync(SamplePath, status);
    });

  /// <summary>What the status line says about a document that shows.</summary>
  public static string Describe(ResultDocument document) =>
    $"{document.Name}: {document.Grid.GetNumberOfCells()} cells, {document.Fields.Count} fields. {Controls}";

  private async Task<OpenResult> Exclusive(Func<Task<OpenResult>> open)
  {
    if (IsBusy)
    {
      return new OpenResult("Another file is still opening.", null);
    }

    IsBusy = true;
    try
    {
      return await open();
    }
    finally
    {
      IsBusy = false;
    }
  }

  private static async Task<OpenResult> ReadAsync(string path, Action<string> status)
  {
    var name = Path.GetFileName(path);
    status($"Reading {name}...");
    try
    {
      var document = await Task.Run(() => ResultDocument.Read(path));
      if (document.Fields.Count == 0)
      {
        document.Dispose();
        return new OpenResult($"{name}: the result has no fields to show.", null);
      }

      return new OpenResult(Describe(document), document);
    }
    catch (InvalidDataException exception)
    {
      return new OpenResult($"{name}: {exception.Message}", null);
    }
  }
}
