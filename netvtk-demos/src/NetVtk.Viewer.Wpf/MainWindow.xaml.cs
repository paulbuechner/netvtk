// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Windows;

namespace NetVtk.Viewer.Wpf;

public partial class MainWindow : Window
{
  private readonly ViewerSession _session = new();

  // the controls follow the session: their events then change nothing
  private bool _syncing;

  public MainWindow()
  {
    VtkViewer.InitializeOnUiThread();
    InitializeComponent();
    _session.Viewer = () => Host.Viewer;
    _session.DocumentChanged += (_, _) => SyncControls();
  }

  private async void Window_Loaded(object sender, RoutedEventArgs e) =>
    await Run(status => _session.OpenAsync(Environment.GetCommandLineArgs()[1..], status));

  // after the host destroyed the view and its scene
  private void Window_Closed(object? sender, EventArgs e) => _session.Dispose();

  private async void Open_Click(object sender, RoutedEventArgs e)
  {
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
      Filter = "VTK results (*.vtu;*.vtk)|*.vtu;*.vtk|All files (*.*)|*.*",
    };
    if (dialog.ShowDialog(this) == true)
    {
      await Run(status => _session.OpenAsync(dialog.FileName, status));
    }
  }

  private async void Sample_Click(object sender, RoutedEventArgs e) =>
    await Run(_session.OpenSampleAsync);

  private void Options_Changed(object sender, RoutedEventArgs e) => ApplyControls();

  private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) =>
    ApplyControls();

  // an open, the buttons off meanwhile; the status line says how it went
  private async System.Threading.Tasks.Task Run(
    Func<Action<string>, System.Threading.Tasks.Task<string>> open)
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
