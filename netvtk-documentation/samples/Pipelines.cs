// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Pipelines: sources, filters connected by ports, demand-driven updates, and what draws them
/// (mappers, actors, renderers), all without a window.
/// </summary>
[TestFixture]
public class Pipelines
{
  [Test]
  public void Source_MakesItsOutputOnUpdate()
  {
    // Act
    #region source
    // a source makes data: here a sphere's triangles
    using var sphere = new vtkSphereSource();
    sphere.SetRadius(2);
    sphere.SetThetaResolution(16);
    sphere.SetPhiResolution(8);
    sphere.Update();
    using var polyData = sphere.GetOutput();
    long points = polyData.GetNumberOfPoints();
    long triangles = polyData.GetNumberOfPolys();
    #endregion

    // Assert (two poles and 6 rings of 16 between; 16 triangles at each pole, 2 per quad between)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(points, Is.EqualTo(2 + 6 * 16));
      Assert.That(triangles, Is.EqualTo(2 * 16 + 2 * 16 * 5));
    }
  }

  [Test]
  public void Filters_RunOnDemand()
  {
    // Act
    #region connect
    // a filter's input is another's output port: sphere -> elevation -> shrink
    using var sphere = new vtkSphereSource();
    using var elevation = new vtkElevationFilter();
    using var spherePort = sphere.GetOutputPort();
    elevation.SetInputConnection(spherePort);
    using var shrink = new vtkShrinkFilter();
    using var elevationPort = elevation.GetOutputPort();
    shrink.SetInputConnection(elevationPort);

    // nothing has run yet: Update on the last one runs all three
    shrink.Update();
    using var shrunk = shrink.GetOutput(); // a vtkUnstructuredGrid
    long before = shrunk.GetNumberOfCells();

    // a changed parameter marks the sphere modified: the next Update runs it and what follows
    sphere.SetThetaResolution(32);
    shrink.Update();
    long after = shrunk.GetNumberOfCells();
    #endregion

    // Assert (vtkSphereSource's defaults: 8 by 8; the output object updated in place)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(before, Is.EqualTo(2 * 8 + 2 * 8 * 5));
      Assert.That(after, Is.EqualTo(2 * 32 + 2 * 32 * 5));
    }
  }

  [Test]
  public void InputData_TakesADataObject()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();
    using var polyData = sphere.GetOutput();

    // Act
    #region input-data
    // data no algorithm made (built in C#, read earlier): SetInputData instead of a port
    using var normals = new vtkPolyDataNormals();
    normals.SetInputData(polyData);
    normals.Update();
    using var withNormals = normals.GetOutput();
    using var pointData = withNormals.GetPointData();
    using var normalArray = pointData.GetNormals();
    #endregion

    // Assert
    Assert.That(normalArray.GetNumberOfTuples(), Is.EqualTo(withNormals.GetNumberOfPoints()));
  }

  [Test]
  public void Scene_HoldsTheActor()
  {
    // Arrange
    using var sphere = new vtkSphereSource();

    // Act
    #region scene
    // a mapper turns data into graphics, an actor places it with its look, a renderer holds actors
    using var mapper = new vtkPolyDataMapper();
    using var port = sphere.GetOutputPort();
    mapper.SetInputConnection(port);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    actor.SetPosition(0, 0, 1);
    using (var look = actor.GetProperty())
    {
      look.SetColor(1.0, 0.5, 0.0); // orange
      look.SetOpacity(0.5);
    }

    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    renderer.SetBackground(1, 1, 1);
    #endregion

    // Assert
    using var property = actor.GetProperty();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(renderer.VisibleActorCount(), Is.EqualTo(1));
      Assert.That(property.GetColor(), Is.EqualTo(new[] { 1.0, 0.5, 0.0 }));
      Assert.That(actor.GetPosition(), Is.EqualTo(new[] { 0.0, 0.0, 1.0 }));
    }
  }
}
