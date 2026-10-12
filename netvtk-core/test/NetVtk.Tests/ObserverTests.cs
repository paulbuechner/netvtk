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
/// C# subclasses of vtkCommand as observers (Directors.i): VTK calls them back, keeps them alive
/// while it holds them, and an exception one throws comes out of the call that led to it. C#
/// handlers and events on top (vtkObject.cs).
/// </summary>
[TestFixture]
public class ObserverTests
{
  [Test]
  public void Observer_IsCalledBack()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    using var observer = new Recorder();
    subject.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, observer);

    // Act
    subject.Modified();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(observer.Events, Is.EqualTo(new[] { (ulong)vtkCommand_EventIds.ModifiedEvent }));
      Assert.That(observer.Callers, Is.EqualTo(new[] { "vtkDoubleArray" }));
    }
  }

  [Test]
  public void Observer_VtkHolds_OutlivesItsLastCSharpReference()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    var calls = new List<ulong>();
    Observe(subject, calls);

    // Act
    Garbage.Collect();
    subject.Modified();

    // Assert
    Assert.That(calls, Has.Count.EqualTo(1));
  }

  [Test]
  public void Observer_Throwing_ThrowsFromTheCallThatLedToIt()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    using var observer = new Thrower();
    subject.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, observer);

    // Act
    Action modify = subject.Modified;

    // Assert
    Assert.That(
      modify,
      Throws.TypeOf<VtkException>()
        .With.InnerException.TypeOf<InvalidOperationException>()
        .And.InnerException.Message.EqualTo("observer failed"));
  }

  [Test]
  public void Observer_Throwing_LeavesTheNextCallAlone()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    using var observer = new Thrower();
    var tag = subject.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, observer);
    try
    {
      subject.Modified();
    }
    catch (VtkException)
    {
    }

    subject.RemoveObserver(tag);

    // Act
    Action modify = subject.Modified;

    // Assert
    Assert.That(modify, Throws.Nothing);
  }

  [Test]
  public void Handler_IsCalledBack()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    var calls = new List<ulong>();
    subject.AddObserver(vtkCommand_EventIds.ModifiedEvent, (_, e) => calls.Add(e.EventId));

    // Act
    subject.Modified();

    // Assert
    Assert.That(calls, Is.EqualTo(new[] { (ulong)vtkCommand_EventIds.ModifiedEvent }));
  }

  [Test]
  public void Handler_Observed_StopsWhenDisposed()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    var calls = 0;
    var observation = subject.Observe(vtkCommand_EventIds.ModifiedEvent, (_, _) => calls++);
    subject.Modified();

    // Act
    observation.Dispose();
    subject.Modified();

    // Assert
    Assert.That(calls, Is.EqualTo(1));
  }

  [Test]
  public void Event_HandlerRemoved_IsNoLongerCalled()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    var calls = 0;
    EventHandler<VtkEventArgs> handler = (_, _) => calls++;
    subject.ModifiedEvent += handler;
    subject.Modified();

    // Act
    subject.ModifiedEvent -= handler;
    subject.Modified();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(calls, Is.EqualTo(1));
      Assert.That(subject.HasObserver((ulong)vtkCommand_EventIds.ModifiedEvent), Is.Zero);
    }
  }

  [Test]
  public void ProgressEvent_ReportsTheProgress()
  {
    // Arrange
    using var source = new vtkSphereSource();
    var progress = new List<double>();
    source.ProgressEvent += (_, e) => progress.Add(e.GetDouble());

    // Act
    source.Update();

    // Assert
    Assert.That(progress, Is.Not.Empty.And.All.InRange(0.0, 1.0));
  }

  [Test]
  public void Handler_Throwing_ThrowsFromTheCallThatLedToIt()
  {
    // Arrange
    using var subject = new vtkDoubleArray();
    subject.ModifiedEvent += (_, _) => throw new InvalidOperationException("handler failed");

    // Act
    Action modify = subject.Modified;

    // Assert
    Assert.That(modify,
                Throws.TypeOf<VtkException>()
                  .With.InnerException.Message.EqualTo("handler failed"));
  }

  // an observer only VTK holds
  private static void Observe(vtkObject subject, List<ulong> calls) =>
    subject.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, new Counter(calls));

  private sealed class Recorder : vtkCommand
  {
    public List<ulong> Events { get; } = [];

    public List<string> Callers { get; } = [];

    public override void Execute(vtkObject caller, ulong eventId, IntPtr callData)
    {
      Events.Add(eventId);
      Callers.Add(caller.GetClassName());
      caller.Dispose();
    }
  }

  private sealed class Counter(List<ulong> calls) : vtkCommand
  {
    public override void Execute(vtkObject caller, ulong eventId, IntPtr callData)
    {
      calls.Add(eventId);
      caller.Dispose();
    }
  }

  private sealed class Thrower : vtkCommand
  {
    public override void Execute(vtkObject caller, ulong eventId, IntPtr callData)
    {
      caller.Dispose();
      throw new InvalidOperationException("observer failed");
    }
  }
}
