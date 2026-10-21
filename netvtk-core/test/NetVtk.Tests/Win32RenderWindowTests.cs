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
/// Windows' render windows (RenderingOpenGL2Win32, in NetVtkWin32, which only the Windows runtime
/// packages hold): the platform's OpenGL window, and the one that renders into a texture Direct3D
/// 11 shares. The device comes from the caller, as an application's own: the one VTK makes is a
/// debug device, which needs the Graphics Tools.
/// </summary>
[TestFixture]
public class Win32RenderWindowTests
{
  // the RGBA channel of a cone's colour
  private const int Red = 0;
  private const int Green = 1;
  // VTK_UNSIGNED_CHAR (vtkType.h)
  private const int UnsignedChar = 3;

  [Test]
  [Platform("Win")]
  public void DXRenderWindow_IsAWin32OpenGLRenderWindow()
  {
    // Arrange
    using var window = new vtkWin32OpenGLDXRenderWindow();

    // Act
    using var win32 = vtkWin32OpenGLRenderWindow.SafeDownCast(window);
    using var openGL = vtkOpenGLRenderWindow.SafeDownCast(window);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(win32?.NativePointer, Is.EqualTo(window.NativePointer));
      Assert.That(openGL?.NativePointer, Is.EqualTo(window.NativePointer));
    }
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void RenderWindow_IsTheWin32Window()
  {
    // Arrange (the object factory's, which another library made: the Win32 window where one
    // initializes, OpenGL 3.2, else the OSMesa one)
    using var window = new vtkRenderWindow();

    // Act
    using var win32 =
      vtkObjectBase.FromNativePointer<vtkWin32OpenGLRenderWindow>(window.NativePointer);

    // Assert
    Assert.That(win32.GetClassName(), Is.EqualTo("vtkWin32OpenGLRenderWindow"));
  }

