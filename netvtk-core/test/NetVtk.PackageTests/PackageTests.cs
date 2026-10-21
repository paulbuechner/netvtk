// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.PackageTests;

/// <summary>
/// The installed package works: the natives land where NetVtk's loader looks (runtimes\ on .NET,
/// x64\ or x86\ on .NET Framework) and resolve their VTK dependencies there.
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
}
