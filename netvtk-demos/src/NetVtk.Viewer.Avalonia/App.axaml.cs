// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Avalonia
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NetVtk.Viewer.Avalonia;

public partial class App : Application
{
  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      desktop.MainWindow = new MainWindow(desktop.Args ?? []);
    }

    base.OnFrameworkInitializationCompleted();
  }
}
