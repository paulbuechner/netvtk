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
/// Reverse Z (vtkRenderWindow's UseReverseZ, the port's patch): depth runs from 1 at the near plane
/// to 0 at the far one, in floats, so it resolves the same share of any distance whatever the ratio
/// of the clipping range; a depth read from the window goes back to its world point. Pixels are
/// found in the render, not at the window's middle.
/// </summary>
[TestFixture]
public class ReverseZTests
{
  private const int Side = 64;

  [Test]
  [Category("Rendering")]
  public void Render_ClippingRangeOf10To8_DrawsQuadsAMillionthOfTheirDistanceApartInDepthOrder()
  {
    // Arrange (the far quad drawn last, 0.01 behind the near one at 10,000: a hundred-millionth of
    // the far plane apart)
    using var scene = new QuadScene();
    scene.Add(-10_000, 1, 0, 0);
    scene.Add(-10_000.01, 0, 1, 0);
    scene.Camera.SetClippingRange(1e-3, 1e5);

    // Act
    scene.Render();

    // Assert: the near quad, red, wherever the quads show
    var pixels = scene.Pixels();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(scene.Window.GetUseReverseZ(), Is.True);
      Assert.That(Count(pixels, IsRed), Is.Positive);
      Assert.That(Count(pixels, IsGreen), Is.Zero);
    }
  }

  [Test]
  [Category("Rendering")]
  public void GetZbufferDataAtPoint_OverNothingAndAQuad_IsTheFarPlanesZeroAndTheQuadsReversedDepth()
  {
    // Arrange: a quad over the middle of the view
    using var scene = new QuadScene();
    scene.Add(-100, 1, 0, 0, 0.5);
    scene.Camera.SetClippingRange(1, 1000);
    scene.Render();
    var pixels = scene.Pixels();
    var background = Find(pixels, IsBlack);
    var quad = Find(pixels, IsRed);

    // Act
    var nothing = scene.Window.GetZbufferDataAtPoint(background[0], background[1]);
    var drawn = scene.Window.GetZbufferDataAtPoint(quad[0], quad[1]);

    // Assert: near (1 - 100 / far) / (100 (1 - near / far))
    using (Assert.EnterMultipleScope())
    {
      Assert.That(nothing, Is.Zero);
      Assert.That(drawn, Is.EqualTo(1 * (1000 - 100) / (999.0 * 100)).Within(1e-6));
    }
  }

  [Test]
  [Category("Rendering")]
  public void DisplayToWorld_OfTheDepthRead_IsThePointDrawnThere()
  {
    // Arrange
    using var scene = new QuadScene();
    scene.Add(-25_000, 1, 0, 0);
    scene.Camera.SetClippingRange(1e-2, 1e5);
    scene.Render();
    var quad = Find(scene.Pixels(), IsRed);
    var depth = scene.Window.GetZbufferDataAtPoint(quad[0], quad[1]);

    // Act
    scene.Renderer.SetDisplayPoint(quad[0], quad[1], depth);
    scene.Renderer.DisplayToWorld();
    var world = scene.Renderer.GetWorldPoint();

    // Assert
    Assert.That(world[2] / world[3], Is.EqualTo(-25_000).Within(0.5));
  }

  [Test]
  [Category("Rendering")]
  public void Render_PolygonOffsets_DrawALineOverTheQuadItLiesOn()
  {
    // Arrange (VTK's global resolution, restored after)
    using var scene = new QuadScene();
    scene.Add(-100, 1, 0, 0);
    scene.AddLine(-100, 0, 1, 0);
    scene.Camera.SetClippingRange(1e-2, 1e4);
    vtkMapper.SetResolveCoincidentTopologyToPolygonOffset();
    try
    {
      // Act
      scene.Render();
    }
    finally
    {
      vtkMapper.SetResolveCoincidentTopologyToDefault();
    }

    // Assert: the line, green, over the quad
    Assert.That(Count(scene.Pixels(), IsGreen), Is.Positive);
  }

  private static bool IsRed(byte[] pixels, int i) => pixels[i] > 200 && pixels[i + 1] < 50;

  private static bool IsGreen(byte[] pixels, int i) => pixels[i + 1] > 200 && pixels[i] < 50;

  private static bool IsBlack(byte[] pixels, int i) =>
    pixels[i] < 20 && pixels[i + 1] < 20 && pixels[i + 2] < 20;

  // how many pixels of the RGBA <pixels> are as <test> says
  private static int Count(byte[] pixels, Func<byte[], int, bool> test)
  {
    var count = 0;
    for (var i = 0; i < pixels.Length; i += 4)
    {
      if (test(pixels, i))
      {
        count++;
      }
    }

    return count;
  }

  // the first pixel, from the bottom left, of the RGBA <pixels> that is as <test> says
  private static int[] Find(byte[] pixels, Func<byte[], int, bool> test)
  {
    for (var i = 0; i < pixels.Length; i += 4)
    {
      if (test(pixels, i))
      {
        return [i / 4 % Side, i / 4 / Side];
      }
    }

    Assert.Fail("no such pixel in the render");
    return [];
  }

  // square quads square to the view axis, a camera at the origin looking down -z (30°), in an
  // offscreen window with reverse Z
  private sealed class QuadScene : IDisposable
  {
    private readonly List<IDisposable> _objects = [];

    public QuadScene()
    {
      Window.SetOffScreenRendering(1);
      Window.SetUseReverseZ(true);
      Window.SetSize(Side, Side);
      Window.AddRenderer(Renderer);
      Camera = Renderer.GetActiveCamera();
      Camera.SetPosition(0, 0, 0);
      Camera.SetFocalPoint(0, 0, -1);
      Camera.SetViewUp(0, 1, 0);
      Camera.SetViewAngle(30);
    }

    public vtkRenderWindow Window { get; } = new();

    public vtkRenderer Renderer { get; } = new();

    public vtkCamera Camera { get; }

    // a quad at depth z (negative) that covers the view, or the middle share of it
    public void Add(double z, double red, double green, double blue, double share = 1)
    {
      var half = -z * Math.Tan(15 * Math.PI / 180) * share;
      var points = new vtkPoints();
      points.InsertNextPoint(-half, -half, z);
      points.InsertNextPoint(half, -half, z);
      points.InsertNextPoint(half, half, z);
      points.InsertNextPoint(-half, half, z);
      var cells = new vtkCellArray();
      cells.InsertNextCell(4);
      for (var i = 0; i < 4; i++)
      {
        cells.InsertCellPoint(i);
      }

      var data = new vtkPolyData();
      data.SetPoints(points);
      data.SetPolys(cells);
      Show(data, red, green, blue, 1);
      _objects.AddRange(new IDisposable[] { points, cells, data });
    }

    // a line across the view at depth z
    public void AddLine(double z, double red, double green, double blue)
    {
      var half = -z * Math.Tan(15 * Math.PI / 180);
      var points = new vtkPoints();
      points.InsertNextPoint(-half, 0, z);
      points.InsertNextPoint(half, 0, z);
      var cells = new vtkCellArray();
      cells.InsertNextCell(2);
      cells.InsertCellPoint(0);
      cells.InsertCellPoint(1);
      var data = new vtkPolyData();
      data.SetPoints(points);
      data.SetLines(cells);
      Show(data, red, green, blue, 3);
      _objects.AddRange(new IDisposable[] { points, cells, data });
    }

    public void Render() => Window.Render();

    // the RGBA pixels of the render, the bottom row first
    public byte[] Pixels()
    {
      using var pixels = new vtkUnsignedCharArray();
      Window.GetRGBACharPixelData(0, 0, Side - 1, Side - 1, 0, pixels);
      return pixels.ToArray();
    }

    public void Dispose()
    {
      Window.Dispose();
      Camera.Dispose();
      Renderer.Dispose();
      for (var i = _objects.Count - 1; i >= 0; i--)
      {
        _objects[i].Dispose();
      }
    }

    private void Show(vtkPolyData data, double red, double green, double blue, float width)
    {
      var mapper = new vtkPolyDataMapper();
      mapper.SetInputData(data);
      var actor = new vtkActor();
      actor.SetMapper(mapper);
      using (var property = actor.GetProperty())
      {
        property.SetColor(red, green, blue);
        property.SetLineWidth(width);
        property.LightingOff();
      }

      Renderer.AddActor(actor);
      _objects.AddRange(new IDisposable[] { mapper, actor });
    }
  }
}
