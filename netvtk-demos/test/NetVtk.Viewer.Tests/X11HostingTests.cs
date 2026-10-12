// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.Versioning;

// NUnit
using NUnit.Framework;

//
using Vtk;

// static usings
using static NetVtk.Viewer.X11Methods;

namespace NetVtk.Viewer.Tests;

/// <summary>
/// The Linux viewer window in a host's X11 window, as Avalonia's <c>NativeControlHost</c> gives
/// it: VTK draws into a child of the host's window. Needs an X server with OpenGL 3.2 (Xvfb and
/// Mesa's llvmpipe in Docker and CI).
/// </summary>
[TestFixture]
[Platform("Linux")]
[Category("Rendering")]
[SupportedOSPlatform("linux")]
public class X11HostingTests
{
  [OneTimeSetUp]
  public void RouteVtkOutput() => VtkOutput.Install();

  [Test]
  public void ViewerWindow_DrawsInAChildOfTheHostsWindow()
  {
    // Arrange (a top-level window standing in for the host's, and a red cone to show)
    var display = XOpenDisplay(IntPtr.Zero);
    if (display == IntPtr.Zero)
    {
      Assert.Ignore("no X display (DISPLAY)");
    }

    var host = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 320, 200, 0, 0, 0);
    XMapWindow(display, host);
    XFlush(display);
    using var cone = new vtkConeSource();
    using var mapper = new vtkPolyDataMapper();
    using (var port = cone.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using (var look = actor.GetProperty())
    {
      look.SetColor(1, 0, 0);
    }

    var viewer = new VtkViewer();
    viewer.Renderer.AddActor(actor);
    var window = X11ViewerWindow.Create((IntPtr)(nint)host, viewer);
    try
    {
      // Act (the host's size, then what X sent)
      viewer.Resize(320, 200);
      viewer.Renderer.ResetCamera();
      window.ProcessEvents();
      viewer.Redraw();

      // Assert
      XQueryTree(display, (nuint)(nint)window.Handle, out _, out var parent, out var children,
                 out _);
      XFree(children);
      var pixels = Pixels.Of(viewer.RenderWindow);
      using (Assert.EnterMultipleScope())
      {
        Assert.That(parent, Is.EqualTo(host), "the host's child");
        Assert.That(pixels, Has.Length.EqualTo(320 * 200));
        Assert.That(
          pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { R: > 150, G: < 80, B: < 80 }),
          "the cone");
      }
    }
    finally
    {
      // the view before its window, as the host disposes them
      viewer.Dispose();
      window.Dispose();
      XDestroyWindow(display, host);
      XCloseDisplay(display);
    }
  }
}
