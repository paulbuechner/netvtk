// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Results, a CAE recipe: a .vtu read (the repository README's example), coloured by a field with a
/// scalar bar, cut, contoured and warped. The result is <see cref="Beam"/>, written before each
/// test.
/// </summary>
[TestFixture]
public class Results : Files
{
  [SetUp]
  public void WriteTheResult() => Beam.Write("result.vtu");

  [Test]
  public void Read_TakesAField()
  {
    // Act
    #region read
    using var reader = new vtkXMLUnstructuredGridReader();
    reader.SetFileName("result.vtu");
    reader.Update();
    using var grid = reader.GetOutput();
    using var points = grid.GetPointData();
    using var stress = points.GetArray("stress");
    double[] values = stress.ToDoubleArray();
    #endregion

    // Assert (a value per point, the largest at the clamp's top)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(grid.GetNumberOfCells(), Is.EqualTo(Beam.Cells));
      Assert.That(values, Has.Length.EqualTo(Beam.Points));
      Assert.That(values.Max(), Is.EqualTo(Beam.MaxStress).Within(1e-9));
    }
  }

  [Test]
  public void Fields_AreThePointDataArrays()
  {
    // Arrange
    using var reader = Read();
    using var grid = reader.GetOutput();

    // Act
    #region fields
    // each field an array: a tuple per point, of 1 component (a scalar) or 3 (a vector)
    var fields = new List<string>();
    using var points = grid.GetPointData();
    for (var i = 0; i < points.GetNumberOfArrays(); i++)
    {
      using var array = points.GetArray(i);
      fields.Add(
        $"{array.GetName()}: {array.GetNumberOfTuples()} x {array.GetNumberOfComponents()}");
    }
    #endregion

    // Assert
    Assert.That(fields, Is.EqualTo(new[]
    {
      $"displacement: {Beam.Points} x 3", $"stress: {Beam.Points} x 1",
    }));
  }

  [Test]
  public void Colour_MapsTheFieldFromBlueToRed()
  {
    // Arrange
    using var reader = Read();
    using var readerPort = reader.GetOutputPort();

    // Act
    #region colour
    // the grid's outer faces, which a polydata mapper draws
    using var surface = new vtkDataSetSurfaceFilter();
    surface.SetInputConnection(readerPort);
    surface.Update();
    using var faces = surface.GetOutput();
    using var faceData = faces.GetPointData();
    using var stress = faceData.GetArray("stress");
    double[] range = stress.GetRange();

    // blue at the field's smallest value, red at its largest
    using var colors = new vtkLookupTable();
    colors.SetHueRange(0.667, 0.0);
    colors.SetRange(range[0], range[1]);
    colors.Build();

    using var mapper = new vtkPolyDataMapper();
    using var surfacePort = surface.GetOutputPort();
    mapper.SetInputConnection(surfacePort);
    mapper.SetLookupTable(colors);
    mapper.UseLookupTableScalarRangeOn();
    mapper.SetScalarModeToUsePointFieldData();
    mapper.SelectColorArray("stress");
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    #endregion

    // Assert (the outer faces of 20 x 2 x 4 hexahedra; the colours the mapper gives the points)
    using var mapped = mapper.MapScalars(faces, 1.0);
    var rgba = mapped.ToArray();
    var values = stress.ToDoubleArray();
    var lowest = Array.IndexOf(values, values.Min());
    var highest = Array.IndexOf(values, values.Max());
    using (Assert.EnterMultipleScope())
    {
      Assert.That(faces.GetNumberOfPolys(),
                  Is.EqualTo(2 * (Beam.Nx * Beam.Ny + Beam.Ny * Beam.Nz + Beam.Nx * Beam.Nz)));
      Assert.That(range, Is.EqualTo(new[] { -Beam.MaxStress, Beam.MaxStress }).Within(1e-9));
      Assert.That(rgba.Skip(4 * lowest).Take(3), Is.EqualTo(new byte[] { 0, 0, 255 }));
      Assert.That(rgba.Skip(4 * highest).Take(3), Is.EqualTo(new byte[] { 255, 0, 0 }));
    }
  }

  [Test]
  public void ScalarBar_ShowsTheColours()
  {
    // Arrange
    using var colors = new vtkLookupTable();
    colors.SetHueRange(0.667, 0.0);
    colors.Build();
    using var actor = new vtkActor();

    // Act
    #region scalar-bar
    // the colours and their values, in the renderer's corner
    using var bar = new vtkScalarBarActor();
    bar.SetLookupTable(colors);
    bar.SetTitle("stress (MPa)");
    bar.SetNumberOfLabels(5);

    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    renderer.AddViewProp(bar);
    #endregion

    // Assert
    using var props = renderer.GetViewProps();
    Assert.That(props.GetNumberOfItems(), Is.EqualTo(2));
  }

  [Test]
  public void Cut_IsASectionWithTheFieldOnIt()
  {
    // Arrange
    using var reader = Read();
    using var readerPort = reader.GetOutputPort();

    // Act
    #region cut
    // a section across the beam at x = 5.25: the field interpolated onto it
    using var plane = new vtkPlane();
    plane.SetOrigin(5.25, 0, 0);
    plane.SetNormal(1, 0, 0);
    using var cutter = new vtkCutter();
    cutter.SetCutFunction(plane);
    cutter.SetInputConnection(readerPort);
    cutter.Update();
    using var section = cutter.GetOutput();
    #endregion

    // Assert (the beam's 1 x 2 cross section; the stress there, linear in x)
    var bounds = section.GetBounds();
    using var data = section.GetPointData();
    using var stress = data.GetArray("stress");
    var expected = Beam.MaxStress * (Beam.Length - 5.25) / Beam.Length;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(section.GetNumberOfPolys(), Is.Positive);
      Assert.That(bounds, Is.EqualTo(new[] { 5.25, 5.25, -0.5, 0.5, -1, 1 }).Within(1e-9));
      Assert.That(stress.GetRange(), Is.EqualTo(new[] { -expected, expected }).Within(1e-6));
    }
  }

  [Test]
  public void Contours_AreAtTheirValues()
  {
    // Arrange
    using var reader = Read();
    using var readerPort = reader.GetOutputPort();

    // Act
    #region contours
    // isosurfaces of the stress at -2400, -1600, ..., 2400, each carrying its value
    using var contours = new vtkContourFilter();
    contours.SetInputConnection(readerPort);
    contours.SetInputArrayToProcess(
      "stress", (int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_POINTS);
    contours.GenerateValues(7, -2400, 2400);
    contours.ComputeScalarsOn();
    contours.Update();
    using var isosurfaces = contours.GetOutput();
    #endregion

    // Assert
    using var data = isosurfaces.GetPointData();
    using var scalars = data.GetScalars();
    var values = scalars.ToDoubleArray().Select(v => Math.Round(v, 6)).Distinct().Order();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(isosurfaces.GetNumberOfPolys(), Is.Positive);
      Assert.That(values, Is.EqualTo(new double[] { -2400, -1600, -800, 0, 800, 1600, 2400 }));
    }
  }

  [Test]
  public void ExodusII_KeepsTheResult()
  {
    // Arrange
    using var reader = Read();
    using var grid = reader.GetOutput();

    // Act
    #region exodus
    // the result as an Exodus II file
    using (var writer = new vtkExodusIIWriter())
    {
      writer.SetFileName("result.exo");
      writer.SetInputData(grid);
      writer.Write();
    }

    // and read back, every point field with it
    using var exodus = new vtkExodusIIReader();
    exodus.SetFileName("result.exo");
    exodus.UpdateInformation();
    exodus.SetAllArrayStatus((int)vtkExodusIIReader_ObjectType.NODAL, 1);
    exodus.Update();

    // a tree of blocks: the element blocks first, then each block's grid
    using var output = exodus.GetOutput();
    using var elementBlocks = vtkMultiBlockDataSet.SafeDownCast(output.GetBlock(0));
    using var block = vtkUnstructuredGrid.SafeDownCast(elementBlocks.GetBlock(0));
    #endregion

    // Assert
    using var data = block.GetPointData();
    using var stress = data.GetArray("stress");
    using (Assert.EnterMultipleScope())
    {
      Assert.That(block.GetNumberOfCells(), Is.EqualTo(Beam.Cells));
      Assert.That(stress.ToDoubleArray().Max(), Is.EqualTo(Beam.MaxStress).Within(1e-9));
    }
  }

  [Test]
  public void Warp_MovesThePointsByTheDisplacement()
  {
    // Arrange
    using var reader = Read();
    using var readerPort = reader.GetOutputPort();

    // Act
    #region warp
    // the points moved by twice their displacement, so the bending shows
    using var warp = new vtkWarpVector();
    warp.SetInputConnection(readerPort);
    warp.SetInputArrayToProcess("displacement",
                                (int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_POINTS);
    warp.SetScaleFactor(2);

    // what showed the grid, now on the warp's output
    using var surface = new vtkDataSetSurfaceFilter();
    using var warpPort = warp.GetOutputPort();
    surface.SetInputConnection(warpPort);
    surface.Update();
    using var bent = surface.GetOutput();
    #endregion

    // Assert (the tip's bottom: half the height down, and twice the tip's deflection)
    var bounds = bent.GetBounds();
    Assert.That(bounds[4], Is.EqualTo(-Beam.Height / 2 - 2 * Beam.TipDeflection).Within(1e-9));
  }

  [Test]
  [Category("Rendering")]
  public void Scene_Offscreen_ShowsBlueAndRed()
  {
    // Arrange (the colour and scalar bar steps, in a window off the screen)
    using var reader = Read();
    using var readerPort = reader.GetOutputPort();
    using var surface = new vtkDataSetSurfaceFilter();
    surface.SetInputConnection(readerPort);
    using var colors = new vtkLookupTable();
    colors.SetHueRange(0.667, 0.0);
    colors.SetRange(-Beam.MaxStress, Beam.MaxStress);
    colors.Build();
    using var mapper = new vtkPolyDataMapper();
    using var surfacePort = surface.GetOutputPort();
    mapper.SetInputConnection(surfacePort);
    mapper.SetLookupTable(colors);
    mapper.UseLookupTableScalarRangeOn();
    mapper.SetScalarModeToUsePointFieldData();
    mapper.SelectColorArray("stress");
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using var bar = new vtkScalarBarActor();
    bar.SetLookupTable(colors);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    renderer.AddViewProp(bar);
    using var camera = renderer.GetActiveCamera();
    camera.Elevation(30);
    renderer.ResetCamera();
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(320, 200);
    window.AddRenderer(renderer);

    // Act
    window.Render();

    // Assert (on black)
    var pixels = Rendering.Pixels(window);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(pixels.Count(p => p.R + p.G + p.B > 0), Is.GreaterThan(320 * 200 / 10));
      Assert.That(
        pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { R: > 150, B: < 100 }));
      Assert.That(
        pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { B: > 150, R: < 100 }));
    }
  }

  // result.vtu, read
  private static vtkXMLUnstructuredGridReader Read()
  {
    var reader = new vtkXMLUnstructuredGridReader();
    reader.SetFileName("result.vtu");
    reader.Update();
    return reader;
  }
}
