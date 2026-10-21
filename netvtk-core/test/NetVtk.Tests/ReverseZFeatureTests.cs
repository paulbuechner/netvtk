// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// Reverse Z beyond the plain polygon mappers (the port's patch 0005): what computes, compares or
/// converts depth itself (impostor spheres and tubes, 2-D actors, translucency, SSAO, EDL, volumes,
/// pickers) renders and picks with reverse Z as it does without.
/// </summary>
[TestFixture]
public class ReverseZFeatureTests
{
  private const int Side = 96;

  // a channel this far apart is another colour; a share of the pixels this large may differ at
  // edges, where the depths' precision tips a pixel either way
  private const int ChannelTolerance = 24;
  private const double PixelShareTolerance = 0.01;

  public enum Feature
  {
    PointsAsSpheres,
    LinesAsTubes,
    TextOverGeometry,
    DepthPeeling,
    Ssao,
    EyeDomeLighting,
    VolumeBehindAPlane,
  }

  [Test]
  [Category("Rendering")]
  public void Render_ReverseZ_DrawsWhatConventionalDepthDraws([Values] Feature feature)
  {
    // Arrange
    var conventional = Render(feature, reverseZ: false);

    // Act
    var reversed = Render(feature, reverseZ: true);

    // Assert (and the scene isn't empty: both alike for want of anything drawn says nothing)
    var differing = Enumerable.Range(0, Side * Side).Count(p => Differs(conventional, reversed, p));
    using (Assert.EnterMultipleScope())
    {
      Assert.That(Enumerable.Range(0, Side * Side).Count(p => !IsBlack(conventional, p)),
                  Is.GreaterThan(Side));
      Assert.That(differing, Is.LessThanOrEqualTo(PixelShareTolerance * Side * Side));
    }
  }

  [Test]
  [Category("Rendering")]
  public void WorldPointPicker_OnAQuadWithReverseZ_FindsItsPoint()
  {
    // Arrange (a quad over the view 10 below the eye)
    using var scene = new Scene(reverseZ: true);
    scene.Quad(-10, 1, 0, 0);
    scene.Render();
    using var picker = new vtkWorldPointPicker();

    // Act
    picker.Pick(Side / 2, Side / 2, 0, scene.Renderer);

    // Assert
    Assert.That(picker.GetPickPosition()[2], Is.EqualTo(-10).Within(1e-2));
  }

  [Test]
  [Category("Rendering")]
  public void PropPicker_TwoQuadsWithReverseZ_PicksTheNearer()
  {
    // Arrange (the far quad added last)
    using var scene = new Scene(reverseZ: true);
    var near = scene.Quad(-10, 1, 0, 0);
    scene.Quad(-20, 0, 1, 0);
    scene.Render();
    using var picker = new vtkPropPicker();

    // Act
    picker.Pick(Side / 2, Side / 2, 0, scene.Renderer);

    // Assert
    using var picked = picker.GetActor();
    Assert.That(picked?.NativePointer, Is.EqualTo(near.NativePointer));
  }

  // the RGBA pixels of <feature>'s scene with or without reverse Z
  private static byte[] Render(Feature feature, bool reverseZ)
  {
    using var scene = new Scene(reverseZ);
    switch (feature)
    {
      case Feature.PointsAsSpheres:
        scene.Points([[0, 0, -10, 1, 0, 0], [0.4, 0, -12, 0, 1, 0]], 40, tubes: false);
        break;
      case Feature.LinesAsTubes:
        scene.Points([[-2, -0.5, -10, 1, 0, 0], [2, 0.5, -10, 1, 0, 0]], 14, tubes: true);
        scene.Points([[-2, 0.5, -12, 0, 1, 0], [2, -0.5, -12, 0, 1, 0]], 14, tubes: true);
        break;
      case Feature.TextOverGeometry:
        scene.Quad(-10, 0, 0, 1);
        scene.Text("Reverse Z");
        break;
      case Feature.DepthPeeling:
        scene.Window.AlphaBitPlanesOn();
        scene.Window.SetMultiSamples(0);
        scene.Renderer.UseDepthPeelingOn();
        scene.Renderer.SetMaximumNumberOfPeels(4);
        scene.Renderer.SetOcclusionRatio(0);
        scene.Quad(-10, 1, 0, 0, share: 0.6, opacity: 0.5, x: -0.15);
        scene.Quad(-12, 0, 1, 0, share: 0.6, opacity: 0.5, x: 0.15);
        scene.Quad(-11, 0, 0, 1, share: 0.3, x: 0.3);
        break;
      case Feature.Ssao:
        scene.Box();
        scene.Renderer.UseSSAOOn();
        scene.Renderer.SetSSAORadius(0.5);
        scene.Renderer.SetSSAOKernelSize(32);
        break;
      case Feature.EyeDomeLighting:
        scene.Box();
        scene.EyeDomeLighting();
        break;
      case Feature.VolumeBehindAPlane:
        scene.Volume();
        scene.Quad(-10, 0, 0, 1, share: 0.5, x: 0.5);
        break;
    }

    scene.Render();
    return scene.Pixels();
  }

  private static bool Differs(byte[] a, byte[] b, int pixel) =>
    Enumerable.Range(0, 3)
      .Any(c => Math.Abs(a[4 * pixel + c] - b[4 * pixel + c]) > ChannelTolerance);

  private static bool IsBlack(byte[] pixels, int pixel) =>
    pixels[4 * pixel] < 20 && pixels[4 * pixel + 1] < 20 && pixels[4 * pixel + 2] < 20;

  // a camera at the origin looking down -z (30°) in an offscreen window, on black
  private sealed class Scene : IDisposable
  {
    private readonly List<IDisposable> _objects = [];

