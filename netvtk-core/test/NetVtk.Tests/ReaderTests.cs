// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// Simulation result formats beyond VTK's own (the port's exodus and cgns features): a mesh with a
/// field, written and read back.
/// </summary>
[TestFixture]
public class ReaderTests
{
  private string _directory = null!;

  [OneTimeSetUp]
  public void CreateTheDirectory()
  {
    _directory = Path.Combine(Path.GetTempPath(), $"netvtk-readers-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_directory);
  }

  [OneTimeTearDown]
  public void DeleteTheDirectory() => Directory.Delete(_directory, true);

  [Test]
  public void ExodusII_WrittenAndRead_KeepsTheMeshAndItsField()
  {
    // Arrange (a hexahedron, a temperature at its corners)
    var path = Path.Combine(_directory, "cube.exo");
    using (var grid = Cube())
    using (var writer = new vtkExodusIIWriter())
    {
      writer.SetFileName(path);
      writer.SetInputData(grid);
      writer.Write();
    }

    // Act
    using var reader = new vtkExodusIIReader();
    reader.SetFileName(path);
    reader.UpdateInformation();
    reader.SetAllArrayStatus((int)vtkExodusIIReader_ObjectType.NODAL, 1);
    reader.Update();

    // Assert (the element blocks, then the first block)
    using var output = reader.GetOutput();
    using var blocks = vtkMultiBlockDataSet.SafeDownCast(output.GetBlock(0));
    using var block = vtkUnstructuredGrid.SafeDownCast(blocks.GetBlock(0));
    using var pointData = block.GetPointData();
    using var temperature = pointData.GetArray("temperature");
    using (Assert.EnterMultipleScope())
    {
      Assert.That(block.GetNumberOfCells(), Is.EqualTo(1));
      Assert.That(block.GetNumberOfPoints(), Is.EqualTo(8));
      Assert.That(temperature?.GetRange(0), Is.EqualTo(new double[] { 20, 27 }));
    }
  }

#if NET5_0_OR_GREATER
  [Test]
  public void Cgns_Read_GivesTheMeshAndItsField()
  {
    // Arrange (the cube again, in a CGNS file: VTK reads CGNS only, its CGNS library writes it)
    var path = Path.Combine(_directory, "cube.cgns");
    CgnsFile.Write(path, Corners, [1, 2, 3, 4, 5, 6, 7, 8], "Temperature",
                   [20, 21, 22, 23, 24, 25, 26, 27]);

    // Act
    using var reader = new vtkCGNSReader();
    reader.SetFileName(path);
    reader.UpdateInformation();
    reader.EnableAllPointArrays();
    reader.Update();

    // Assert (the base, its zone: the leaves of the reader's tree of blocks)
    using var output = reader.GetOutput();
    long cells = 0;
    double[]? range = null;
    using var leaves = output.NewIterator();
    for (leaves.InitTraversal(); leaves.IsDoneWithTraversal() == 0; leaves.GoToNextItem())
    {
      using var leaf = vtkDataSet.SafeDownCast(leaves.GetCurrentDataObject());
      cells += leaf.GetNumberOfCells();
      using var pointData = leaf.GetPointData();
      using var temperature = pointData.GetArray("Temperature");
      range ??= temperature?.GetRange(0);
    }

    using (Assert.EnterMultipleScope())
    {
      Assert.That(output.GetNumberOfPoints(), Is.EqualTo(8));
      Assert.That(cells, Is.EqualTo(1));
      Assert.That(range, Is.EqualTo(new double[] { 20, 27 }));
    }
  }
#endif

  // a unit cube's corners, x, y, z after each other, in VTK's hexahedron order
  private static readonly double[] Corners =
    [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1];

  // a unit cube as one hexahedron, its temperature 20 to 27 over its corners
  private static vtkUnstructuredGrid Cube()
  {
    var grid = new vtkUnstructuredGrid();
    using var points = new vtkPoints();
    using var corners = new vtkIdList();
    for (var p = 0; p < Corners.Length / 3; p++)
    {
      corners.InsertNextId(points.InsertNextPoint(Corners[3 * p], Corners[3 * p + 1],
                                                  Corners[3 * p + 2]));
    }

    grid.SetPoints(points);
    grid.InsertNextCell(12, corners); // VTK_HEXAHEDRON
    using var temperature = vtkDoubleArray.FromArray([20, 21, 22, 23, 24, 25, 26, 27]);
    temperature.SetName("temperature");
    using var pointData = grid.GetPointData();
    pointData.AddArray(temperature);
    return grid;
  }
}
