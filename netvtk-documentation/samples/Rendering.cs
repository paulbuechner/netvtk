// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Rendering and hosting: a picture off the screen (needs OpenGL, so the Rendering category), and a
/// view in an application's window, which needs a window and compiles without running.
/// </summary>
[TestFixture]
public class Rendering
{
  [Test]
  [Category("Rendering")]
  public void Offscreen_RendersIntoAnImage()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    using var mapper = new vtkPolyDataMapper();
    using var port = sphere.GetOutputPort();
    mapper.SetInputConnection(port);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);

    // Act
    #region offscreen
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(640, 480);
    window.AddRenderer(renderer);

    // the camera looks at everything, from a little above
    renderer.ResetCamera();
    using (var camera = renderer.GetActiveCamera())
    {
      camera.Elevation(20);
      camera.Zoom(1.2);
    }

    window.Render();

    // the picture as image data: 640 x 480 points, an RGB tuple each
    using var grab = new vtkWindowToImageFilter();
    grab.SetInput(window);
    grab.SetInputBufferTypeToRGB();
    grab.ReadFrontBufferOff();
    grab.Update();
    using var image = grab.GetOutput();
    int[] size = image.GetDimensions();
    #endregion

    // Assert
    var pixels = Pixels(window);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(size, Is.EqualTo(new[] { 640, 480, 1 }));
      Assert.That(pixels.Count(p => p.R + p.G + p.B > 0), Is.GreaterThan(640 * 480 / 10));
    }
  }

  [Test]
  [Category("Rendering")]
  public void Volume_ShowsTheImagesValues()
  {
    // Arrange (a 32 x 32 x 32 image whose value grows along x)
    using var image = new vtkImageData();
    image.SetDimensions(32, 32, 32);
    var values = Enumerable.Range(0, 32 * 32 * 32).Select(i => (float)(i % 32)).ToArray();
    using var density = vtkFloatArray.FromArray(values);
    using (var pointData = image.GetPointData())
    {
      pointData.SetScalars(density);
    }

    using var renderer = new vtkRenderer();
    using var window = new vtkRenderWindow();
    window.SetOffScreenRendering(1);
    window.SetSize(64, 64);
    window.AddRenderer(renderer);

    // Act
    #region volume
    // transfer functions: the value's opacity and colour, transparent black to opaque red
    using var opacity = new vtkPiecewiseFunction();
    opacity.AddPoint(0, 0);
    opacity.AddPoint(31, 1);
    using var color = new vtkColorTransferFunction();
    color.AddRGBPoint(0, 0, 0, 0);
    color.AddRGBPoint(31, 1, 0, 0);
    using var property = new vtkVolumeProperty();
    property.SetScalarOpacity(opacity);
    property.SetColor(color);

    // the mapper ray-casts on the GPU where it can, on the CPU otherwise
    using var mapper = new vtkSmartVolumeMapper();
    mapper.SetInputData(image);
    using var volume = new vtkVolume();
    volume.SetMapper(mapper);
    volume.SetProperty(property);
    renderer.AddVolume(volume);
    renderer.ResetCamera();
    window.Render();
    #endregion

    // Assert (on black: red where the values are high)
    var pixels = Pixels(window);
    Assert.That(pixels, Has.Some.Matches<(int R, int G, int B)>(p => p is { R: > 100, G: < 60 }));
  }

  [Test]
  public void InitializeOnUiThread_SetsTheFinalizersContext()
  {
    // Arrange (a context like a UI thread's)
    var previous = SynchronizationContext.Current;
    var ui = new SynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(ui);

    // Act
    try
    {
      HostedView.InitializeOnUiThread();

      // Assert
      using (Assert.EnterMultipleScope())
      {
        Assert.That(VtkFinalizer.Context, Is.SameAs(ui));
        Assert.That(VtkOutput.IsInstalled);
      }
    }
    finally
    {
      VtkFinalizer.Context = null;
      SynchronizationContext.SetSynchronizationContext(previous);
    }
  }

  // what the window shows, a pixel at a time
  internal static (int R, int G, int B)[] Pixels(vtkRenderWindow window)
  {
    using var grab = new vtkWindowToImageFilter();
    grab.SetInput(window);
    grab.SetInputBufferTypeToRGB();
    grab.ReadFrontBufferOff();
    grab.Update();
    using var image = grab.GetOutput();
    using var data = image.GetPointData();
    using var scalars = data.GetScalars();
    using var colors = vtkUnsignedCharArray.SafeDownCast(scalars);
    var rgb = colors.ToArray();
    return
    [
      .. Enumerable.Range(0, rgb.Length / 3)
        .Select(i => ((int)rgb[3 * i], (int)rgb[3 * i + 1], (int)rgb[3 * i + 2]))
    ];
  }

  #region host
  // VTK drawing into a child window the application made (an HWND on Windows), its mouse input fed
  // from the window's messages; create, use and dispose it on the UI thread
  public sealed class HostedView : IDisposable
  {
    private readonly vtkRenderWindow _window = new();
    private readonly vtkGenericRenderWindowInteractor _interactor = new();
    private readonly vtkInteractorStyleTrackballCamera _style = new();

    public HostedView(IntPtr handle)
    {
      _window.SetWindowId(handle);
      _window.AddRenderer(Renderer);
      _interactor.SetRenderWindow(_window);
      _interactor.SetInteractorStyle(_style);
      _interactor.Initialize();
    }

    public vtkRenderer Renderer { get; } = new();

    // once, on the UI thread: VTK's messages to C#, the finalizer's releases to this thread
    public static void InitializeOnUiThread()
    {
      VtkOutput.Install();
      VtkFinalizer.Context = SynchronizationContext.Current;
    }

    // the window's size and paint messages
    public void Resize(int width, int height)
    {
      _window.SetSize(width, height);
      _interactor.UpdateSize(width, height);
    }

    public void Paint() => _window.Render();

    // its mouse messages, in the window's pixels (y down): the style rotates, pans and zooms
    public void LeftButtonDown(int x, int y)
    {
      _interactor.SetEventInformationFlipY(x, y);
      _interactor.LeftButtonPressEvent();
    }

    public void MouseMove(int x, int y)
    {
      _interactor.SetEventInformationFlipY(x, y);
      _interactor.MouseMoveEvent();
    }

    public void LeftButtonUp(int x, int y)
    {
      _interactor.SetEventInformationFlipY(x, y);
      _interactor.LeftButtonReleaseEvent();
    }

    // before the window goes: the render window last, its OpenGL context with it
    public void Dispose()
    {
      _interactor.Dispose();
      _style.Dispose();
      Renderer.Dispose();
      _window.Dispose();
    }
  }
  #endregion
}
