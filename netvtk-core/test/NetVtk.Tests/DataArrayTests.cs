// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// VTK's typed arrays: their values come from the template instance they derive from, and copy in
/// bulk (extras/CommonCore.i).
/// </summary>
[TestFixture]
public class DataArrayTests
{
  [Test]
  public void CellArray_TakesIdArrays()
  {
    // Arrange (vtkIdTypeArray's typed members are vtkAOSDataArrayTemplate<long long>'s: vtkIdType
    // is long long on every platform)
    using var offsets = new vtkIdTypeArray();
    offsets.InsertNextValue(0);
    offsets.InsertNextValue(3);
    using var connectivity = new vtkIdTypeArray();
    foreach (var id in new long[] { 0, 1, 2 })
    {
      connectivity.InsertNextValue(id);
    }

    using var cells = new vtkCellArray();

    // Act
    cells.SetData(offsets, connectivity);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cells.GetNumberOfCells(), Is.EqualTo(1));
      Assert.That(cells.GetCellSize(0), Is.EqualTo(3));
      Assert.That(connectivity.GetValue(2), Is.EqualTo(2));
    }
  }

  [Test]
  public void FromArray_ToArray_RoundTrip()
  {
    // Arrange
    double[] values = [1, 2, 3, 4, 5, 6];

    // Act
    using var array = vtkDoubleArray.FromArray(values, 3);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(array.GetNumberOfTuples(), Is.EqualTo(2));
      Assert.That(array.GetComponent(1, 2), Is.EqualTo(6));
      Assert.That(array.ToArray(), Is.EqualTo(values));
    }
  }

  [Test]
  public void CopyFrom_ReplacesTheValues()
  {
    // Arrange
    using var array = vtkIdTypeArray.FromArray([7, 8, 9, 10]);

    // Act
    array.CopyFrom([1, 2]);

    // Assert
    Assert.That(array.ToArray(), Is.EqualTo(new long[] { 1, 2 }));
  }

  [Test]
  public void CopyFrom_PartialTuple_Throws()
  {
    // Arrange
    using var array = new vtkFloatArray();
    array.SetNumberOfComponents(3);

    // Act
    Action copy = () => array.CopyFrom([1f, 2f]);

    // Assert
    Assert.That(copy, Throws.ArgumentException);
  }

  [Test]
  public void ToDoubleArray_ConvertsAnyArray()
  {
    // Arrange
    using var array = vtkUnsignedCharArray.FromArray([0, 128, 255]);

    // Act
    var values = array.ToDoubleArray();

    // Assert
    Assert.That(values, Is.EqualTo(new double[] { 0, 128, 255 }));
  }

  [Test]
  public void ToDoubleArray_InterleavesSeparateComponents()
  {
    // Arrange (an array of structures' layout's counterpart: one block per component)
    using var array = new vtkSOADataArrayTemplate_double();
    array.SetNumberOfComponents(2);
    array.SetNumberOfTuples(2);
    array.SetComponent(0, 0, 1);
    array.SetComponent(0, 1, 2);
    array.SetComponent(1, 0, 3);
    array.SetComponent(1, 1, 4);

    // Act
    var values = array.ToDoubleArray();

    // Assert
    Assert.That(values, Is.EqualTo(new double[] { 1, 2, 3, 4 }));
  }

  [Test]
  public void ToDoubleArray_ConvertsBits()
  {
    // Arrange (no typed loop for bits: value by value)
    using var array = new vtkBitArray();
    array.InsertNextValue(1);
    array.InsertNextValue(0);
    array.InsertNextValue(1);

    // Act
    var values = array.ToDoubleArray();

    // Assert
    Assert.That(values, Is.EqualTo(new double[] { 1, 0, 1 }));
  }

#if NET5_0_OR_GREATER
  [Test]
  public void AsSpan_IsTheArraysMemory()
  {
    // Arrange
    using var array = vtkIntArray.FromArray([1, 2, 3]);

    // Act
    array.AsSpan()[1] = 20;

    // Assert
    Assert.That(array.GetValue(1), Is.EqualTo(20));
  }
#endif
}
