// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// A pipeline as in C++: a source, its output as point data, a mapper, an actor and a renderer.
/// </summary>
[TestFixture]
public class PipelineTests
{
  [Test]
  public void PointData_TakesAScalarField()
  {
    // Arrange
    using var cone = new vtkConeSource();
    cone.Update();
    using var output = cone.GetOutput();
    var points = output.GetNumberOfPoints();
    using var field = new vtkDoubleArray();
    field.SetName("height");
    for (long i = 0; i < points; i++)
    {
      field.InsertNextValue(output.GetPoint(i)[2]);
    }

    // Act
    using var pointData = output.GetPointData();
    pointData.SetScalars(field);

    // Assert
    using var scalars = pointData.GetScalars();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(scalars.GetName(), Is.EqualTo("height"));
      Assert.That(scalars.GetNumberOfTuples(), Is.EqualTo(points));
    }
  }

  [Test]
  [Category("Rendering")]
  public void Render_Offscreen_DrawsTheActor()
  {
    // Arrange
    using var cone = new vtkConeSource();
    using var mapper = new vtkPolyDataMapper();
    using var port = cone.GetOutputPort();
    mapper.SetInputConnection(port);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(64, 64);
    window.AddRenderer(renderer);

    // Act
    window.Render();

    // Assert
    Assert.That(renderer.VisibleActorCount(), Is.EqualTo(1));
  }
}
