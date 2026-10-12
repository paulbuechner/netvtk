// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// Pointers VTK returns with a size hint (VTK_SIZEHINT) are C# arrays of that length
/// (VtkObjects.i).
/// </summary>
[TestFixture]
public class SizeHintTests
{
  [Test]
  public void GetBounds_IsSixDoubles()
  {
    // Arrange
    using var cone = new vtkConeSource();
    // a square base: its corners reach the radius on both axes
    cone.SetResolution(4);
    cone.SetHeight(2.0);
    cone.SetRadius(0.5);
    cone.SetCenter(1.0, 2.0, 3.0);
    cone.Update();
    using var output = cone.GetOutput();

    // Act
    var bounds = output.GetBounds();

    // Assert: the cone's axis is x
    Assert.That(bounds, Is.EqualTo(new[] { 0.0, 2.0, 1.5, 2.5, 2.5, 3.5 }).Within(1e-6));
  }

  [Test]
  public void GetColor_IsThreeDoubles()
  {
    // Arrange
    using var property = new vtkProperty();
    property.SetColor(0.25, 0.5, 0.75);

    // Act
    var color = property.GetColor();

    // Assert
    Assert.That(color, Is.EqualTo(new[] { 0.25, 0.5, 0.75 }));
  }

  [Test]
  public void GetSize_IsTwoInts()
  {
    // Arrange
    using var window = new vtkRenderWindow();
    window.SetSize(320, 240);

    // Act
    var size = window.GetSize();

    // Assert
    Assert.That(size, Is.EqualTo(new[] { 320, 240 }));
  }

  [Test]
  public void GetRange_IsTwoDoubles()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    array.InsertNextValue(-1.5);
    array.InsertNextValue(4.0);
    array.InsertNextValue(2.0);

    // Act
    var range = array.GetRange();

    // Assert
    Assert.That(range, Is.EqualTo(new[] { -1.5, 4.0 }));
  }
}
