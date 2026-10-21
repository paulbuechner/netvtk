// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// Picking from the hardware selector's buffers, captured once: what a pixel shows
/// (GetPixelInformation, a value class whose data are properties; its prop has a reference of its
/// own). A window with samples keeps them, and its pixels' ids don't blend: the passes render with
/// one sample, into the window's resolve framebuffer (the port's patch).
/// </summary>
[TestFixture]
public class HardwareSelectorTests
{
  private const int Size = 64;

  [Test]
  [Category("Rendering")]
  public void GetPixelInformation_WhereTheSphereShows_IsItsActor()
  {
    // Arrange
    using var scene = new Scene();

    // Act
    using var hit = scene.Selector.GetPixelInformation(scene.SphereMiddle());

    // Assert
    using var prop = hit.Prop;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(hit.Valid, Is.True);
      Assert.That(prop?.NativePointer, Is.EqualTo(scene.Actor.NativePointer));
      Assert.That(hit.AttributeID, Is.GreaterThanOrEqualTo(0));
    }
  }

  [Test]
  [Category("Rendering")]
  public void GetPixelInformation_WhereTheBackgroundShows_IsNotValid()
  {
    // Arrange
    using var scene = new Scene();

    // Act
    using var miss = scene.Selector.GetPixelInformation(scene.Background());

    // Assert
    using var prop = miss.Prop;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(miss.Valid, Is.False);
      Assert.That(prop, Is.Null);
    }
  }

  [Test]
  [Category("Rendering")]
  public void GetPixelInformation_ActorsSharingAnEdgeWithSamples_NamesNoThirdActor()
  {
    // Arrange (the two halves of a square, cut along its diagonal)
    using var scene = new SampledScene([-0.9, -0.9, 0, 0.5, -0.9, 0, -0.9, 0.5, 0],
                                       [0.5, -0.9, 0, 0.5, 0.5, 0, -0.9, 0.5, 0]);

    // Act
    var named = scene.NameEveryPixel();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(named.First, Is.Positive);
      Assert.That(named.Second, Is.Positive);
      Assert.That(named.Third, Is.Zero);
    }
  }

  [Test]
  [Category("Rendering")]
  public void GetPixelInformation_ActorsCrossingWithSamples_NamesNoThirdActor()
  {
    // Arrange (two squares tilted against each other, which cross along a slanted line)
    using var scene = new SampledScene(
      [-0.9, -0.9, -0.4, 0.5, -0.9, 0.3, 0.5, 0.5, 0.0, -0.9, 0.5, -0.7],
      [-0.9, -0.9, 0.3, 0.5, -0.9, -0.4, 0.5, 0.5, -0.1, -0.9, 0.5, 0.6]);

    // Act
    var named = scene.NameEveryPixel();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(named.First, Is.Positive);
      Assert.That(named.Second, Is.Positive);
      Assert.That(named.Third, Is.Zero);
    }
  }

  [Test]
  [Category("Rendering")]
  public void CaptureBuffers_WithSamples_KeepsTheWindowsSamples()
  {
    // Arrange
    using var scene = new SampledScene([-0.9, -0.9, 0, 0.5, -0.9, 0, -0.9, 0.5, 0],
                                       [0.5, -0.9, 0, 0.5, 0.5, 0, -0.9, 0.5, 0]);

    // Act
    scene.Selector.CaptureBuffers();

    // Assert
    Assert.That(scene.Window.GetMultiSamples(), Is.EqualTo(SampledScene.Samples));
  }

  // a sphere on black in an offscreen window, rendered, the selector's buffers captured over all of
  // it. Positions come from the render, not the window's middle: win-x86 draws the scene off it
  // (2026-10-17, NetVtk's earlier builds too).
  private sealed class Scene : IDisposable
  {
    private readonly vtkSphereSource _sphere = new();
    private readonly vtkPolyDataMapper _mapper = new();
    private readonly vtkRenderer _renderer = new();
    private readonly vtkRenderWindow _window = new();
    private readonly byte[] _rgba;

    public Scene()
    {
      using (var port = _sphere.GetOutputPort())
      {
        _mapper.SetInputConnection(port);
      }

      Actor.SetMapper(_mapper);
      _renderer.AddActor(Actor);
      _window.SetOffScreenRendering(1);
      _window.SetSize(Size, Size);
      _window.AddRenderer(_renderer);
      _renderer.ResetCamera();
      _window.Render();
      using (var pixels = new vtkUnsignedCharArray())
      {
        _window.GetRGBACharPixelData(0, 0, Size - 1, Size - 1, 0, pixels);
        _rgba = pixels.ToArray();
      }

      Selector.SetRenderer(_renderer);
      Selector.SetArea(0, 0, Size - 1, Size - 1);
      Assert.That(Selector.CaptureBuffers(), Is.True);
    }

    public vtkActor Actor { get; } = new();

    // abstract: its New() asks the factories, which RenderingCore's companion registers
    public vtkHardwareSelector Selector { get; } = vtkHardwareSelector.New();

    // the display position in the middle of the pixels that show the sphere
    public uint[] SphereMiddle()
    {
      long x = 0, y = 0, count = 0;
      for (var row = 0; row < Size; row++)
      {
        for (var column = 0; column < Size; column++)
        {
          if (ShowsTheSphere(column, row))
          {
            x += column;
            y += row;
            count++;
          }
        }
      }

      Assert.That(count, Is.Positive, "the render shows the sphere");
      return [(uint)(x / count), (uint)(y / count)];
    }

    // the display position of the background pixel farthest from the sphere's middle
    public uint[] Background()
    {
      var middle = SphereMiddle();
      uint[] farthest = null!;
      long distance = -1;
      for (var row = 0; row < Size; row++)
      {
        for (var column = 0; column < Size; column++)
        {
          long dx = column - middle[0], dy = row - middle[1];
          if (!ShowsTheSphere(column, row) && dx * dx + dy * dy > distance)
          {
            distance = dx * dx + dy * dy;
            farthest = [(uint)column, (uint)row];
          }
        }
      }

      return farthest;
    }

    public void Dispose()
    {
      Selector.Dispose();
      _window.Dispose();
      _renderer.Dispose();
      Actor.Dispose();
      _mapper.Dispose();
      _sphere.Dispose();
    }

    private bool ShowsTheSphere(int column, int row)
    {
      var i = 4 * (row * Size + column);
      return _rgba[i] + _rgba[i + 1] + _rgba[i + 2] > 30;
    }
  }

  // Three red and blue actors in a window with samples, seen from the front in parallel: a first
  // and a second red one, which meet, and between them in the renderer a small blue one in a corner
  // of its own. The selector numbers the actors in the renderer's order, so a pixel whose samples
  // blended the first's and the second's ids would name the third.
  private sealed class SampledScene : IDisposable
  {
    public const int Samples = 4;

    // pixels per side
    private const int Side = 200;

    private readonly List<IDisposable> _objects = [];
    private readonly vtkRenderer _renderer = new();
    private readonly vtkActor _first;
    private readonly vtkActor _third;
    private readonly vtkActor _second;

    // each actor a fan of triangles over its corners (x, y, z each)
    public SampledScene(double[] first, double[] second)
    {
      _first = Add(first, 1, 0, 0);
      _third = Add([0.7, 0.7, 0, 0.95, 0.7, 0, 0.7, 0.95, 0], 0, 0, 1);
      _second = Add(second, 1, 0, 0);
      using (var camera = _renderer.GetActiveCamera())
      {
        camera.ParallelProjectionOn();
        camera.SetPosition(0, 0, 5);
        camera.SetFocalPoint(0, 0, 0);
        camera.SetViewUp(0, 1, 0);
        camera.SetParallelScale(1);
      }

      _renderer.ResetCameraClippingRange();
      Window.SetOffScreenRendering(1);
      Window.SetMultiSamples(Samples);
      Window.SetSize(Side, Side);
      Window.AddRenderer(_renderer);
      Window.Render();
      Selector.SetRenderer(_renderer);
      Selector.SetFieldAssociation((int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_CELLS);
      Selector.SetArea(0, 0, Side - 1, Side - 1);
    }

    public vtkRenderWindow Window { get; } = new();

    public vtkHardwareSelector Selector { get; } = vtkHardwareSelector.New();

    // how many pixels a capture names each actor at, the third's own left out: where the render
    // shows blue, and the pixels next to them
    public Names NameEveryPixel()
    {
      var blue = BlueBounds();
      Assert.That(Selector.CaptureBuffers(), Is.True);
      int first = 0, second = 0, third = 0;
      for (uint row = 0; row < Side; row++)
      {
        for (uint column = 0; column < Side; column++)
        {
          using var hit = Selector.GetPixelInformation([column, row]);
          using var prop = hit.Prop;
          var pointer = hit.Valid ? prop?.NativePointer : null;
          if (pointer == _first.NativePointer)
          {
            first++;
          }
          else if (pointer == _second.NativePointer)
          {
            second++;
          }
          else if (hit.Valid
                   && (column < blue[0] - 1
                       || column > blue[1] + 1
                       || row < blue[2] - 1
                       || row > blue[3] + 1))
          {
            third++;
          }
        }
      }

      return new Names(first, second, third);
    }

    public void Dispose()
    {
      Selector.Dispose();
      Window.Dispose();
      _renderer.Dispose();
      for (var i = _objects.Count - 1; i >= 0; i--)
      {
        _objects[i].Dispose();
      }
    }

    private vtkActor Add(double[] corners, double red, double green, double blue)
    {
      var points = new vtkPoints();
      var triangles = new vtkCellArray();
      var count = corners.Length / 3;
      for (var i = 0; i < count; i++)
      {
        points.InsertNextPoint(corners[3 * i], corners[3 * i + 1], corners[3 * i + 2]);
      }

      for (var i = 1; i + 1 < count; i++)
      {
        triangles.InsertNextCell(3);
        triangles.InsertCellPoint(0);
        triangles.InsertCellPoint(i);
        triangles.InsertCellPoint(i + 1);
      }

      var surface = new vtkPolyData();
      surface.SetPoints(points);
      surface.SetPolys(triangles);
      var mapper = new vtkPolyDataMapper();
      mapper.SetInputData(surface);
      var actor = new vtkActor();
      actor.SetMapper(mapper);
      using (var property = actor.GetProperty())
      {
        property.SetColor(red, green, blue);
        property.LightingOff();
      }

      _renderer.AddActor(actor);
      _objects.AddRange(new IDisposable[] { points, triangles, surface, mapper, actor });
      return actor;
    }

    // the columns and rows (left, right, bottom, top) of the pixels the render shows blue at
    private int[] BlueBounds()
    {
      using var pixels = new vtkUnsignedCharArray();
      Window.GetRGBACharPixelData(0, 0, Side - 1, Side - 1, 0, pixels);
      var rgba = pixels.ToArray();
      int[] bounds = [Side, -1, Side, -1];
      for (var row = 0; row < Side; row++)
      {
        for (var column = 0; column < Side; column++)
        {
          var i = 4 * (row * Side + column);
          if (rgba[i + 2] > 100 && rgba[i] < 60)
          {
            bounds =
            [
              Math.Min(bounds[0], column), Math.Max(bounds[1], column),
              Math.Min(bounds[2], row), Math.Max(bounds[3], row)
            ];
          }
        }
      }

      return bounds;
    }
  }

  private sealed class Names(int first, int second, int third)
  {
    public int First { get; } = first;

    public int Second { get; } = second;

    public int Third { get; } = third;
  }
}
