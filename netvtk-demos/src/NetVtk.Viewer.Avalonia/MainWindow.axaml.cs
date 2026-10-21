// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading.Tasks;

// Avalonia
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace NetVtk.Viewer.Avalonia;

public partial class MainWindow : Window
{
  private readonly ViewerSession _session = new();
  private readonly string[] _commandLine;

  // the controls follow the session: their events then change nothing
  private bool _syncing;

  // the command line opens once, in the first viewer
  private bool _opened;

  // the previewer's
  public MainWindow() : this([])
  {
  }

  public MainWindow(string[] commandLine)
  {
    _commandLine = commandLine;
    VtkViewer.InitializeOnUiThread();
    InitializeComponent();
    _session.Viewer = () => Host.Viewer;
    _session.DocumentChanged += (_, _) => SyncControls();
    Host.ViewerCreated += Host_ViewerCreated;
    Closed += (_, _) => _session.Dispose();
    if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
    {
      Status.Text = "This demo hosts the VTK view as a Win32 or X11 window or an NSView: it runs "
                    + "on Windows, Linux (X11) and macOS.";
    }
  }

  // each native control Avalonia creates has a viewer of its own: the document goes into the new
  // one, the command line's into the first
  private async void Host_ViewerCreated(object? sender, EventArgs e)
  {
    if (Host.Viewer is not { } viewer)
    {
      return;
    }

    _session.Attach(viewer);
    if (!_opened)
    {
      _opened = true;
      await Run(status => _session.OpenAsync(_commandLine, status));
    }
  }

  private async void Open_Click(object? sender, RoutedEventArgs e)
  {
    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
      Title = "Open a result",
      AllowMultiple = false,
      FileTypeFilter =
      [
        new FilePickerFileType("VTK results") { Patterns = ["*.vtu", "*.vtk"] },
        FilePickerFileTypes.All,
      ],
    });
    if (files is [var file] && file.TryGetLocalPath() is { } path)
    {
      await Run(status => _session.OpenAsync(path, status));
    }
  }

  private async void Sample_Click(object? sender, RoutedEventArgs e) =>
    await Run(_session.OpenSampleAsync);

  private void Fields_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
    ApplyControls();

  private void Toggle_Changed(object? sender, RoutedEventArgs e) => ApplyControls();

  private void Slider_Changed(object? sender, RangeBaseValueChangedEventArgs e) => ApplyControls();

  // an open, the buttons off meanwhile; the status line says how it went
  private async Task Run(Func<Action<string>, Task<string>> open)
  {
    OpenButton.IsEnabled = SampleButton.IsEnabled = false;
    try
    {
      Status.Text = await open(text => Status.Text = text);
    }
    finally
    {
      OpenButton.IsEnabled = SampleButton.IsEnabled = true;
    }
  }

  private void ApplyControls()
  {
    if (_syncing
        || _session.Options is not { } options
        || Fields.SelectedItem is not ResultField field)
    {
      return;
    }

    _session.Apply(options with
    {
      Field = field,
      Warp = Warp.IsChecked == true,
      WarpScale = WarpScale.Value * _session.AutoWarpScale,
      Cut = Cut.IsChecked == true,
      CutPosition = CutPosition.Value,
      Contours = Contours.IsChecked == true,
    });
    Contours.IsEnabled = field.IsScalar;
  }

  private void SyncControls()
  {
    if (_session is not { Document: { } document, Options: { } options })
    {
      return;
    }

    _syncing = true;
    try
    {
      Fields.ItemsSource = document.Fields.ToList();
      Fields.SelectedItem = options.Field;
      Warp.IsChecked = options.Warp;
      WarpScale.Value = options.WarpScale / _session.AutoWarpScale;
      Cut.IsChecked = options.Cut;
      CutPosition.Value = options.CutPosition;
      Contours.IsChecked = options.Contours;
      Fields.IsEnabled = Cut.IsEnabled = CutPosition.IsEnabled = true;
      Warp.IsEnabled = WarpScale.IsEnabled = _session.CanWarp;
      Contours.IsEnabled = options.Field.IsScalar;
    }
    finally
    {
      _syncing = false;
    }
  }
}
