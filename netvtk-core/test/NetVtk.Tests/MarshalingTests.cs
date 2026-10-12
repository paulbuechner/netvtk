// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// The typemap library's conversions VTK's signatures use: UTF-8 strings (Strings.i), a C# stream
/// for a std::ostream (Streams.i), a C# array for a C array parameter (Types.i).
/// </summary>
[TestFixture]
public class MarshalingTests
{
  [Test]
  public void String_NonAscii_RoundTrips()
  {
    // Arrange
    using var array = new vtkDoubleArray();

    // Act
    array.SetName(NonAscii.WideText);

    // Assert
    Assert.That(array.GetName(), Is.EqualTo(NonAscii.WideText));
  }

  [Test]
  public void String_Null_IsNull()
  {
    // Arrange
    using var array = new vtkDoubleArray();

    // Act
    array.SetName(null);

    // Assert
    Assert.That(array.GetName(), Is.Null);
  }

  [Test]
  public void Stream_TakesWhatVtkPrints()
  {
    // Arrange
    using var array = new vtkDoubleArray();
    array.SetName("pressure");
    using var stream = new MemoryStream();

    // Act
    array.Print(stream);

    // Assert
    var text = Encoding.UTF8.GetString(stream.ToArray());
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text, Does.StartWith("vtkDoubleArray"));
      Assert.That(text, Does.Contain("pressure"));
    }
  }

  [Test]
  public void ArrayParameter_IsReadByTheCallee()
  {
    // Arrange
    using var property = new vtkProperty();

    // Act
    property.SetColor(new[] { 0.1, 0.2, 0.3 });

    // Assert
    Assert.That(property.GetColor(), Is.EqualTo(new[] { 0.1, 0.2, 0.3 }));
  }
}
