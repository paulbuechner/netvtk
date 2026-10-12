// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Data and arrays: an unstructured grid built from C# arrays, its fields, and the typed arrays'
/// bulk copies.
/// </summary>
[TestFixture]
public class Data
{
  [Test]
  public void Grid_FromArrays()
  {
    // Act
    #region grid
    // two unit cubes side by side along x: 12 points, x fastest, then y, then z
    double[] coordinates =
    [
      0, 0, 0, 1, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 0, 2, 1, 0,
      0, 0, 1, 1, 0, 1, 2, 0, 1, 0, 1, 1, 1, 1, 1, 2, 1, 1,
    ];

    // a hexahedron's corners: the bottom face counterclockwise, then the top; offsets mark where
    // each cell's corners start in the connectivity, and where the last one ends
    long[] connectivity = [0, 1, 4, 3, 6, 7, 10, 9, 1, 2, 5, 4, 7, 8, 11, 10];
    long[] offsets = [0, 8, 16];

    using var grid = new vtkUnstructuredGrid();
    using (var points = new vtkPoints())
    using (var pointArray = vtkDoubleArray.FromArray(coordinates, 3))
    {
      points.SetData(pointArray);
      grid.SetPoints(points);
    }

    using (var offsetArray = vtkIdTypeArray.FromArray(offsets))
    using (var connectivityArray = vtkIdTypeArray.FromArray(connectivity))
    using (var cells = new vtkCellArray())
    {
      cells.SetData(offsetArray, connectivityArray);
      grid.SetCells((int)VTKCellType.VTK_HEXAHEDRON, cells);
    }
    #endregion

    // Assert
    using var first = grid.GetCell(0);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(grid.GetNumberOfPoints(), Is.EqualTo(12));
      Assert.That(grid.GetNumberOfCells(), Is.EqualTo(2));
      Assert.That(grid.GetCellType(1), Is.EqualTo((int)VTKCellType.VTK_HEXAHEDRON));
      Assert.That(first.GetBounds(), Is.EqualTo(new double[] { 0, 1, 0, 1, 0, 1 }));
    }
  }

  [Test]
  public void Fields_OnPointsAndCells()
  {
    // Arrange
    using var grid = TwoCubes();

    // Act
    #region fields
    // a value per point: a temperature, and the one VTK colours by when nothing else is chosen
    using var pointData = grid.GetPointData();
    using (var temperature = vtkDoubleArray.FromArray(
             [20, 30, 40, 20, 30, 40, 20, 30, 40, 20, 30, 40]))
    {
      temperature.SetName("temperature");
      pointData.AddArray(temperature);
    }

    pointData.SetActiveScalars("temperature");

    // a value per cell: a vector of 3 components each
    using var cellData = grid.GetCellData();
    using (var velocity = vtkFloatArray.FromArray([1f, 0f, 0f, 3f, 4f, 0f], 3))
    {
      velocity.SetName("velocity");
      cellData.AddArray(velocity);
    }
    #endregion

    // Assert
    using var scalars = pointData.GetScalars();
    using var vectors = cellData.GetArray("velocity");
    using (Assert.EnterMultipleScope())
    {
      Assert.That(scalars.GetName(), Is.EqualTo("temperature"));
      Assert.That(vectors.GetNumberOfTuples(), Is.EqualTo(grid.GetNumberOfCells()));
      Assert.That(vectors.GetTuple3(1), Is.EqualTo(new double[] { 3, 4, 0 }));
    }
  }

  [Test]
  public void Field_ReadBack()
  {
    // Arrange
    using var grid = TwoCubes();
    using (var pointData = grid.GetPointData())
    using (var array = vtkDoubleArray.FromArray([20, 30, 40, 20, 30, 40, 20, 30, 40, 20, 30, 40]))
    {
      array.SetName("temperature");
      pointData.AddArray(array);
    }

    using (var cellData = grid.GetCellData())
    using (var array = vtkFloatArray.FromArray([1f, 0f, 0f, 3f, 4f, 0f], 3))
    {
      array.SetName("velocity");
      cellData.AddArray(array);
    }

    // Act
    #region read-back
    // GetArray gives any field as a vtkDataArray, null for a name that isn't there
    using var points = grid.GetPointData();
    using var temperature = points.GetArray("temperature");
    double[] values = temperature.ToDoubleArray(); // every value, converted to double
    double[] range = temperature.GetRange();       // smallest and largest

    // a vector's components one after another; GetRange(-1) is the magnitudes' range
    using var cells = grid.GetCellData();
    using var velocity = cells.GetArray("velocity");
    double[] components = velocity.ToDoubleArray(); // 1, 0, 0, 3, 4, 0
    double[] speeds = velocity.GetRange(-1);        // 1, 5

    // the typed array, for a copy without conversion
    using var typed = vtkFloatArray.SafeDownCast(velocity);
    float[] floats = typed.ToArray();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(values, Has.Length.EqualTo(12));
      Assert.That(range, Is.EqualTo(new double[] { 20, 40 }));
      Assert.That(components, Is.EqualTo(new double[] { 1, 0, 0, 3, 4, 0 }));
      Assert.That(speeds, Is.EqualTo(new double[] { 1, 5 }));
      Assert.That(floats, Is.EqualTo(new float[] { 1, 0, 0, 3, 4, 0 }));
      Assert.That(points.GetArray("pressure"), Is.Null);
    }
  }

  [Test]
  public void Bulk_CopiesAndSpans()
  {
    // Act
    #region bulk
    // FromArray: a new array with a copy of the values, in tuples of the components given
    using var displacement = vtkDoubleArray.FromArray([0.0, 0.0, -0.1, 0.0, 0.0, -0.2], 3);

    // ToArray: a copy of all values; CopyFrom: replaces them, whole tuples, resizing the array
    double[] copy = displacement.ToArray();
    displacement.CopyFrom([0.0, 0.0, -0.1, 0.0, 0.0, -0.2, 0.0, 0.0, -0.3]);

    // AsSpan (.NET 6 and later): the array's own memory, no copy; valid while the array lives
    // and isn't resized. Tell VTK when the values changed, so pipelines run again
    Span<double> values = displacement.AsSpan();
    values[2] = -0.15;
    displacement.Modified();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(copy, Has.Length.EqualTo(6));
      Assert.That(displacement.GetNumberOfTuples(), Is.EqualTo(3));
      Assert.That(displacement.GetComponent(0, 2), Is.EqualTo(-0.15));
    }
  }

  [Test]
  public void Span_Writing_DoesNotMarkTheArrayModified()
  {
    // Arrange
    using var array = vtkDoubleArray.FromArray([1.0, 2.0, 3.0]);
    var before = array.GetMTime();

    // Act
    array.AsSpan()[0] = 10;
    var written = array.GetMTime();
    array.Modified();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(written, Is.EqualTo(before));
      Assert.That(array.GetMTime(), Is.GreaterThan(before));
    }
  }

  [Test]
  public void CopyFrom_PartialTuple_Throws()
  {
    // Arrange
    using var array = vtkDoubleArray.FromArray([0.0, 0.0, 0.0], 3);

    // Act
    Action copy = () => array.CopyFrom([1.0, 2.0]);

    // Assert
    Assert.That(copy, Throws.ArgumentException);
  }

  [Test]
  public void ToDoubleArray_ConvertsAnyType()
  {
    // Act
    #region to-double
    // whatever the array stores: bytes, ints, floats, ids
    using var labels = vtkUnsignedCharArray.FromArray([0, 128, 255]);
    double[] values = labels.ToDoubleArray(); // 0, 128, 255
    #endregion

    // Assert
    Assert.That(values, Is.EqualTo(new double[] { 0, 128, 255 }));
  }

  // the grid region's two cubes
  private static vtkUnstructuredGrid TwoCubes()
  {
    double[] coordinates =
    [
      0, 0, 0, 1, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 0, 2, 1, 0,
      0, 0, 1, 1, 0, 1, 2, 0, 1, 0, 1, 1, 1, 1, 1, 2, 1, 1,
    ];
    var grid = new vtkUnstructuredGrid();
    using (var points = new vtkPoints())
    using (var pointArray = vtkDoubleArray.FromArray(coordinates, 3))
    {
      points.SetData(pointArray);
      grid.SetPoints(points);
    }

    using (var offsets = vtkIdTypeArray.FromArray([0, 8, 16]))
    using (var connectivity =
           vtkIdTypeArray.FromArray([0, 1, 4, 3, 6, 7, 10, 9, 1, 2, 5, 4, 7, 8, 11, 10]))
    using (var cells = new vtkCellArray())
    {
      cells.SetData(offsets, connectivity);
      grid.SetCells((int)VTKCellType.VTK_HEXAHEDRON, cells);
    }

    return grid;
  }
}
