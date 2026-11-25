// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// Avalonia
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NetVtk.Viewer.Avalonia;

/// <summary>
/// The viewer's native window in Avalonia's layout: a Win32 child window on Windows, an X11 child
/// window on Linux, whose events a timer reads, an NSView on macOS. Avalonia creates the native
/// control once the window shows, and may create it again after the control moves: the viewer
/// lives with each.
/// </summary>
public sealed class ViewerHost : NativeControlHost
{
  private ViewerWindow? _window;
  private X11ViewerWindow? _x11Window;
  private DispatcherTimer? _x11Events;
  private CocoaViewerView? _cocoaView;

  /// <summary>The viewer, once the native control exists.</summary>
  public VtkViewer? Viewer { get; private set; }

  /// <summary>A viewer exists: raised after the layout pass that created it.</summary>
  public event EventHandler? ViewerCreated;

  protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
  {
    if (OperatingSystem.IsWindows())
    {
      Viewer = new VtkViewer();
      _window = ViewerWindow.Create(parent.Handle, Viewer);
      Dispatcher.UIThread.Post(() => ViewerCreated?.Invoke(this, EventArgs.Empty));
      return new PlatformHandle(_window.Handle, "HWND");
    }

    if (OperatingSystem.IsLinux() && parent.HandleDescriptor == "XID")
    {
      Viewer = new VtkViewer();
      var window = X11ViewerWindow.Create(parent.Handle, Viewer);
      _x11Window = window;
      // the window's events, read on the UI thread: about 60 times a second
      _x11Events = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input,
                                       ReadX11Events);
      _x11Events.Start();
      Dispatcher.UIThread.Post(() => ViewerCreated?.Invoke(this, EventArgs.Empty));
      return new PlatformHandle(window.Handle, "XID");
    }

    // Avalonia adds the view to its own, in the window
    if (OperatingSystem.IsMacOS() && parent.HandleDescriptor == "NSView")
    {
      Viewer = new VtkViewer();
      _cocoaView = CocoaViewerView.Create(Viewer);
      Dispatcher.UIThread.Post(() => ViewerCreated?.Invoke(this, EventArgs.Empty));
      return new PlatformHandle(_cocoaView.Handle, "NSView");
    }

    return base.CreateNativeControlCore(parent);
  }

  private void ReadX11Events(object? sender, EventArgs e)
  {
    if (OperatingSystem.IsLinux())
    {
      _x11Window?.ProcessEvents();
    }
  }

  // the view goes before its window
  protected override void DestroyNativeControlCore(IPlatformHandle control)
  {
    if (_window is null && _x11Window is null && _cocoaView is null)
    {
      base.DestroyNativeControlCore(control);
      return;
    }

    _x11Events?.Stop();
    _x11Events = null;
    Viewer?.Dispose();
    Viewer = null;
    if (OperatingSystem.IsWindows())
    {
      _window?.Dispose();
    }

    if (OperatingSystem.IsLinux())
    {
      _x11Window?.Dispose();
    }

    if (OperatingSystem.IsMacOS())
    {
      _cocoaView?.Dispose();
    }

    _window = null;
    _x11Window = null;
    _cocoaView = null;
  }
}
