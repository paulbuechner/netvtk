// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>VTK's messages in C# (VtkOutput), and an object's own (its ErrorEvent).</summary>
[TestFixture]
public class OutputTests
{
  private const string Missing = "netvtk-missing.vtp";

  [Test]
  public void Message_ReceivesVtksErrors()
  {
    // Arrange
    var messages = new List<VtkOutputEventArgs>();
    EventHandler<VtkOutputEventArgs> handler = (_, e) => messages.Add(e);
    using var reader = new vtkXMLPolyDataReader();
    reader.SetFileName(Missing);
    VtkOutput.Message += handler;

    // Act
    try
    {
      reader.Update();
    }
    finally
    {
      VtkOutput.Message -= handler;
    }

    // Assert
    Assert.That(messages,
                Has.Some.Matches<VtkOutputEventArgs>(m => m.Kind == VtkOutputKind.Error
                                                          && m.Text.Contains(Missing)));
  }

  [Test]
  public void ErrorEvent_TakesTheObjectsErrors()
  {
    // Arrange
    var errors = new List<string>();
    using var reader = new vtkXMLPolyDataReader();
    reader.SetFileName(Missing);
    reader.ErrorEvent += (_, e) => errors.Add(e.GetText());

    // Act
    reader.Update();

    // Assert
    Assert.That(errors, Has.Some.Contains(Missing));
  }

  [Test]
  public void Message_HandlerThrowing_ThrowsFromTheCallThatReported()
  {
    // Arrange
    EventHandler<VtkOutputEventArgs>
      handler = (_, _) => throw new InvalidOperationException("seen");
    using var reader = new vtkXMLPolyDataReader();
    reader.SetFileName(Missing);
    VtkOutput.Message += handler;

    // Act
    Action update = () => reader.Update();

    // Assert
    try
    {
      Assert.That(update,
                  Throws.TypeOf<VtkException>().With.InnerException.Message.EqualTo("seen"));
    }
    finally
    {
      VtkOutput.Message -= handler;
    }
  }

  [Test]
  public void Uninstall_GivesVtkItsWindowBack()
  {
    // Arrange
    VtkOutput.Install();

    // Act
    VtkOutput.Uninstall();
    var uninstalled = !VtkOutput.IsInstalled;
    VtkOutput.Install();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(uninstalled);
      Assert.That(VtkOutput.IsInstalled);
    }
  }
}