    public Scene(bool reverseZ)
    {
      Window.SetOffScreenRendering(1);
      Window.SetUseReverseZ(reverseZ);
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

    // a quad at depth z (negative) over the middle share of the view, moved by x of its side, lit
    // flat
    public vtkActor Quad(double z, double red, double green, double blue, double share = 1,
                         double opacity = 1, double x = 0)
    {
      var half = -z * Math.Tan(15 * Math.PI / 180) * share;
      var shift = x * 2 * half;
      var plane = Keep(new vtkPlaneSource());
      plane.SetOrigin(shift - half, -half, z);
      plane.SetPoint1(shift + half, -half, z);
      plane.SetPoint2(shift - half, half, z);
      return Show(plane, red, green, blue, opacity, lit: false);
    }

    // points, each x, y, z and red, green, blue, as spheres of <size> pixels, or the line between
    // two of them as a tube of <size> pixels in the first one's colour
    public void Points(double[][] points, float size, bool tubes)
    {
      if (tubes)
      {
        var line = Keep(new vtkLineSource());
        line.SetPoint1(points[0][0], points[0][1], points[0][2]);
        line.SetPoint2(points[1][0], points[1][1], points[1][2]);
        var actor = Show(line, points[0][3], points[0][4], points[0][5], 1, lit: true);
        using var property = actor.GetProperty();
        property.RenderLinesAsTubesOn();
        property.SetLineWidth(size);
        return;
      }

      foreach (var point in points)
      {
        var source = Keep(new vtkPointSource());
        source.SetNumberOfPoints(1);
        source.SetRadius(0);
        source.SetCenter(point[0], point[1], point[2]);
        var actor = Show(source, point[3], point[4], point[5], 1, lit: true);
        using var property = actor.GetProperty();
        property.RenderPointsAsSpheresOn();
        property.SetPointSize(size);
      }
    }

    // white text over the scene's middle
    public void Text(string text)
    {
      var actor = Keep(new vtkTextActor());
      actor.SetInput(text);
      actor.SetDisplayPosition(10, Side / 2 - 8);
      using (var property = actor.GetTextProperty())
      {
        property.SetFontSize(18);
        property.SetColor(1, 1, 1);
      }

      Renderer.AddViewProp(actor);
    }

    // a lit box standing on a plate, seen from above and aside
    public void Box()
    {
      var plate = Keep(new vtkCubeSource());
      plate.SetBounds(-2, 2, -0.1, 0, -2, 2);
      Show(plate, 0.8, 0.8, 0.8, 1, lit: true);
      var box = Keep(new vtkCubeSource());
      box.SetBounds(-0.5, 0.5, 0, 1, -0.5, 0.5);
      Show(box, 0.9, 0.5, 0.2, 1, lit: true);
      Camera.SetPosition(3, 3, 4);
      Camera.SetFocalPoint(0, 0.3, 0);
      Camera.SetViewUp(0, 1, 0);
      Renderer.ResetCameraClippingRange();
    }

    // eye-dome lighting over the renderer's own steps
    public void EyeDomeLighting()
    {
      var steps = Keep(new vtkRenderStepsPass());
      var edl = Keep(new vtkEDLShading());
      edl.SetDelegatePass(steps);
      using var openGL = vtkOpenGLRenderer.SafeDownCast(Renderer);
      openGL.SetPass(edl);
    }

    // a red cube of density growing along x around depth -10, through the GPU ray cast mapper
    public void Volume()
    {
      const int size = 32;
      var image = Keep(new vtkImageData());
      image.SetDimensions(size, size, size);
      image.SetSpacing(2.0 / size, 2.0 / size, 2.0 / size);
      image.SetOrigin(-1, -1, -11);
      var values = Enumerable.Range(0, size * size * size).Select(i => (float)(i % size)).ToArray();
      var scalars = Keep(vtkFloatArray.FromArray(values));
      scalars.SetName("density");
      using (var pointData = image.GetPointData())
      {
        pointData.SetScalars(scalars);
      }

      var opacity = Keep(new vtkPiecewiseFunction());
      opacity.AddPoint(0, 0.2);
      opacity.AddPoint(size - 1, 1);
      var colour = Keep(new vtkColorTransferFunction());
      colour.AddRGBPoint(0, 1, 0, 0);
      colour.AddRGBPoint(size - 1, 1, 0, 0);
      var property = Keep(new vtkVolumeProperty());
      property.SetScalarOpacity(opacity);
      property.SetColor(colour);
      var mapper = Keep(vtkGPUVolumeRayCastMapper.New());
      mapper.SetInputData(image);
      var volume = Keep(new vtkVolume());
      volume.SetMapper(mapper);
      volume.SetProperty(property);
      Renderer.AddVolume(volume);
    }

    public void Render()
    {
      Renderer.ResetCameraClippingRange();
      Window.Render();
    }

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

    private T Keep<T>(T value) where T : IDisposable
    {
      _objects.Add(value);
      return value;
    }

    private vtkActor Show(vtkPolyDataAlgorithm source, double red, double green, double blue,
                          double opacity, bool lit)
    {
      var mapper = Keep(new vtkPolyDataMapper());
      using (var port = source.GetOutputPort())
      {
        mapper.SetInputConnection(port);
      }

      var actor = Keep(new vtkActor());
      actor.SetMapper(mapper);
      using (var property = actor.GetProperty())
      {
        property.SetColor(red, green, blue);
        property.SetOpacity(opacity);
        if (!lit)
        {
          property.LightingOff();
        }
      }

      Renderer.AddActor(actor);
      return actor;
    }
  }
}
