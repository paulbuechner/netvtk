// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// An object's text, native pointers in both directions (vtkObjectBase.cs), and where the finalizer
/// releases (VtkFinalizer).
/// </summary>
[TestFixture]
public class InteropTests
{
  [Test]
  public void ToString_IsVtksDescription()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    array.SetName("pressure");

    // Act
    var text = array.ToString();

    // Assert
    Assert.That(text, Does.StartWith("vtkDoubleArray").And.Contain("pressure"));
  }

  [Test]
  public void FromNativePointer_IsTheSameObject()
  {
    // Arrange
    using var data = new vtkPolyData();

    // Act
    using var other = vtkObjectBase.FromNativePointer<vtkPolyData>(data.NativePointer);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(other.NativePointer, Is.EqualTo(data.NativePointer));
      Assert.That(data.GetReferenceCount(), Is.EqualTo(2), "a reference for each proxy");
    }
  }

  [Test]
  public void FromNativePointer_ABase_IsTheObject()
  {
    // Arrange
    using var data = new vtkPolyData();

    // Act
    using var other = vtkObjectBase.FromNativePointer<vtkDataObject>(data.NativePointer);

    // Assert
    Assert.That(other.GetClassName(), Is.EqualTo("vtkPolyData"));
  }

  [Test]
  public void FromNativePointer_AnotherClass_Throws()
  {
    // Arrange
    using var data = new vtkPolyData();

    // Act
    Action wrap = () => vtkObjectBase.FromNativePointer<vtkDoubleArray>(data.NativePointer);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(wrap, Throws.TypeOf<InvalidCastException>());
      Assert.That(data.GetReferenceCount(), Is.EqualTo(1), "nothing held");
    }
  }

  [Test]
  public void Finalizer_WithAContext_ReleasesThere()
  {
    // Arrange (an object only the collection and a lost proxy hold)
    using var collection = new vtkCollection();
    var context = new QueueContext();
    var previous = VtkFinalizer.Context;
    VtkFinalizer.Context = context;
    try
    {
      AddLost(collection);
      Garbage.Collect();
    }
    finally
    {
      VtkFinalizer.Context = previous;
    }

    using var item = collection.GetItemAsObject(0);
    var held = item.GetReferenceCount();

    // Act
    context.Run();

    // Assert
    Assert.That(item.GetReferenceCount(), Is.EqualTo(held - 1));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void AddLost(vtkCollection collection) => collection.AddItem(new vtkPolyData());

  // what was posted, run when the test says
  private sealed class QueueContext : SynchronizationContext
  {
    private readonly Queue<KeyValuePair<SendOrPostCallback, object>> _posted = new();

    public override void Post(SendOrPostCallback d, object state)
    {
      lock (_posted)
      {
        _posted.Enqueue(new KeyValuePair<SendOrPostCallback, object>(d, state));
      }
    }

    public void Run()
    {
      while (true)
      {
        KeyValuePair<SendOrPostCallback, object> next;
        lock (_posted)
        {
          if (_posted.Count == 0)
          {
            return;
          }

          next = _posted.Dequeue();
        }

        next.Key(next.Value);
      }
    }
  }
}
