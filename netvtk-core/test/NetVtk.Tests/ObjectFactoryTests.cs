// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET5_0_OR_GREATER
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// The objects a class's constructor makes through VTK's object factory when the class is the first
/// a process makes. .NET loads each NetVtk library at its first call: a library whose classes make
/// RenderingCore's objects registers the factories that override them when it loads, as
/// NetVtkRendering does, or they are VTK's base classes until NetVtkRendering loads. Each case runs
/// in a process of its own (this assembly's <see cref="FirstObject"/>); .NET Framework loads every
/// library at once (NativeLibraryLoader).
/// </summary>
[TestFixture]
public class ObjectFactoryTests
{
  private const int Timeout = 60000;

  [Test]
  [Platform("Win")]
  [TestCase("vtkWin32OpenGLDXRenderWindow")]
  [TestCase("vtkWin32OpenGLRenderWindow")]
  public void GetRenderTimer_OfAWindowMadeFirst_IsOpenGLs(string window)
  {
    // Act
    var timer = MadeFirst(window);

    // Assert
    Assert.That(timer, Is.EqualTo("vtkOpenGLRenderTimerLog, supported"));
  }

  [Test]
  public void GetHandleProperty_OfARepresentationMadeFirst_IsOpenGLs()
  {
    // Act
    var property = MadeFirst("vtkBoxRepresentation");

    // Assert
    Assert.That(property, Is.EqualTo("vtkOpenGLProperty"));
  }

  // what FirstObject prints in a process of its own: this assembly, run by the runtime's host
  private static string MadeFirst(string className)
  {
    // the runtime's directory: <root>/shared/Microsoft.NETCore.App/<version>
    var root = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..");
    var host =
      Path.GetFullPath(Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
    var start = new ProcessStartInfo(
      host, $"\"{typeof(ObjectFactoryTests).Assembly.Location}\" {FirstObject.Command} {className}")
    {
      UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
      CreateNoWindow = true,
    };
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(Timeout))
    {
      process.Kill();
      Assert.Fail($"{className}: the process didn't end in {Timeout} ms");
    }

    Assert.That(process.ExitCode, Is.Zero, $"{className}: {output.Result}{errors.Result}");
    return output.Result.Trim();
  }
}

/// <summary>
/// The assembly's entry point on .NET, in place of the test SDK's empty one: <c>first
/// &lt;class&gt;</c> makes an object of the class before any other but VTK's output window (in
/// NetVtkCommon, which registers no factories), and prints what its constructor made through the
/// factory: a window's render timer and whether it is supported, a box representation's handle
/// property.
/// </summary>
internal static class FirstObject
{
  public const string Command = "first";

  private static int Main(string[] args)
  {
    if (args.Length != 2 || args[0] != Command)
    {
      return 2;
    }

    VtkOutput.Message += (_, e) => Console.Error.WriteLine($"{e.Kind}: {e.Text}");
    switch (args[1])
    {
      case "vtkWin32OpenGLDXRenderWindow":
      case "vtkWin32OpenGLRenderWindow":
      {
        using vtkWin32OpenGLRenderWindow window = args[1] == "vtkWin32OpenGLDXRenderWindow"
          ? new vtkWin32OpenGLDXRenderWindow()
          : new vtkWin32OpenGLRenderWindow();
        using var timer = window.GetRenderTimer();
        var supported = timer.IsSupported() ? "supported" : "not supported";
        Console.WriteLine($"{timer.GetClassName()}, {supported}");
        return 0;
      }
      case "vtkBoxRepresentation":
      {
        using var representation = new vtkBoxRepresentation();
        using var property = representation.GetHandleProperty();
        Console.WriteLine(property.GetClassName());
        return 0;
      }
      default:
        return 2;
    }
  }
}
#endif
