// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Runtime.Versioning;

// static usings
using static NetVtk.Viewer.X11Methods;

namespace NetVtk.Viewer;

/// <summary>
/// An X11 child window that shows a <see cref="VtkViewer"/>, the Linux counterpart of
/// <see cref="ViewerWindow"/>: VTK creates it in the host's window (with the visual OpenGL needs),
/// on a display connection of its own, whose events C# reads (<see cref="ProcessEvents"/>) and
/// passes on to the viewer (size, expose, mouse). Avalonia's <c>NativeControlHost</c> hosts it.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class X11ViewerWindow : IDisposable
{
  private const long Events = StructureNotifyMask
                              | ExposureMask
                              | ButtonPressMask
                              | ButtonReleaseMask
                              | PointerMotionMask;

  private readonly VtkViewer _viewer;
  private IntPtr _display;

  private X11ViewerWindow(IntPtr display, IntPtr handle, VtkViewer viewer)
  {
    _display = display;
    Handle = handle;
    _viewer = viewer;
  }

  /// <summary>The window's id (an X11 Window).</summary>
  public IntPtr Handle { get; }

  /// <summary>
  /// A child window of <paramref name="parent"/> (an X11 window id) showing
  /// <paramref name="viewer"/>.
  /// </summary>
  public static X11ViewerWindow Create(IntPtr parent, VtkViewer viewer)
  {
    var display = XOpenDisplay(IntPtr.Zero);
    if (display == IntPtr.Zero)
    {
      throw new InvalidOperationException("no X display: DISPLAY names none that opens");
    }

    var handle = viewer.AttachChild(display, parent);
    XSelectInput(display, (nuint)(nint)handle, (nint)Events);
    XFlush(display);
    return new X11ViewerWindow(display, handle, viewer);
  }

  /// <summary>
  /// Passes what X sent the window since the last call on to the viewer: on the UI thread, from a
  /// timer. An exception is reported, not thrown: the next events still come.
  /// </summary>
  public void ProcessEvents()
  {
    while (_display != IntPtr.Zero && XPending(_display) > 0)
    {
      XNextEvent(_display, out var xevent);
      var received = X11Event.Read(xevent);
      try
      {
        Dispatch(received);
      }
      catch (Exception exception)
      {
        Trace.TraceError($"NetVtk viewer, X event {received.Type}: {exception}");
      }
    }
  }

  // VTK destroys its window with the render window (the viewer, disposed first); the connection
  // stays open until then
  public void Dispose()
  {
    if (_display != IntPtr.Zero)
    {
      XCloseDisplay(_display);
      _display = IntPtr.Zero;
    }
  }

  /// <summary>An event's effect on the viewer.</summary>
  internal void Dispatch(X11Event received)
  {
    switch (received.Type)
    {
      case ConfigureNotify:
        _viewer.Resize(received.Width, received.Height);
        break;
      case Expose:
        _viewer.Redraw();
        break;
      case MotionNotify:
        _viewer.MouseMove(received.X, received.Y, Modifiers(received.State));
        break;
      // X's buttons 4 and 5 are the wheel; a press grabs the pointer until the last release
      case ButtonPress when received.Button is 4 or 5:
        _viewer.MouseWheel(received.X, received.Y, received.Button == 4 ? 1 : -1,
                           Modifiers(received.State));
        break;
      case ButtonPress when Button(received.Button) is { } pressed:
        _viewer.ButtonDown(pressed, received.X, received.Y, Modifiers(received.State));
        break;
      case ButtonRelease when Button(received.Button) is { } released:
        _viewer.ButtonUp(released, received.X, received.Y, Modifiers(received.State));
        break;
    }
  }

  private static ViewerButton? Button(uint button) => button switch
  {
    1 => ViewerButton.Left,
    2 => ViewerButton.Middle,
    3 => ViewerButton.Right,
    _ => null,
  };

  private static ViewerModifiers Modifiers(uint state) =>
    ((state & ShiftMask) != 0 ? ViewerModifiers.Shift : 0)
    | ((state & ControlMask) != 0 ? ViewerModifiers.Control : 0);
}
