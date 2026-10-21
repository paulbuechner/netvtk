// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Reflection;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// Native interop: an object's vtkObjectBase* and a proxy for one, and the SWIG files the package
/// carries.
/// </summary>
[TestFixture]
public class Interop
{
  [Test]
  public void NativePointer_RoundTrips()
  {
    // Arrange
    using var polyData = new vtkPolyData();

    // Act
    #region native-pointer
    // the vtkObjectBase*, for C++ that takes VTK objects (C++/CLI, a native library of your own):
    // valid while the proxy holds its reference
    IntPtr pointer = polyData.NativePointer;

    // a proxy for a pointer: a reference of its own, checked against the class asked for
    using var same = vtkObjectBase.FromNativePointer<vtkPolyData>(pointer);
    using var asBase = vtkObjectBase.FromNativePointer<vtkDataObject>(pointer);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(same.NativePointer, Is.EqualTo(pointer));
      Assert.That(asBase.GetClassName(), Is.EqualTo("vtkPolyData"));
      Assert.That(polyData.GetReferenceCount(), Is.EqualTo(3), "a reference for each proxy");
    }
  }

  [Test]
  public void FromNativePointer_AnotherClass_Throws()
  {
    // Arrange
    using var polyData = new vtkPolyData();

    // Act
    Action wrap = () => vtkObjectBase.FromNativePointer<vtkDoubleArray>(polyData.NativePointer);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(wrap, Throws.TypeOf<InvalidCastException>());
      Assert.That(polyData.GetReferenceCount(), Is.EqualTo(1), "nothing held");
    }
  }

  [Test]
  public void NativePointer_Disposed_IsZero()
  {
    // Arrange
    var polyData = new vtkPolyData();

    // Act
    polyData.Dispose();

    // Assert
    Assert.That(polyData.NativePointer, Is.EqualTo(IntPtr.Zero));
  }

  [Test]
  public void Package_CarriesItsSwigFiles()
  {
    // Arrange (the restored package's folder, from the csproj)
    var package = typeof(Interop).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .Single(a => a.Key == "NetVtkPackage")
      .Value!;
    var swig = Path.Combine(package, "swig");

    // Act
    var folders = Directory.GetDirectories(swig).Select(Path.GetFileName).Order();
    var indexes = Directory.GetFiles(swig, "*.json").Select(Path.GetFileName).Order();

    // Assert (a module per VTK module and RenderingOpenGL2's Windows-only one, the typemap library,
    // the companions, the module headers, the interop file)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(
        folders, Is.EqualTo(new[] { "common", "extras", "headers", "interop", "wrapper" }));
      Assert.That(indexes, Is.EqualTo(new[] { "classes.json", "modules.json", "platforms.json" }));
      Assert.That(Directory.GetFiles(Path.Combine(swig, "wrapper"), "*.i"), Has.Length.EqualTo(26));
      Assert.That(File.Exists(Path.Combine(swig, "interop", "NetVtkInterop.i")));
      Assert.That(File.Exists(Path.Combine(swig, "wrapper", "CommonCore.i")));
    }
  }
}
