// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.PackageTests;

/// <summary>
/// The installed package works: the natives land where NetVtk's loader looks (runtimes\ on .NET,
/// x64\ or x86\ on .NET Framework) and resolve their VTK dependencies there. build.py
/// test-package runs them for one architecture (a RuntimeIdentifier), then again with
/// NetVtkModules.
/// </summary>
[TestFixture]
public class PackageTests
{
  [Test]
  public void Cone_HasItsBounds()
  {
    // Arrange
    using var cone = new vtkConeSource();
    cone.SetResolution(4);
    cone.SetHeight(2.0);
    cone.SetRadius(0.5);
    cone.Update();
    using var output = cone.GetOutput();

    // Act
    var bounds = output.GetBounds();

    // Assert
    Assert.That(bounds, Is.EqualTo(new[] { -1.0, 1.0, -0.5, 0.5, -0.5, 0.5 }).Within(1e-6));
  }

  [Test]
  public void RenderWindow_IsTheObjectFactorysOpenGLWindow()
  {
    // Act
    using var window = new vtkRenderWindow();

    // Assert: RenderingOpenGL2's factory is registered when its wrapper library loads
    Assert.That(window.GetClassName(), Is.Not.EqualTo("vtkRenderWindow"));
  }

  [Test]
  [Platform("Win")]
  public void DXRenderWindow_ComesWithTheWindowsNatives()
  {
    // Act (NetVtkWin32: in the Windows runtime packages only)
    using var window = new vtkWin32OpenGLDXRenderWindow();

    // Assert
    Assert.That(window.IsA("vtkOpenGLRenderWindow"), Is.EqualTo(1));
  }

#if NETFRAMEWORK
  [Test]
  public void Natives_LieInTheProcessArchitecturesFolderOnly()
  {
    // Arrange
    var directory = AppDomain.CurrentDomain.BaseDirectory;

    // Act
    var copies = Directory.GetFiles(directory, "NetVtkRuntime.dll", SearchOption.AllDirectories);

    // Assert
    Assert.That(copies, Is.EqualTo(new[] { Path.Combine(NativeDirectory(), "NetVtkRuntime.dll") }));
  }
#endif

#if NETVTK_MODULES
  [Test]
  public void Natives_HoldTheSelectedModulesLibrariesOnly()
  {
    // Arrange (Win32's library is in the Windows packages only)
    var modules = typeof(PackageTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .Single(attribute => attribute.Key == "NetVtkModules").Value!.Split(';')
      .Where(module => module != "Win32" || RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
    // a module's library on any system, not the managed NetVtk.dll
    var library = new Regex(@"^(?:lib)?NetVtk([A-Za-z0-9]+)\.(?:dll|so|dylib)$");

    // Act
    var libraries = Directory.GetFiles(NativeDirectory())
      .Select(file => library.Match(Path.GetFileName(file)))
      .Where(match => match.Success)
      .Select(match => match.Groups[1].Value);

    // Assert
    Assert.That(libraries, Is.EquivalentTo(modules.Append("Runtime")));
  }

  [Test]
  public void Natives_LeaveOutWhatOnlyModulesLeftOutLoad()
  {
    // Arrange: IO's readers and Interaction's widgets, which build.py's selection leaves out and
    // none of its modules loads
    var leftOut = new Regex(@"^(?:lib)?vtk(?:IOExodus|IOCGNSReader|InteractionWidgets)-");

    // Act
    var files = Directory.GetFiles(NativeDirectory()).Select(Path.GetFileName).ToList();

    // Assert
    Assert.That(files, Has.None.Matches(leftOut));
  }
#endif

  // where the natives are: x64\ or x86\ on .NET Framework, next to the app on .NET (a RID)
  private static string NativeDirectory()
  {
#if NETFRAMEWORK
    var architecture = IntPtr.Size == 8 ? "x64" : "x86";
    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, architecture);
#else
    return AppContext.BaseDirectory;
#endif
  }
}
