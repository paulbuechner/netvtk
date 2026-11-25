// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// static usings
using static NetVtk.Viewer.CocoaMethods;

namespace NetVtk.Viewer;

/// <summary>
/// An NSView that shows a <see cref="VtkViewer"/>, the macOS counterpart of
/// <see cref="ViewerWindow"/>: its class, an NSView subclass registered from C#, overrides what
/// drives the viewer (draw, size, backing scale, mouse), and VTK's OpenGL context draws into it.
/// Avalonia's <c>NativeControlHost</c> hosts it; the host adds it to its view.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CocoaViewerView : IDisposable
{
  private const string ClassName = "NetVtkViewerView";
  private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

  private static readonly Dictionary<IntPtr, CocoaViewerView> Views = [];
  private static readonly Dictionary<IntPtr, MouseEvent> MouseEvents = [];
  private static IntPtr _class, _superclass;

  private readonly VtkViewer _viewer;

  private CocoaViewerView(IntPtr handle, VtkViewer viewer)
  {
    Handle = handle;
    _viewer = viewer;
  }

  /// <summary>The view (an NSView), which this object owns.</summary>
  public IntPtr Handle { get; private set; }

  /// <summary>
  /// A view showing <paramref name="viewer"/>, in no window yet: on the main thread, as AppKit
  /// wants.
  /// </summary>
  public static CocoaViewerView Create(VtkViewer viewer)
  {
    var cls = ViewClass();
    var handle = Send(Send(cls, Selector("alloc")), Selector("initWithFrame:"),
                      new CGRect(0, 0, 1, 1));
    // a pixel for each of the screen's pixels: on a Retina screen, two for a point
    Send(handle, Selector("setWantsBestResolutionOpenGLSurface:"), true);
    // moves without a button down, while the view's window is the key window
    var area = Send(Send(Class("NSTrackingArea"), Selector("alloc")),
                    Selector("initWithRect:options:owner:userInfo:"), default(CGRect),
                    TrackMouseMoved | TrackInKeyWindow | TrackInVisibleRect, handle, IntPtr.Zero);
    Send(handle, Selector("addTrackingArea:"), area);
    Send(area, Selector("release"));

    var view = new CocoaViewerView(handle, viewer);
    Views[handle] = view;
    viewer.Attach(handle);
    return view;
  }

  /// <summary>The view's size in pixels: its bounds in the backing store's units.</summary>
  public (int Width, int Height) PixelSize
  {
    get
    {
      var bounds = SendRect(Handle, Selector("bounds"));
      var size = SendSize(Handle, Selector("convertSizeToBacking:"), bounds.Size);
      return ((int)Math.Round(size.Width), (int)Math.Round(size.Height));
    }
  }

  // VTK lets go of the view with the render window (the viewer, disposed first)
  public void Dispose()
  {
    if (Handle != IntPtr.Zero)
    {
      Views.Remove(Handle);
      Send(Handle, Selector("removeFromSuperview"));
      Send(Handle, Selector("release"));
      Handle = IntPtr.Zero;
    }
  }

  // the class, once: NSView's methods it overrides take their type encodings from NSView's
  private static unsafe IntPtr ViewClass()
  {
    if (_class != IntPtr.Zero)
    {
      return _class;
    }

    NativeLibrary.Load(AppKit);
    var superclass = Class("NSView");
    var cls = objc_allocateClassPair(superclass, ClassName, 0);
    if (cls == IntPtr.Zero)
    {
      throw new InvalidOperationException($"the Objective-C class {ClassName} exists already");
    }

    Override(cls, superclass, "drawRect:",
             (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGRect, void>)&DrawRect);
    Override(cls, superclass, "setFrameSize:",
             (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGSize, void>)&SetFrameSize);
    Override(cls, superclass, "viewDidMoveToWindow",
             (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&BackingChanged);
    Override(cls, superclass, "viewDidChangeBackingProperties",
             (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&BackingChanged);
    // OpenGL paints every pixel; the first click into an inactive window reaches the view too
    Override(cls, superclass, "isOpaque", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Yes);
    Override(cls, superclass, "acceptsFirstMouse:",
             (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&YesTo);
    foreach (var (name, kind) in new (string, MouseEvent)[]
             {
               ("mouseMoved:", MouseEvent.Move), ("mouseDragged:", MouseEvent.Move),
               ("rightMouseDragged:", MouseEvent.Move), ("otherMouseDragged:", MouseEvent.Move),
               ("mouseDown:", MouseEvent.Down), ("rightMouseDown:", MouseEvent.Down),
               ("otherMouseDown:", MouseEvent.Down), ("mouseUp:", MouseEvent.Up),
               ("rightMouseUp:", MouseEvent.Up), ("otherMouseUp:", MouseEvent.Up),
               ("scrollWheel:", MouseEvent.Wheel),
             })
    {
      MouseEvents[Selector(name)] = kind;
      Override(cls, superclass, name,
               (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Mouse);
    }

    objc_registerClassPair(cls);
    _superclass = superclass;
    _class = cls;
    return cls;
  }

  private static void Override(IntPtr cls, IntPtr superclass, string name, IntPtr implementation)
  {
    var selector = Selector(name);
    var types = method_getTypeEncoding(class_getInstanceMethod(superclass, selector));
    if (!class_addMethod(cls, selector, implementation, types))
    {
      throw new InvalidOperationException($"{ClassName} doesn't take {name}");
    }
  }

  // AppKit calls the overrides on the main thread: no .NET exception may leave them
  private static void Run(IntPtr self, IntPtr selector, Action<CocoaViewerView> action)
  {
    if (!Views.TryGetValue(self, out var view))
    {
      return;
    }

    try
    {
      action(view);
    }
    catch (Exception exception)
    {
      // a .NET exception can't unwind through AppKit, which called us: report it and carry on
      Trace.TraceError(
        $"NetVtk viewer, {Marshal.PtrToStringUTF8(sel_getName(selector))}: {exception}");
    }
  }

  [UnmanagedCallersOnly]
  private static void DrawRect(IntPtr self, IntPtr selector, CGRect dirty) =>
    Run(self, selector, view => view._viewer.Redraw());

  // NSView's first: the frame changes, then the viewer follows
  [UnmanagedCallersOnly]
  private static void SetFrameSize(IntPtr self, IntPtr selector, CGSize size)
  {
    SendSuper(new ObjCSuper { Receiver = self, SuperClass = _superclass }, selector, size);
    Run(self, selector, view => view.Resized());
  }

  [UnmanagedCallersOnly]
  private static void BackingChanged(IntPtr self, IntPtr selector)
  {
    SendSuper(new ObjCSuper { Receiver = self, SuperClass = _superclass }, selector);
    Run(self, selector, view => view.Resized());
  }

  [UnmanagedCallersOnly]
  private static byte Yes(IntPtr self, IntPtr selector) => 1;

  [UnmanagedCallersOnly]
  private static byte YesTo(IntPtr self, IntPtr selector, IntPtr argument) => 1;

  [UnmanagedCallersOnly]
  private static void Mouse(IntPtr self, IntPtr selector, IntPtr theEvent) =>
    Run(self, selector, view => view.OnMouse(MouseEvents[selector], theEvent));

  // the size in pixels, and VTK's DPI for the screen's pixels per point (72 a point), which keeps
  // text at its size on a Retina screen
  private void Resized()
  {
    var window = Send(Handle, Selector("window"));
    if (window != IntPtr.Zero)
    {
      _viewer.SetPixelsPerPoint(SendDouble(window, Selector("backingScaleFactor")));
    }

    var (width, height) = PixelSize;
    _viewer.Resize(width, height);
    Send(Handle, Selector("setNeedsDisplay:"), true);
  }

  private void OnMouse(MouseEvent kind, IntPtr theEvent)
  {
    var (x, y) = Position(theEvent);
    var modifiers = Modifiers(SendUInt(theEvent, Selector("modifierFlags")));
    switch (kind)
    {
      case MouseEvent.Move:
        _viewer.MouseMove(x, y, modifiers);
        break;
      // AppKit sends the moves and the release to the view the button went down in, wherever the
      // pointer is
      case MouseEvent.Down when Button(theEvent) is { } pressed:
        _viewer.ButtonDown(pressed, x, y, modifiers);
        break;
      case MouseEvent.Up when Button(theEvent) is { } released:
        _viewer.ButtonUp(released, x, y, modifiers);
        break;
      case MouseEvent.Wheel:
        var delta = SendDouble(theEvent, Selector("deltaY"));
        if (delta != 0)
        {
          _viewer.MouseWheel(x, y, delta, modifiers);
        }

        break;
    }
  }

  // the event's location in the view's pixels, y down: AppKit's y goes up from the bottom
  private (int X, int Y) Position(IntPtr theEvent)
  {
    var inWindow = SendPoint(theEvent, Selector("locationInWindow"));
    var inView = SendPoint(Handle, Selector("convertPoint:fromView:"), inWindow, IntPtr.Zero);
    var pixel = SendPoint(Handle, Selector("convertPointToBacking:"), inView);
    var x = (int)Math.Floor(pixel.X);
    var y = (int)Math.Floor(pixel.Y);
    return (x, PixelSize.Height - 1 - y);
  }

  private static ViewerButton? Button(IntPtr theEvent) =>
    (long)SendUInt(theEvent, Selector("buttonNumber")) switch
    {
      0 => ViewerButton.Left,
      1 => ViewerButton.Right,
      2 => ViewerButton.Middle,
      _ => null,
    };

  // Command counts as Control, as in VTK's own Cocoa view
  private static ViewerModifiers Modifiers(nuint flags) =>
    ((flags & ShiftKey) != 0 ? ViewerModifiers.Shift : 0)
    | ((flags & (ControlKey | CommandKey)) != 0 ? ViewerModifiers.Control : 0);

  private enum MouseEvent
  {
    Move,
    Down,
    Up,
    Wheel,
  }
}
