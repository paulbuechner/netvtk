// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
#region usings
using Vtk;
#endregion

namespace NetVtk.Samples;

/// <summary>
/// Getting started: a source's output (the package README's example), then a scene rendered off the
/// screen.
/// </summary>
[TestFixture]
public class GettingStarted
{
  [Test]
  public void Cone_HasItsBounds()
  {
    // Act
    #region cone
    using var cone = new vtkConeSource();
    cone.SetResolution(32);
    cone.Update();
    using var output = cone.GetOutput();
    double[] bounds = output.GetBounds();
    #endregion

    // Assert (the default cone: height 1 along x, centred, radius 0.5)
    Assert.That(bounds, Is.EqualTo(new[] { -0.5, 0.5, -0.5, 0.5, -0.5, 0.5 }).Within(1e-6));
  }

  [Test]
  [Category("Rendering")]
  public void Picture_ShowsTheCone()
  {
    // Act
    #region picture
    // source -> mapper -> actor -> renderer -> render window
    using var cone = new vtkConeSource();
    using var mapper = new vtkPolyDataMapper();
    using var port = cone.GetOutputPort();
    mapper.SetInputConnection(port);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    renderer.SetBackground(0.1, 0.2, 0.4);

    // off the screen: no window, the picture stays in the render window's buffer
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(300, 300);
    window.AddRenderer(renderer);
    window.Render();

    // its pixels: three bytes (RGB) each, row by row
    using var grab = new vtkWindowToImageFilter();
    grab.SetInput(window);
    grab.Update();
    using var image = grab.GetOutput();
    using var pixelData = image.GetPointData();
    using var scalars = pixelData.GetScalars();
    using var colors = vtkUnsignedCharArray.SafeDownCast(scalars);
    byte[] pixels = colors.ToArray();
    #endregion

    // Assert (the cone's pixels: white, the background's blue (25, 51, 102) everywhere else)
    var rgb = Enumerable.Range(0, pixels.Length / 3).Select(i => pixels[3 * i] + pixels[3 * i + 2]);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(pixels, Has.Length.EqualTo(300 * 300 * 3));
      Assert.That(rgb.Count(sum => sum > 25 + 102 + 30), Is.GreaterThan(300 * 300 / 20));
    }
  }
}
