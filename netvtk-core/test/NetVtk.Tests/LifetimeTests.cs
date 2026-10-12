// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// The proxy is the object and owns one reference (VtkObjects.i): New() and constructors hand
/// theirs over, other returns add one, Dispose drops it.
/// </summary>
[TestFixture]
public class LifetimeTests
{
  [Test]
  public void New_HandsItsReferenceToTheProxy()
  {
    // Act
    using var array = vtkDoubleArray.New();

    // Assert
    Assert.That(array.GetReferenceCount(), Is.EqualTo(1));
  }

  [Test]
  public void Constructor_CallsNew()
  {
    // Act
    using var array = new vtkDoubleArray();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(array.GetReferenceCount(), Is.EqualTo(1));
      Assert.That(array.GetClassName(), Is.EqualTo("vtkDoubleArray"));
    }
  }

  [Test]
  public void Constructor_GetsTheClassTheObjectFactoryChose()
  {
    // Act
    using var window = new vtkRenderWindow();

    // Assert: the platform's (vtkWin32OpenGLRenderWindow, vtkXOpenGLRenderWindow, ...)
    Assert.That(window.GetClassName(), Is.Not.EqualTo("vtkRenderWindow"));
  }

  [Test]
  public void ReturnedObject_HoldsAReferenceOfItsOwn()
  {
    // Arrange
    using var cone = new vtkConeSource();

    // Act
    using var output = cone.GetOutput();

    // Assert: the algorithm's and the proxy's
    Assert.That(output.GetReferenceCount(), Is.EqualTo(2));
  }

  [Test]
  public void Dispose_DropsTheProxysReference()
  {
    // Arrange
    using var cone = new vtkConeSource();
    using var first = cone.GetOutput();
    var second = cone.GetOutput();

    // Act
    second.Dispose();

    // Assert
    Assert.That(first.GetReferenceCount(), Is.EqualTo(2));
  }

  [Test]
  public void Finalizer_DropsTheProxysReference()
  {
    // Arrange
    using var cone = new vtkConeSource();
    using var kept = cone.GetOutput();
    Take(cone);

    // Act
    Garbage.Collect();

    // Assert
    Assert.That(kept.GetReferenceCount(), Is.EqualTo(2));
  }

  [Test]
  public void ObjectSetOnAnother_OutlivesItsProxy()
  {
    // Arrange
    using var actor = new vtkActor();
    var mapper = new vtkPolyDataMapper();
    actor.SetMapper(mapper);

    // Act
    mapper.Dispose();

    // Assert
    using var held = actor.GetMapper();
    Assert.That(held.GetClassName(), Does.EndWith("PolyDataMapper"));
  }

  [Test]
  public void SafeDownCast_ReturnsTheDerivedProxyOrNull()
  {
    // Arrange
    using var cone = new vtkConeSource();
    cone.Update();
    using var data = cone.GetOutputDataObject(0);

    // Act
    using var polyData = vtkPolyData.SafeDownCast(data);
    using var array = vtkDoubleArray.SafeDownCast(data);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(polyData.GetNumberOfPolys(), Is.GreaterThan(0));
      Assert.That(array, Is.Null);
    }
  }

  // a returned proxy C# drops at once
  private static void Take(vtkConeSource cone) => cone.GetOutput();
}
