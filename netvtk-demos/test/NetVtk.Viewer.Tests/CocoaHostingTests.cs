// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// NUnit
using NUnit.Framework;

//
using Vtk;

// static usings
using static NetVtk.Viewer.CocoaMethods;

namespace NetVtk.Viewer.Tests;

/// <summary>
/// The macOS viewer view in a host's NSView, as Avalonia's <c>NativeControlHost</c> gives it: VTK
/// draws into it at its size in pixels. AppKit wants its main thread, which NUnit's workers aren't:
/// the case runs in a process of its own (this assembly's <see cref="CocoaHost"/>). Needs a
/// logged-in session with a GPU (a Mac's, not CI's runners).
/// </summary>
[TestFixture]
[Platform("MacOsX")]
[Category("Rendering")]
[SupportedOSPlatform("macos")]
public class CocoaHostingTests
{
  private const int Timeout = 60000;

  [Test]
  public void ViewerView_DrawsInTheHostsViewAtItsPixelSize()
  {
    // Act
    var shown = Run(CocoaHost.Command);

    // Assert (320 x 200 points; a Retina screen has two pixels for a point)
    var scale = shown.Width / 320.0;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(shown.Superview, Is.EqualTo("host"), "the host's subview");
      Assert.That(scale, Is.EqualTo(1).Or.EqualTo(2), "the screen's pixels per point");
      Assert.That(shown.Height, Is.EqualTo(200 * scale));
      Assert.That(shown.Pixels, Is.EqualTo(shown.Width * shown.Height), "the render window's size");
      Assert.That(shown.Red, Is.GreaterThan(0), "the cone");
      Assert.That(shown.Dpi, Is.EqualTo(72 * scale));
    }
  }

  // what CocoaHost prints in a process of its own: this assembly, run by the runtime's host
  private static Shown Run(string command)
  {
    // the runtime's directory: <root>/shared/Microsoft.NETCore.App/<version>
    var root = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..");
    var start = new ProcessStartInfo(Path.GetFullPath(Path.Combine(root, "dotnet")),
                                     $"\"{typeof(CocoaHostingTests).Assembly.Location}\" {command}")
    {
      UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
    };
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(Timeout))
    {
      process.Kill();
      Assert.Fail($"{command}: the process didn't end in {Timeout} ms");
    }

    Assert.That(process.ExitCode, Is.Zero, $"{command}: {output.Result}{errors.Result}");
    var words = output.Result.Trim().Split(' ');
    return new Shown(words[0], int.Parse(words[1]), int.Parse(words[2]), int.Parse(words[3]),
                     int.Parse(words[4]), int.Parse(words[5]));
  }

  private sealed record Shown(string Superview, int Width, int Height, int Pixels, int Red,
                              int Dpi);
}

/// <summary>
/// The assembly's entry point, in place of the test SDK's empty one: <c>cocoa</c> shows a red cone
/// in a <see cref="CocoaViewerView"/> in a window's content view (320 x 200 points), on the main
/// thread, and prints whose subview the view is, its size in pixels, the render window's pixels,
/// the red ones among them and the render window's DPI.
/// </summary>
internal static class CocoaHost
{
  public const string Command = "cocoa";

  private static int Main(string[] args)
  {
    if (args is not [Command] || !OperatingSystem.IsMacOS())
    {
      return 2;
    }

    VtkOutput.Message += (_, e) => Console.Error.WriteLine($"{e.Kind}: {e.Text}");
    return Show();
  }

  [SupportedOSPlatform("macos")]
  private static int Show()
  {
    // Arrange (a borderless window standing in for the host's, never on the screen: VTK reads its
    // own framebuffer; and a red cone to show)
    NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    var application = Send(Class("NSApplication"), Selector("sharedApplication"));
    // no Dock icon: NSApplicationActivationPolicyAccessory
    Send(application, Selector("setActivationPolicy:"), 1);
    var bounds = new CGRect(0, 0, 320, 200);
    // NSWindowStyleMaskBorderless, NSBackingStoreBuffered
    var window = Send(Send(Class("NSWindow"), Selector("alloc")),
                      Selector("initWithContentRect:styleMask:backing:defer:"), bounds, 0, 2,
                      false);
    var host = Send(window, Selector("contentView"));
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
    var view = CocoaViewerView.Create(viewer);
    try
    {
      // Act (the host adds the view and sizes it, as Avalonia does, then it draws)
      Send(host, Selector("addSubview:"), view.Handle);
      Send(view.Handle, Selector("setFrame:"), bounds);
      viewer.Renderer.ResetCamera();
      viewer.Redraw();

      // Assert (in the test)
      var superview = Send(view.Handle, Selector("superview")) == host ? "host" : "other";
      var (width, height) = view.PixelSize;
      var pixels = Pixels.Of(viewer.RenderWindow);
      var red = pixels.Count(p => p is { R: > 150, G: < 80, B: < 80 });
      Console.WriteLine(
        $"{superview} {width} {height} {pixels.Length} {red} {viewer.RenderWindow.GetDPI()}");
      return 0;
    }
    finally
    {
      // the view before its window, as the host disposes them
      viewer.Dispose();
      view.Dispose();
      Send(window, Selector("close"));
    }
  }
}
