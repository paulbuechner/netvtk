// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace NetVtk.Viewer;

// the Xlib calls X11ViewerWindow needs (libX11.so.6, source-generated): an X window id is a C
// unsigned long, an event mask a C long, 64 bits on linux-x64
internal static partial class X11Methods
{
  private const string Xlib = "libX11.so.6";

  public const long ExposureMask = 1L << 15,
    StructureNotifyMask = 1L << 17,
    ButtonPressMask = 1L << 2,
    ButtonReleaseMask = 1L << 3,
    PointerMotionMask = 1L << 6;
  public const int ButtonPress = 4,
    ButtonRelease = 5,
    MotionNotify = 6,
    Expose = 12,
    DestroyNotify = 17,
    ConfigureNotify = 22;
  public const uint ShiftMask = 1, ControlMask = 4;

  /// <summary>
  /// XEvent: a union of 24 longs, whose members C# reads at their offsets on linux-x64
  /// (<see cref="X11Event"/>).
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public unsafe struct XEvent
  {
    public fixed long Pad[24];
  }

  [LibraryImport(Xlib)]
  public static partial IntPtr XOpenDisplay(IntPtr name);

  [LibraryImport(Xlib)]
  public static partial int XCloseDisplay(IntPtr display);

  [LibraryImport(Xlib)]
  public static partial int XSelectInput(IntPtr display, nuint window, nint mask);

  [LibraryImport(Xlib)]
  public static partial int XPending(IntPtr display);

  [LibraryImport(Xlib)]
  public static partial int XNextEvent(IntPtr display, out XEvent xevent);

  [LibraryImport(Xlib)]
  public static partial int XDestroyWindow(IntPtr display, nuint window);

  [LibraryImport(Xlib)]
  public static partial int XFlush(IntPtr display);

  [LibraryImport(Xlib)]
  public static partial nuint XDefaultRootWindow(IntPtr display);

  [LibraryImport(Xlib)]
  public static partial nuint XCreateSimpleWindow(IntPtr display, nuint parent, int x, int y,
                                                  uint width, uint height, uint borderWidth,
                                                  nuint border, nuint background);

  [LibraryImport(Xlib)]
  public static partial int XMapWindow(IntPtr display, nuint window);

  [LibraryImport(Xlib)]
  public static partial int XResizeWindow(IntPtr display, nuint window, uint width, uint height);

  [LibraryImport(Xlib)]
  public static partial int XQueryTree(IntPtr display, nuint window, out nuint root,
                                       out nuint parent, out IntPtr children, out uint childCount);

  [LibraryImport(Xlib)]
  public static partial int XFree(IntPtr data);
}

/// <summary>
/// What an X event says, read from its <see cref="X11Methods.XEvent"/> at linux-x64's offsets:
/// type at 0; XButtonEvent's and XMotionEvent's x, y at 64, 68, state at 80, button at 84;
/// XConfigureEvent's width, height at 56, 60.
/// </summary>
internal readonly record struct X11Event(int Type, int X, int Y, uint State, uint Button, int Width,
                                         int Height)
{
  public static unsafe X11Event Read(in X11Methods.XEvent xevent)
  {
    fixed (long* pad = xevent.Pad)
    {
      var bytes = (byte*)pad;
      var type = *(int*)bytes;
      return type == X11Methods.ConfigureNotify
        ? new X11Event(type, 0, 0, 0, 0, *(int*)(bytes + 56), *(int*)(bytes + 60))
        : new X11Event(type, *(int*)(bytes + 64), *(int*)(bytes + 68), *(uint*)(bytes + 80),
                       *(uint*)(bytes + 84), 0, 0);
    }
  }
}
