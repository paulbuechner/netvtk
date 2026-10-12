// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace NetVtk.Viewer;

/// <summary>
/// A window's result: the document it shows and how, which outlive a viewer (Avalonia creates the
/// view again when its control moves). Both demos' windows drive it; call it on the UI thread.
/// </summary>
public sealed class ViewerSession : IDisposable
{
  private readonly FileOpener _opener = new();

  /// <summary>
  /// The viewer to show in, asked when a document has read: null while there's none.
  /// </summary>
  public Func<VtkViewer?> Viewer { get; set; } = () => null;

  /// <summary>The document that shows, or null.</summary>
  public ResultDocument? Document { get; private set; }

  /// <summary>How it shows.</summary>
  public SceneOptions? Options { get; private set; }

  /// <summary>Whether the document has a displacement to warp by.</summary>
  public bool CanWarp { get; private set; }

  /// <summary>The warp scale that moves the largest displacement a tenth of the diagonal.</summary>
  public double AutoWarpScale { get; private set; } = 1;

  /// <summary>Another document shows: its fields and options are new.</summary>
  public event EventHandler? DocumentChanged;

  /// <summary>
  /// Opens what the command line names: <c>--sample</c>, or a file. Without one, the status says
  /// how to open one.
  /// </summary>
  public Task<string> OpenAsync(string[] commandLine, Action<string> status) => commandLine switch
  {
    ["--sample"] => OpenSampleAsync(status),
    [var path] => OpenAsync(path, status),
    _ => Task.FromResult("Open a .vtu (or a legacy .vtk) result, or the sample."),
  };

  public async Task<string> OpenAsync(string path, Action<string> status) =>
    Show(await _opener.OpenAsync(path, status));

  public async Task<string> OpenSampleAsync(Action<string> status) =>
    Show(await _opener.OpenSampleAsync(status));

  /// <summary>A new viewer: the document shows in it, as it did.</summary>
  public void Attach(VtkViewer viewer)
  {
    if (Document is not null)
    {
      viewer.Show(Document, Options);
    }
  }

  /// <summary>Shows the document with <paramref name="options"/>.</summary>
  public void Apply(SceneOptions options)
  {
    Options = options;
    Viewer()?.Apply(options);
  }

  public void Dispose()
  {
    Document?.Dispose();
    Document = null;
  }

  private string Show(OpenResult result)
  {
    if (result.Document is not { } document)
    {
      return result.Status;
    }

    // asked after the read: Avalonia creates the view again when its control moves
    if (Viewer() is not { } viewer)
    {
      document.Dispose();
      return $"{document.Name}: there's no view to show it in.";
    }

    var scene = viewer.Show(document);
    var previous = Document;
    Document = document;
    Options = scene.DefaultOptions();
    CanWarp = scene.CanWarp;
    AutoWarpScale = scene.AutoWarpScale;
    previous?.Dispose();
    DocumentChanged?.Invoke(this, EventArgs.Empty);
    return result.Status;
  }
}
