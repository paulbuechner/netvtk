// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Observers and messages: C# handlers and events on VTK's events, vtkCommand subclasses, VTK's
/// messages through VtkOutput, and exceptions thrown in callbacks.
/// </summary>
[TestFixture]
public class Observers
{
  private const string Missing = "netvtk-samples-missing.vtu";

  [Test]
  public void Handler_IsCalledUntilRemoved()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    var calls = new List<string>();

    // Act
    #region handler
    // a handler for one of VTK's events; the tag removes it
    ulong tag = array.AddObserver(vtkCommand_EventIds.ModifiedEvent,
                                  (sender, e) => calls.Add(((vtkObject)sender!).GetClassName()));
    array.Modified(); // calls it: "vtkDoubleArray"
    array.RemoveObserver(tag);
    array.Modified(); // doesn't
    #endregion

    // Assert
    Assert.That(calls, Is.EqualTo(new[] { "vtkDoubleArray" }));
  }

  [Test]
  public void Observe_EndsWhenDisposed()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    var runs = 0;

    // Act
    #region observe
    // observed until disposed
    using (sphere.Observe(vtkCommand_EventIds.EndEvent, (_, _) => runs++))
    {
      sphere.Update(); // runs: 1
    }

    sphere.SetRadius(2);
    sphere.Update(); // runs again, unobserved
    #endregion

    // Assert
    Assert.That(runs, Is.EqualTo(1));
  }

  [Test]
  public void Events_ReportAnAlgorithmsRun()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.SetThetaResolution(256);
    sphere.SetPhiResolution(256);
    var seen = new List<string>();

    // Act
    #region events
    // C# events for the common ones: StartEvent, ProgressEvent (0 to 1), EndEvent on algorithms;
    // ModifiedEvent, ErrorEvent, WarningEvent on every vtkObject
    sphere.StartEvent += (_, _) => seen.Add("start");
    sphere.ProgressEvent += (_, e) => seen.Add($"{e.GetDouble():P0}");
    sphere.EndEvent += (_, _) => seen.Add("end");
    sphere.Update();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(seen[0], Is.EqualTo("start"));
      Assert.That(seen[^1], Is.EqualTo("end"));
      Assert.That(seen, Has.Count.GreaterThan(2));
    }
  }

  [Test]
  public void Command_IsCalledWithItsCaller()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    array.SetName("pressure");

    // Act
    #region command-use
    using var command = new NameRecorder();
    array.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, command);
    array.Modified();
    #endregion

    // Assert
    Assert.That(command.Names, Is.EqualTo(new[] { "pressure" }));
  }

  [Test]
  public void Command_VtkHolds_OutlivesItsProxy()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    var names = new List<string>();
    Observe(array, names);

    // Act
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    array.SetName("kept");
    array.Modified();

    // Assert
    Assert.That(names, Does.Contain("kept"));
  }

  [Test]
  public void Message_ReceivesVtksErrors()
  {
    // Arrange
    using var reader = new vtkXMLUnstructuredGridReader();
    reader.SetFileName(Missing);
    var errors = new List<string>();

    // Act
    #region messages
    // VTK's errors, warnings and text: to this handler, not to a window or the console
    EventHandler<VtkOutputEventArgs> handler = (_, e) =>
    {
      if (e.Kind == VtkOutputKind.Error)
      {
        errors.Add(e.Text);
      }
    };
    VtkOutput.Message += handler;
    try
    {
      reader.Update(); // the file isn't there: VTK reports an error and goes on
    }
    finally
    {
      VtkOutput.Message -= handler;
    }
    #endregion

    // Assert (and no exception, an empty output)
    using var output = reader.GetOutput();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(errors, Has.Some.Contains(Missing));
      Assert.That(output.GetNumberOfCells(), Is.Zero);
    }
  }

  [Test]
  public void ErrorEvent_TakesTheObjectsErrors()
  {
    // Arrange
    var errors = new List<string>();
    var messages = new List<string>();
    EventHandler<VtkOutputEventArgs> record = (_, e) => messages.Add(e.Text);
    VtkOutput.Message += record;

    // Act
    try
    {
      #region error-event
      // one object's errors, which then don't reach VtkOutput; its executive's report of the
      // failure still does
      using var reader = new vtkXMLUnstructuredGridReader();
      reader.SetFileName(Missing);
      reader.ErrorEvent += (_, e) => errors.Add(e.GetText()!);
      reader.Update();
      #endregion
    }
    finally
    {
      VtkOutput.Message -= record;
    }

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(errors, Has.Some.Contains(Missing));
      Assert.That(messages, Has.None.Contains(Missing));
      Assert.That(messages, Has.One.Contains("vtkExecutive"));
    }
  }

  [Test]
  public void Handler_Throwing_ThrowsFromTheCallThatLedToIt()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    Exception? caught = null;

    // Act
    #region exceptions
    // VTK isn't written for exceptions: the handler's stops at the callback, VTK goes on, and the
    // call that led to it throws a VtkException carrying it
    array.ModifiedEvent += (_, _) => throw new InvalidOperationException("no");
    try
    {
      array.Modified();
    }
    catch (VtkException e)
    {
      caught = e.InnerException; // the InvalidOperationException
    }
    #endregion

    // Assert
    Assert.That(caught, Is.TypeOf<InvalidOperationException>().And.Message.EqualTo("no"));
  }

  // a command only VTK holds
  private static void Observe(vtkObject subject, List<string> names) =>
    subject.AddObserver((ulong)vtkCommand_EventIds.ModifiedEvent, new NameRecorder(names));

  #region command
  // a vtkCommand of our own: VTK calls Execute with the object that invoked the event
  public sealed class NameRecorder(List<string>? names = null) : vtkCommand
  {
    public List<string> Names { get; } = names ?? [];

    public override void Execute(vtkObject caller, ulong eventId, IntPtr callData)
    {
      using (caller)
      {
        if (vtkAbstractArray.SafeDownCast(caller) is { } array)
        {
          using (array)
          {
            Names.Add(array.GetName());
          }
        }
      }
    }
  }
  #endregion
}
