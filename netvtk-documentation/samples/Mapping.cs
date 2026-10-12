// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>From C++ to C#: what VTK's signatures become.</summary>
[TestFixture]
public class Mapping
{
  [Test]
  public void New_AndConstructor_AreTheSame()
  {
    // Act
    #region new
    // C++: vtkNew<vtkSphereSource> sphere; the constructor calls vtkSphereSource::New()
    using var sphere = new vtkSphereSource();
    using var same = vtkSphereSource.New(); // the same, as VTK spells it
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(sphere.GetClassName(), Is.EqualTo("vtkSphereSource"));
      Assert.That(same.GetClassName(), Is.EqualTo("vtkSphereSource"));
    }
  }

  [Test]
  public void SizeHints_AreArrays()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();
    using var polyData = sphere.GetOutput();
    using var property = new vtkProperty();
    using var window = new vtkRenderWindow();

    // Act
    #region size-hints
    // double* GetBounds() VTK_SIZEHINT(6): a C# array of 6, copied right after the call
    double[] bounds = polyData.GetBounds(); // xmin, xmax, ymin, ymax, zmin, zmax
    double[] center = polyData.GetCenter(); // 3
    double[] color = property.GetColor();   // 3
    int[] size = window.GetSize();          // 2

    // pointers in parameters are arrays too
    property.SetColor(new[] { 0.2, 0.4, 0.8 });
    var first = new double[3];
    polyData.GetPoint(0, first); // fills the array given
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(bounds, Has.Length.EqualTo(6));
      Assert.That(color, Has.Length.EqualTo(3));
      Assert.That(size, Has.Length.EqualTo(2));
      Assert.That(property.GetColor(), Is.EqualTo(new[] { 0.2, 0.4, 0.8 }));
      Assert.That(center, Has.Length.EqualTo(3));
      Assert.That(first, Is.EqualTo(polyData.GetPoint(0)));
    }
  }

  [Test]
  public void Strings_AreStrings()
  {
    // Arrange
    using var array = new vtkDoubleArray();

    // Act
    #region strings
    // const char* both ways, UTF-8; null for nullptr
    array.SetName("Temperatur in °C");
    string name = array.GetName();
    string className = array.GetClassName(); // "vtkDoubleArray"
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(name, Is.EqualTo("Temperatur in °C"));
      Assert.That(className, Is.EqualTo("vtkDoubleArray"));
    }
  }

  [Test]
  public void WidthTypedefs_AreLongAndULong()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();
    using var polyData = sphere.GetOutput();

    // Act
    #region width-typedefs
    long points = polyData.GetNumberOfPoints(); // vtkIdType: 64 bits on every platform
    ulong time = polyData.GetMTime();           // vtkMTimeType

    // C unsigned long: event ids, observer tags
    ulong tag = polyData.AddObserver(vtkCommand_EventIds.ModifiedEvent, (_, _) => { });
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(points, Is.Positive);
      Assert.That(time, Is.Positive);
      Assert.That(tag, Is.Positive);
    }
  }

  [Test]
  public void NestedEnums_HaveFlatNames()
  {
    // Arrange
    using var grid = new vtkUnstructuredGrid();
    using var contour = new vtkContourFilter();

    // Act
    #region enums
    // vtkCommand::ModifiedEvent: the enum vtkCommand::EventIds, C# vtkCommand_EventIds
    var modified = vtkCommand_EventIds.ModifiedEvent;

    // VTK's setters take an int where C++ converts: cast
    contour.SetInputArrayToProcess(
      0, 0, 0, (int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_POINTS, "stress");

    // enums outside a class keep their names
    var hexahedron = VTKCellType.VTK_HEXAHEDRON;
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That((ulong)modified, Is.EqualTo(33));
      Assert.That((int)hexahedron, Is.EqualTo(12));
    }
  }

  [Test]
  public void TemplateInstances_AreClasses()
  {
    // Arrange
    using var array = vtkDoubleArray.FromArray([1.0, 2.0, 3.0]);

    // Act
    #region templates
    // vtkDoubleArray derives from vtkAOSDataArrayTemplate<double>: in C#, a class named by its
    // arguments, with the template's typed members
    vtkAOSDataArrayTemplate_double typed = array;
    double second = typed.GetValue(1);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(second, Is.EqualTo(2.0));
      Assert.That(typeof(vtkDoubleArray).BaseType,
                  Is.EqualTo(typeof(vtkAOSDataArrayTemplate_double)));
    }
  }

  [Test]
  public void SmartPointers_AreTheObject()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();
    using var polyData = sphere.GetOutput();

    // Act
    #region smart-pointers
    // static vtkSmartPointer<vtkFloatArray> GetCellNormals(vtkPolyData*): the proxy holds the
    // reference the smart pointer held
    using vtkFloatArray normals = vtkPolyDataNormals.GetCellNormals(polyData);
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(normals.GetNumberOfTuples(), Is.EqualTo(polyData.GetNumberOfPolys()));
      Assert.That(normals.GetReferenceCount(), Is.EqualTo(1));
    }
  }

  [Test]
  public void Casts_GoThroughSafeDownCast()
  {
    // Arrange
    using var sphere = new vtkSphereSource();
    sphere.Update();

    // Act
    #region casts
    // vtkPolyData::SafeDownCast(object): the derived proxy, or null
    using var data = sphere.GetOutputDataObject(0); // a vtkDataObject
    using var polyData = vtkPolyData.SafeDownCast(data);
    using var grid = vtkUnstructuredGrid.SafeDownCast(data); // null
    #endregion

    // Assert (the returned proxy is a vtkDataObject, which a C# cast can't make a vtkPolyData)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(polyData.GetNumberOfPolys(), Is.Positive);
      Assert.That(grid, Is.Null);
      Assert.That(data, Is.Not.InstanceOf<vtkPolyData>());
    }
  }
}