  [Test]
  [Platform(Exclude = "Win")]
  public void DXRenderWindow_ElsewhereThrows()
  {
    // Act (NetVtkWin32 isn't in this platform's runtime package)
    Action create = () => new vtkWin32OpenGLDXRenderWindow().Dispose();

    // Assert
    Assert.That(create,
                Throws.InstanceOf<TypeInitializationException>()
                  .With.Matches<Exception>(e => e.GetBaseException() is DllNotFoundException));
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void Initialize_SharesTexturesOnTheCallersDevice()
  {
    AssumeInterop();

    // Arrange
    using var device = Direct3DDevice.Create();
    using var window = new vtkWin32OpenGLDXRenderWindow();
    window.SetShowWindow(false);
    window.SetD3DDeviceContext(device.Context);

    // Act
    window.Initialize();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(window.GetDevice(), Is.EqualTo(device.Device));
      Assert.That(window.GetD3DSharedTexture(), Is.Not.EqualTo(IntPtr.Zero));
      Assert.That(window.GetD3DSharedDepthTexture(), Is.Not.EqualTo(IntPtr.Zero));
    }
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void Render_Locked_DrawsTheConeIntoTheSharedTexture()
  {
    AssumeInterop();

    // Arrange (a red cone on black, the render framebuffer shared; one sample per pixel, as the
    // shared textures have)
    using var cone = new vtkConeSource();
    using var mapper = new vtkPolyDataMapper();
    using var port = cone.GetOutputPort();
    mapper.SetInputConnection(port);
    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using (var property = actor.GetProperty())
    {
      property.SetColor(1, 0, 0);
    }

    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    using var device = Direct3DDevice.Create();
    using var window = new vtkWin32OpenGLDXRenderWindow();
    var errors = new List<string>();
    window.ErrorEvent += (sender, e) =>
    {
      errors.Add(e.GetText());
      // the window, a proxy of its own: released here, not by the finalizer on its thread
      (sender as IDisposable)?.Dispose();
    };
    window.SetShowWindow(false);
    window.SetMultiSamples(0);
    window.SetSize(64, 64);
    window.AddRenderer(renderer);
    window.SetD3DDeviceContext(device.Context);
    window.Initialize();
    window.Render();
    window.RegisterSharedRenderFramebuffer();
    renderer.ResetCamera();
    using var pixels = new vtkUnsignedCharArray();

    // Act (OpenGL draws into the shared texture while it's locked; the render framebuffer is read
    // there too)
    window.Lock();
    window.Render();
    window.GetRGBACharPixelData(0, 0, 63, 63, 0, pixels);
    window.Unlock();

    // Assert
    var rgba = pixels.ToArray();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(errors, Is.Empty);
      Assert.That(
        Enumerable.Range(0, rgba.Length / 4).Count(p => rgba[4 * p] > 100 && rgba[4 * p + 1] < 60),
        Is.Positive);
    }
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void Render_AfterCaptureBuffersWithSamples_ReachesTheSharedTexture()
  {
    AssumeInterop();

    // Arrange (a red cone, four samples per pixel in the window and in its shared textures, as an
    // application renders it: offscreen, its render framebuffer shared; a frame, the hardware
    // selector's buffers captured over the view, then the cone green)
    const int width = 200, height = 150;
    using var cone = new vtkConeSource();
    using var mapper = new vtkPolyDataMapper();
    using (var port = cone.GetOutputPort())
    {
      mapper.SetInputConnection(port);
    }

    using var actor = new vtkActor();
    actor.SetMapper(mapper);
    using var property = actor.GetProperty();
    property.SetColor(1, 0, 0);
    using var renderer = new vtkRenderer();
    renderer.AddActor(actor);
    using var device = Direct3DDevice.Create();
    using var window = new vtkWin32OpenGLDXRenderWindow();
    var errors = new List<string>();
    window.ErrorEvent += (sender, e) =>
    {
      errors.Add(e.GetText());
      // the window, a proxy of its own: released here, not by the finalizer on its thread
      (sender as IDisposable)?.Dispose();
    };
    window.SetShowWindow(false);
    window.UseOffScreenBuffersOn();
    window.SetMultiSamples(4);
    window.SetSharedTextureSamples(4);
    window.SetSize(width, height);
    window.AddRenderer(renderer);
    window.SetD3DDeviceContext(device.Context);
    window.Initialize();
    window.Render();
    window.RegisterSharedRenderFramebuffer();
    renderer.ResetCamera();
    var texture = device.CreateTexture(width, height);
    using var selector = vtkHardwareSelector.New();
    selector.SetRenderer(renderer);
    selector.SetFieldAssociation((int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_CELLS);
    selector.SetArea(0, 0, width - 1, height - 1);
    Frame(window, texture);
    window.Lock();
    var captured = selector.CaptureBuffers();
    window.Unlock();
    property.SetColor(0, 1, 0);

    // Act
    Frame(window, texture);

    // Assert
    var rgba = device.ReadPixels(texture, width, height);
    Direct3DDevice.Release(texture);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(captured, Is.True);
      Assert.That(errors, Is.Empty);
      Assert.That(Pixels(rgba, Green), Is.Positive);
      Assert.That(Pixels(rgba, Red), Is.Zero);
    }
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void SetSize_AnotherWindowCurrent_BothWindowsKeepTheirCones()
  {
    AssumeInterop();

    // Arrange (two windows in one thread, each on a device of its own; the second has a texture of
    // its own more than the first, so its shared texture's name is another texture in the first's
    // context, which the first's render leaves current. The first is wider than the narrowest
    // window Windows makes, 120 pixels here: a narrower one is widened after VTK made its
    // framebuffers, and its renders differ from one to the next.)
    using var first = new ConeWindow(Red, 2, 400, 300);
    using var second = new ConeWindow(Green, 3, 1, 1);
    var firstBefore = first.RenderCone();

    // Act (the second resized, then each rendered)
    second.Window.SetSize(520, 400);
    var secondCone = second.RenderCone();
    var firstCone = first.RenderCone();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first.Errors.Concat(second.Errors), Is.Empty);
      Assert.That(firstCone, Is.EqualTo(firstBefore));
      Assert.That(secondCone, Is.Positive);
    }
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void Dispose_FramebufferShared_ReleasesTheCallersDevice()
  {
    AssumeInterop();

    // Arrange (the window rendered once, its render framebuffer shared)
    using var device = Direct3DDevice.Create();
    var references = device.References;
    var window = new vtkWin32OpenGLDXRenderWindow();
    window.SetShowWindow(false);
    window.SetMultiSamples(0);
    window.SetSize(64, 64);
    window.SetD3DDeviceContext(device.Context);
    window.Initialize();
    window.Render();
    window.RegisterSharedRenderFramebuffer();

    // Act
    window.Dispose();

    // Assert (the device has the references it had before the window took its own)
    Assert.That(device.References, Is.EqualTo(references));
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void SetAdapterId_NoSuchAdapter_VtkFindsNone()
  {
    AssumeInterop();

    // Arrange (no adapter has this id; VTK's own device needs the Graphics Tools, whose error isn't
    // this test's)
    using var window = new vtkWin32OpenGLDXRenderWindow();
    var warnings = new List<string>();
    window.WarningEvent += (sender, e) =>
    {
      warnings.Add(e.GetText());
      // the window, a proxy of its own: released here, not by the finalizer on its thread
      (sender as IDisposable)?.Dispose();
    };
    window.ErrorEvent += (sender, _) => (sender as IDisposable)?.Dispose();
    window.SetShowWindow(false);
    window.SetAdapterId(-1);

    // Act
    window.Initialize();

    // Assert
    Assert.That(warnings, Has.Some.Contains("No DXGI adapter found"));
  }

  [Test]
  [Platform("Win")]
  [Category("Rendering")]
  public void RegisterSharedRenderFramebuffer_NoInteropDevice_ReportsAnError()
  {
    // Arrange (no adapter has this id: the window opens no interop device, as on a driver without
    // WGL_NV_DX_interop)
    using var window = new vtkWin32OpenGLDXRenderWindow();
    var errors = new List<string>();
    window.ErrorEvent += (sender, e) =>
    {
      errors.Add(e.GetText());
      // the window, a proxy of its own: released here, not by the finalizer on its thread
      (sender as IDisposable)?.Dispose();
    };
    window.WarningEvent += (sender, _) => (sender as IDisposable)?.Dispose();
    window.SetShowWindow(false);
    window.SetAdapterId(-1);
    window.SetSize(64, 64);
    window.Initialize();
    window.Render();

    // Act
    window.RegisterSharedRenderFramebuffer();

    // Assert
    Assert.That(errors, Has.Some.Contains("No interop device"));
  }

  // the tests that share textures need a driver that does (Direct3DDevice.Interop)
  private static void AssumeInterop() =>
    Assume.That(Direct3DDevice.Interop, "the OpenGL driver lacks WGL_NV_DX_interop");

  // a frame as an application renders one: OpenGL draws into the shared texture while it's locked,
  // and the application blits that into a texture of its own
  private static void Frame(vtkWin32OpenGLDXRenderWindow window, IntPtr texture)
  {
    window.Lock();
    window.Render();
    window.Unlock();
    window.BlitToTexture(texture);
  }

  // the pixels bright in one channel and dark in the other
  private static int Pixels(byte[] rgba, int channel) => Enumerable.Range(0, rgba.Length / 4)
    .Count(p => rgba[4 * p + channel] > 100 && rgba[4 * p + 1 - channel] < 60);

  // a red or green cone on black in a DX window on a Direct3D device of its own, one sample per
  // pixel, rendered once and its render framebuffer shared; its own textures come first in its
  // context, its framebuffers' after them
  private sealed class ConeWindow : IDisposable
  {
    private readonly int _channel;
    private readonly vtkConeSource _cone = new();
    private readonly vtkPolyDataMapper _mapper = new();
    private readonly vtkActor _actor = new();
    private readonly vtkRenderer _renderer = new();
    private readonly Direct3DDevice _device = Direct3DDevice.Create();
    private readonly List<vtkTextureObject> _textures = [];

    public ConeWindow(int channel, int textures, int width, int height)
    {
      _channel = channel;
      using (var port = _cone.GetOutputPort())
      {
        _mapper.SetInputConnection(port);
      }

      _actor.SetMapper(_mapper);
      using (var property = _actor.GetProperty())
      {
        property.SetColor(channel == Red ? 1 : 0, channel == Green ? 1 : 0, 0);
      }

      _renderer.AddActor(_actor);
      Window.ErrorEvent += (sender, e) =>
      {
        Errors.Add(e.GetText());
        // the window, a proxy of its own: released here, not by the finalizer on its thread
        (sender as IDisposable)?.Dispose();
      };
      Window.SetShowWindow(false);
      Window.SetMultiSamples(0);
      Window.SetSize(width, height);
      Window.AddRenderer(_renderer);
      Window.SetD3DDeviceContext(_device.Context);
      Window.Initialize();
      for (var i = 0; i < textures; i++)
      {
        var texture = new vtkTextureObject();
        texture.SetContext(Window);
        texture.Allocate2D(1, 1, 4, UnsignedChar);
        _textures.Add(texture);
      }

      Window.Render();
      Window.RegisterSharedRenderFramebuffer();
      _renderer.ResetCamera();
    }

    public vtkWin32OpenGLDXRenderWindow Window { get; } = new();

    public List<string> Errors { get; } = [];

    // a render with the shared texture locked: the pixels that show the cone, bright in its
    // colour's channel and dark in the other's
    public int RenderCone()
    {
      var size = Window.GetSize();
      using var pixels = new vtkUnsignedCharArray();
      Window.Lock();
      Window.Render();
      Window.GetRGBACharPixelData(0, 0, size[0] - 1, size[1] - 1, 0, pixels);
      Window.Unlock();
      var rgba = pixels.ToArray();
      return Enumerable.Range(0, rgba.Length / 4)
        .Count(p => rgba[4 * p + _channel] > 100 && rgba[4 * p + 1 - _channel] < 60);
    }

    public void Dispose()
    {
      foreach (var texture in _textures)
      {
        texture.Dispose();
      }

      Window.Dispose();
      _device.Dispose();
      _renderer.Dispose();
      _actor.Dispose();
      _mapper.Dispose();
      _cone.Dispose();
    }
  }
}
