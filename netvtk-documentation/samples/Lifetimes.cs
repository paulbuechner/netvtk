// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Lifetimes: a proxy owns one reference to its VTK object; Dispose or the finalizer drops it, and
/// VTK keeps what it holds.
/// </summary>
[TestFixture]
public class Lifetimes
{
  [Test]
  public void References_OnePerProxy()
  {
    // Act
    #region references
    var points = new vtkPoints(); // New(): 1 reference, the proxy's
    using var polyData = new vtkPolyData();
    polyData.SetPoints(points);       // 2: the poly data's too
    var again = polyData.GetPoints(); // 3: a returned object, another proxy, another reference

    // Dispose drops the proxy's reference at once; the object goes when no one holds it
    again.Dispose();  // 2
    points.Dispose(); // 1: the poly data's
    #endregion

    // Assert
    using var kept = polyData.GetPoints();
    Assert.That(kept.GetReferenceCount(), Is.EqualTo(2));
  }

  [Test]
  public void Count_IncludesVtksOwnReferences()
  {
    // Act
    using var sphere = new vtkSphereSource();

    // Assert (the proxy's, and one VTK holds)
    Assert.That(sphere.GetReferenceCount(), Is.EqualTo(2));
  }

  [Test]
  public void Using_DisposesAtTheEndOfTheScope()
  {
    // Act
    #region using
    using (var reader = new vtkXMLUnstructuredGridReader())
    {
      // ... read, take what's needed
    }

    // or for the rest of the block
    using var grid = new vtkUnstructuredGrid();
    #endregion

    // Assert
    Assert.That(grid.GetReferenceCount(), Is.EqualTo(1));
  }

  [Test]
  public void ObjectSetOnAnother_OutlivesItsProxy()
  {
    // Act
    #region kept
    // VTK counts its own references: the actor holds the mapper after its proxy is gone
    using var actor = new vtkActor();
    using (var mapper = new vtkPolyDataMapper())
    {
      actor.SetMapper(mapper);
    }

    using var held = actor.GetMapper(); // a new proxy for the same mapper
    #endregion

    // Assert
    Assert.That(held.GetClassName(), Does.EndWith("PolyDataMapper"));
  }

  [Test]
  public void Finalizer_DropsTheProxysReference()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();
    using var kept = sphere.GetOutput();

    // Act (a second proxy only the finalizer releases)
    Lose(sphere);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    // Assert (the sphere's and kept's)
    Assert.That(kept.GetReferenceCount(), Is.EqualTo(2));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void Lose(vtkSphereSource sphere) => sphere.GetOutput();
}
