// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// Volume rendering (the port's volume feature): an image's scalars through a transfer function,
/// drawn by the mapper the GPU supports.
/// </summary>
[TestFixture]
public class VolumeTests
{
  [Test]
  public void GpuVolumeRayCastMapper_IsOpenGLs()
  {
    // Act (abstract: its New() asks the factories, which RenderingVolume's companion registers)
    using var mapper = vtkGPUVolumeRayCastMapper.New();

    // Assert
    Assert.That(mapper.GetClassName(), Is.EqualTo("vtkOpenGLGPUVolumeRayCastMapper"));
  }

  [Test]
  [Category("Rendering")]
  public void SmartVolumeMapper_Offscreen_DrawsTheVolume()
  {
    // Arrange (a 32 x 32 x 32 image whose value grows along x, red and opaque where it's high)
    const int size = 32;
    using var image = new vtkImageData();
    image.SetDimensions(size, size, size);
    var values = Enumerable.Range(0, size * size * size).Select(i => (float)(i % size)).ToArray();
    using var scalars = vtkFloatArray.FromArray(values);
    scalars.SetName("density");
    using (var pointData = image.GetPointData())
    {
      pointData.SetScalars(scalars);
    }

    using var opacity = new vtkPiecewiseFunction();
    opacity.AddPoint(0, 0);
    opacity.AddPoint(size - 1, 1);
    using var color = new vtkColorTransferFunction();
    color.AddRGBPoint(0, 0, 0, 0);
    color.AddRGBPoint(size - 1, 1, 0, 0);
    using var property = new vtkVolumeProperty();
    property.SetScalarOpacity(opacity);
    property.SetColor(color);
    using var mapper = new vtkSmartVolumeMapper();
    mapper.SetInputData(image);
    using var volume = new vtkVolume();
    volume.SetMapper(mapper);
    volume.SetProperty(property);
    using var renderer = new vtkRenderer();
    renderer.AddVolume(volume);
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(64, 64);
    window.AddRenderer(renderer);
    renderer.ResetCamera();

    // Act
    window.Render();

    // Assert (on black)
    using var grab = new vtkWindowToImageFilter();
    grab.SetInput(window);
    grab.SetInputBufferTypeToRGB();
    grab.ReadFrontBufferOff();
    grab.Update();
    using var shot = grab.GetOutput();
    using var shotData = shot.GetPointData();
    using var shotScalars = shotData.GetScalars();
    using var rgb = vtkUnsignedCharArray.SafeDownCast(shotScalars);
    var pixels = rgb.ToArray();
    Assert.That(
      Enumerable.Range(0, pixels.Length / 3)
        .Count(p => pixels[3 * p] > 100 && pixels[3 * p + 1] < 60), Is.Positive);
  }
}
