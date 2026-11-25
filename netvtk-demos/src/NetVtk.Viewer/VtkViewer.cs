// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

//
using Vtk;

namespace NetVtk.Viewer;

/// <summary>
/// A VTK render window drawing into a native window, with a renderer and a trackball camera.
/// <see cref="ViewerWindow"/>'s messages drive it (<see cref="X11ViewerWindow"/>'s events,
/// <see cref="CocoaViewerView"/>'s overrides): VTK's own window procedure stays out, so input
/// reaches the interactor from C#, where an exception a C# observer throws comes back.
/// </summary>
public sealed class VtkViewer : IDisposable
{
  private readonly vtkRenderWindow _window = new();
  private readonly vtkGenericRenderWindowInteractor _interactor = new();
  private readonly vtkInteractorStyleTrackballCamera _style = new();
  private ResultScene? _scene;

  public VtkViewer()
  {
    Renderer.SetBackground(0.1, 0.12, 0.16);
  }

  public vtkRenderer Renderer { get; } = new();

  /// <summary>What the viewer shows, or null.</summary>
  public ResultScene? Scene => _scene;

  /// <summary>
  /// Once, on the UI thread, before the first viewer: VTK's messages go to C# (Trace) instead of a
  /// window of VTK's own, and the finalizer releases VTK objects on this thread, where the render
  /// windows' OpenGL contexts are.
  /// </summary>
  public static void InitializeOnUiThread()
  {
    VtkOutput.Install();
    VtkFinalizer.Context = SynchronizationContext.Current;
  }

  /// <summary>
  /// Shows <paramref name="document"/> in place of what showed, with <paramref name="options"/> or
  /// its defaults; the camera fits it.
  /// </summary>
  public ResultScene Show(ResultDocument document, SceneOptions? options = null)
  {
    _scene?.Dispose();
    _scene = null;
    var scene = new ResultScene(document, Renderer);
    scene.Apply(options ?? scene.DefaultOptions());
    _scene = scene;
    Renderer.ResetCamera();
    Redraw();
    return scene;
  }

  /// <summary>Shows the scene with <paramref name="options"/>.</summary>
  public void Apply(SceneOptions options)
  {
    _scene?.Apply(options);
    Redraw();
  }

  /// <summary>
  /// Draws into <paramref name="handle"/> (an HWND, or an NSView on macOS) from now on.
  /// </summary>
  public void Attach(IntPtr handle)
  {
    _window.SetWindowId(handle);
    _window.AddRenderer(Renderer);
    _interactor.SetRenderWindow(_window);
    _interactor.SetInteractorStyle(_style);
    _interactor.Initialize();
  }

  /// <summary>
  /// Draws into a child window VTK creates in <paramref name="parent"/>, an X11 window of
  /// <paramref name="display"/>: VTK gives it the visual its OpenGL context needs. Returns the
  /// child's window id.
  /// </summary>
  public IntPtr AttachChild(IntPtr display, IntPtr parent)
  {
    _window.SetDisplayId(display);
    _window.SetParentId(parent);
    _window.AddRenderer(Renderer);
    _window.Initialize();
    _interactor.SetRenderWindow(_window);
    _interactor.SetInteractorStyle(_style);
    _interactor.Initialize();
    return _window.GetGenericWindowId();
  }

  /// <summary>The render window, for the tests.</summary>
  internal vtkRenderWindow RenderWindow => _window;

  public void Resize(int width, int height)
  {
    _window.SetSize(width, height);
    _interactor.UpdateSize(width, height);
  }

  public void Redraw() => _window.Render();

  /// <summary>
  /// Text keeps its size on a screen of <paramref name="pixels"/> pixels a point (2 on a Retina
  /// screen): VTK's DPI, 72 at one pixel a point.
  /// </summary>
  public void SetPixelsPerPoint(double pixels) => _window.SetDPI((int)Math.Round(72 * pixels));

  public void MouseMove(int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    _interactor.MouseMoveEvent();
  }

  public void ButtonDown(ViewerButton button, int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    switch (button)
    {
      case ViewerButton.Left:
        _interactor.LeftButtonPressEvent();
        break;
      case ViewerButton.Middle:
        _interactor.MiddleButtonPressEvent();
        break;
      case ViewerButton.Right:
        _interactor.RightButtonPressEvent();
        break;
    }
  }

  public void ButtonUp(ViewerButton button, int x, int y, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    switch (button)
    {
      case ViewerButton.Left:
        _interactor.LeftButtonReleaseEvent();
        break;
      case ViewerButton.Middle:
        _interactor.MiddleButtonReleaseEvent();
        break;
      case ViewerButton.Right:
        _interactor.RightButtonReleaseEvent();
        break;
    }
  }

  public void MouseWheel(int x, int y, double notches, ViewerModifiers modifiers)
  {
    Position(x, y, modifiers);
    if (notches > 0)
    {
      _interactor.MouseWheelForwardEvent();
    }
    else if (notches < 0)
    {
      _interactor.MouseWheelBackwardEvent();
    }
  }

  // the window's pixels: y down, as VTK's FlipY takes them
  private void Position(int x, int y, ViewerModifiers modifiers) =>
    _interactor.SetEventInformationFlipY(x, y, (modifiers & ViewerModifiers.Control) != 0 ? 1 : 0,
                                         (modifiers & ViewerModifiers.Shift) != 0 ? 1 : 0);

  // the scene first, the render window last: its OpenGL context goes with it, on this thread
  public void Dispose()
  {
    _scene?.Dispose();
    _scene = null;
    _interactor.Dispose();
    _style.Dispose();
    Renderer.Dispose();
    _window.Dispose();
  }
}

public enum ViewerButton
{
  Left,
  Middle,
  Right,
}

[Flags]
public enum ViewerModifiers
{
  None = 0,
  Shift = 1,
  Control = 2,
}
