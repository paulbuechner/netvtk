// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// A render window's GPU times (its render timer, on OpenGL's timer queries): a frame's events, a
/// value class's vector of value classes, are an array of copies, each with its name as a string
/// and the events nested in it.
/// </summary>
[TestFixture]
public class RenderTimerLogTests
{
  // the event vtkRenderWindow::Render logs around a render, the frame's outermost
  private const string WindowRender = "vtkRenderWindow::Render";

  [Test]
  [Category("Rendering")]
  public void PopFirstReadyFrame_AfterRenders_HasTheRendersTimedEvent()
  {
    // Arrange
    using var scene = new TimedScene();

    // Act
    using var frame = scene.Timer.PopFirstReadyFrame();

    // Assert
    var events = frame.Events;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(events.Select(e => e.Name), Does.Contain(WindowRender));
      Assert.That(events.Select(e => e.ElapsedTimeMilliseconds()),
                  Has.All.GreaterThanOrEqualTo(0f));
    }
  }

  [Test]
  [Category("Rendering")]
  public void Events_OfTheWindowsRender_HoldTheRenderersRender()
  {
    // Arrange (the renderer renders inside the window's render)
    using var scene = new TimedScene();
    using var frame = scene.Timer.PopFirstReadyFrame();
    using var render = frame.Events.Single(e => e.Name == WindowRender);

    // Act
    var nested = render.Events;

    // Assert
    Assert.That(nested.Select(e => e.Name), Has.Some.StartWith("vtkRenderer::Render"));
  }

  // a sphere in an offscreen window, rendered with the timer logging until its first frame's timer
  // queries are ready, which the GPU answers a frame or more after the render
  private sealed class TimedScene : IDisposable
  {
    private const int MaxRenders = 100;

    private readonly vtkSphereSource _sphere = new();
    private readonly vtkPolyDataMapper _mapper = new();
    private readonly vtkActor _actor = new();
    private readonly vtkRenderer _renderer = new();
    private readonly vtkRenderWindow _window = new();

    public TimedScene()
    {
      using (var port = _sphere.GetOutputPort())
      {
        _mapper.SetInputConnection(port);
      }

      _actor.SetMapper(_mapper);
      _renderer.AddActor(_actor);
      _window.SetOffScreenRendering(1);
      _window.SetSize(64, 64);
      _window.AddRenderer(_renderer);
      Timer = _window.GetRenderTimer();
      Timer.SetLoggingEnabled(true);
      for (var renders = 0; renders < MaxRenders && !Timer.FrameReady(); renders++)
      {
        _window.Render();
      }

      Assert.That(Timer.FrameReady(), Is.True, "a frame is ready");
    }

    public vtkRenderTimerLog Timer { get; }

    public void Dispose()
    {
      Timer.Dispose();
      _window.Dispose();
      _renderer.Dispose();
      _actor.Dispose();
      _mapper.Dispose();
      _sphere.Dispose();
    }
  }
}
