// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// A value class's public fields are properties: a C array a C# array of its length, copied both
/// ways (Types.i); a C string read only (Strings.i).
/// </summary>
[TestFixture]
public class ValueClassFieldTests
{
  [Test]
  public void CArray_ReadsWhatTheObjectHolds()
  {
    // Arrange (the constructor copies the extent)
    using var neighbor = new vtkStructuredNeighbor(3, new[] { 0, 4, 1, 5, 2, 6 });

    // Act
    var extent = neighbor.OverlapExtent;

    // Assert
    Assert.That(extent, Is.EqualTo(new[] { 0, 4, 1, 5, 2, 6 }));
  }

  [Test]
  public void CArray_IsWritten()
  {
    // Arrange
    using var neighbor = new vtkStructuredNeighbor();

    // Act
    neighbor.Orientation = new[] { 1, -1, 2 };

    // Assert
    Assert.That(neighbor.Orientation, Is.EqualTo(new[] { 1, -1, 2 }));
  }

  [Test]
  public void CArray_OfAnotherLength_IsRejected()
  {
    // Arrange
    using var neighbor = new vtkStructuredNeighbor();

    // Act
    Action write = () => neighbor.Orientation = new[] { 1, 2 };

    // Assert
    Assert.That(write, Throws.ArgumentException);
  }

  [Test]
  public void CString_ReadsTheText()
  {
    // Arrange
    using var component = new vtkMergeFields_Component();

    // Act
    component.SetName(NonAscii.WideText);

    // Assert
    Assert.That(component.FieldName, Is.EqualTo(NonAscii.WideText));
  }
}
