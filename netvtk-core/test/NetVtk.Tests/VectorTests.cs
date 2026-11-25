// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// A std::vector in a signature is a C# array, copied both ways (Std.i): a parameter goes in, a
/// non-const reference comes back too, a return comes back.
/// </summary>
[TestFixture]
public class VectorTests
{
  [Test]
  public void Parameter_NumbersGoIn()
  {
    // Arrange
    using var contour = new vtkContourFilter();

    // Act
    contour.SetContourValues(new[] { 0.5, 1.5, 2.5 });

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(contour.GetNumberOfContours(), Is.EqualTo(3));
      Assert.That(contour.GetValue(2), Is.EqualTo(2.5));
    }
  }

  [Test]
  public void Return_NumbersComeBack()
  {
    // Arrange
    using var contour = new vtkContourFilter();
    contour.SetValue(0, 0.25);
    contour.SetValue(1, 0.75);

    // Act
    var values = contour.GetContourValues();

    // Assert
    Assert.That(values, Is.EqualTo(new[] { 0.25, 0.75 }));
  }

  [Test]
  public void Return_StringsComeBack()
  {
    // Arrange
    using var calculator = new vtkArrayCalculator();
    calculator.AddScalarArrayName("pressure");
    calculator.AddScalarArrayName(NonAscii.WideText);

    // Act
    var names = calculator.GetScalarArrayNames();

    // Assert
    Assert.That(names, Is.EqualTo(new[] { "pressure", NonAscii.WideText }));
  }

  [Test]
  public void MutableReference_ComesBackFilled()
  {
    // Arrange
    using var uniforms = new vtkOpenGLUniforms();
    uniforms.SetUniform("weights", vtkUniforms_TupleType.TupleTypeScalar, 1,
                        new[] { 1.5f, 2.5f, 3.5f });
    float[]? weights = null;

    // Act
    var found = uniforms.GetUniform("weights", ref weights);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(found, Is.True);
      Assert.That(weights, Is.EqualTo(new[] { 1.5f, 2.5f, 3.5f }));
    }
  }

  [Test]
  public void Parameter_VtkObjectsGoIn()
  {
    // Arrange (two inputs' point data, appended into the output's)
    using var first = Points(1.0, 2.0);
    using var second = Points(3.0, 4.0, 5.0);
    using var output = new vtkPolyData();

    // Act
    vtkAppendFilter.AppendArrays(new vtkDataSet[] { first, second },
                                 (int)vtkDataObject_AttributeTypes.POINT, output, 5, false);

    // Assert
    using var appended = output.GetPointData().GetArray("p");
    Assert.That(appended.ToDoubleArray(), Is.EqualTo(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }));
  }

  [Test]
  public void Return_VtkObjectsComeBackAsTheirProxies()
  {
    // Arrange
    using var grid = new vtkCellGrid();
    using var attribute = new vtkCellAttribute();
    using var name = new vtkStringToken("temperature");
    using var space = new vtkStringToken("R1");
    attribute.Initialize(name, space, 1);
    grid.AddCellAttribute(attribute);

    // Act
    var attributes = grid.GetCellAttributeList();

    // Assert (each element's proxy holds a reference of its own)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(attributes, Has.Length.EqualTo(1));
      Assert.That(attributes[0].NativePointer, Is.EqualTo(attribute.NativePointer));
    }

    foreach (var item in attributes)
    {
      item.Dispose();
    }
  }

  // a poly data whose point data holds the values as the array "p"
  private static vtkPolyData Points(params double[] values)
  {
    var data = new vtkPolyData();
    using var array = vtkDoubleArray.FromArray(values);
    array.SetName("p");
    data.GetPointData().AddArray(array);
    return data;
  }
}
