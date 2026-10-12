// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Viewer.Tests;

/// <summary>
/// The sample result written and read back, and the scene's pipeline over it: no window, so the
/// filters run (Update) but nothing renders.
/// </summary>
[TestFixture]
public class ResultTests
{
  private string _path = null!;
  private ResultDocument _document = null!;

  [OneTimeSetUp]
  public void WriteTheSample()
  {
    VtkOutput.Install();
    _path = Path.Combine(Path.GetTempPath(), $"netvtk-viewer-tests-{Environment.ProcessId}.vtu");
    SampleResult.Write(_path);
    _document = ResultDocument.Read(_path);
  }

  [OneTimeTearDown]
  public void DeleteTheSample()
  {
    _document.Dispose();
    File.Delete(_path);
  }

  [Test]
  public void Sample_ReadsBackWithItsFields()
  {
    // Act
    var fields = _document.Fields;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(_document.Grid.GetNumberOfCells(), Is.EqualTo(40 * 4 * 8));
      Assert.That(fields, Is.EqualTo(new[]
      {
        new ResultField(SampleResult.Displacement, FieldLocation.Points, 3),
        new ResultField(SampleResult.Stress, FieldLocation.Points, 1),
        new ResultField(SampleResult.Temperature, FieldLocation.Points, 1),
      }));
    }
  }

  [Test]
  public void Sample_DeflectsTheTipByItsDeflection()
  {
    // Arrange
    using var points = _document.Grid.GetPointData();
    using var displacement = points.GetArray(SampleResult.Displacement);

    // Act
    var z = displacement.GetRange(2);

    // Assert
    Assert.That(z[0], Is.EqualTo(-SampleResult.TipDeflection).Within(1e-9));
  }

  [Test]
  public void Read_NotAResult_ThrowsWithTheReason()
  {
    // Arrange
    var path = Path.Combine(Path.GetTempPath(),
                            $"netvtk-viewer-tests-{Environment.ProcessId}-text.vtu");
    File.WriteAllText(path, "not a VTK file");

    // Act
    Action read = () => ResultDocument.Read(path);

    // Assert
    try
    {
      Assert.That(read, Throws.TypeOf<InvalidDataException>().With.Message.Not.Empty);
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public void Scene_Defaults_ColourByAScalarWarped()
  {
    // Arrange
    using var renderer = new vtkRenderer();
    using var scene = new ResultScene(_document, renderer);
    using var points = _document.Grid.GetPointData();
    using var displacement = points.GetArray(SampleResult.Displacement);
    var largest = displacement.GetRange(-1)[1];

    // Act
    var options = scene.DefaultOptions();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(options.Field.Name, Is.EqualTo(SampleResult.Stress));
      Assert.That(options.Warp);
      Assert.That(scene.AutoWarpScale * largest, Is.EqualTo(0.1 * scene.Diagonal).Within(1e-9),
                  "the largest displacement moves a tenth of the diagonal");
    }
  }

  [Test]
  public void Scene_CutAndContours_ProduceGeometry()
  {
    // Arrange
    using var renderer = new vtkRenderer();
    using var scene = new ResultScene(_document, renderer);
    var options = scene.DefaultOptions() with { Cut = true, Contours = true };

    // Act
    scene.Apply(options);
    scene.Cutter.Update();
    scene.Contour.Update();

    // Assert
    using var cut = scene.Cutter.GetOutput();
    using var contours = scene.Contour.GetOutput();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(cut.GetNumberOfCells(), Is.Positive, "the plane across the beam");
      Assert.That(contours.GetNumberOfCells(), Is.Positive, "isosurfaces of the stress");
      Assert.That(scene.ScalarBar.GetTitle(), Is.EqualTo(SampleResult.Stress));
    }
  }

  [Test]
  public void Scene_Warp_MovesTheTip()
  {
    // Arrange
    using var renderer = new vtkRenderer();
    using var scene = new ResultScene(_document, renderer);

    // Act
    scene.Apply(scene.DefaultOptions() with { Warp = true, WarpScale = 1 });
    scene.WarpFilter.Update();

    // Assert
    using var warped = scene.WarpFilter.GetOutput();
    var bounds = warped.GetBounds();
    Assert.That(bounds[4],
                Is.EqualTo(-SampleResult.Height / 2 - SampleResult.TipDeflection).Within(0.05));
  }

  [Test]
  public void Scene_VectorField_ColoursByMagnitudeWithoutContours()
  {
    // Arrange
    using var renderer = new vtkRenderer();
    using var scene = new ResultScene(_document, renderer);
    var displacement = _document.Fields.First(f => f.Name == SampleResult.Displacement);

    // Act
    scene.Apply(scene.DefaultOptions() with { Field = displacement, Contours = true });

    // Assert
    Assert.That(scene.ScalarBar.GetTitle(), Is.EqualTo($"|{SampleResult.Displacement}|"));
  }

  [Test]
  [Category("Rendering")]
  public void Scene_Offscreen_DrawsTheResult()
  {
    // Arrange (the whole scene, as a viewer shows it, in a window off the screen)
    using var renderer = new vtkRenderer();
    using var scene = new ResultScene(_document, renderer);
    scene.Apply(scene.DefaultOptions() with { Cut = true, Contours = true });
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(320, 200);
    window.AddRenderer(renderer);
    renderer.ResetCamera();

    // Act
    window.Render();

    // Assert (on black: the result fills the view, from blue to red)
    var pixels = Pixels.Of(window);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(pixels.Count(p => p.R + p.G + p.B > 0), Is.GreaterThan(320 * 200 / 10));
      Assert.That(
        pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { R: > 150, B: < 100 }));
      Assert.That(
        pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { B: > 150, R: < 100 }));
    }
  }
}
