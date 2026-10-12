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
/// The start page's program (Programs/Contours.cs, a whole file with its Main): run, and what it
/// prints and writes checked against the file read back.
/// </summary>
[TestFixture]
public class Programs : Files
{
  [Test]
  public void Contours_WritesNineRings()
  {
    // Act
    var output = Run(typeof(Contours.Program));

    // Assert (each ring's points carry its value of the field, as floats)
    using var reader = new vtkXMLPolyDataReader();
    reader.SetFileName("rings.vtp");
    reader.Update();
    using var rings = reader.GetOutput();
    using var pointData = rings.GetPointData();
    using var elevation = pointData.GetArray("Elevation");
    var values = elevation.ToDoubleArray().Select(v => Math.Round(v, 6)).Distinct().Order();
    using (Assert.EnterMultipleScope())
    {
      Assert.That(output,
                  Is.EqualTo(
                    $"{rings.GetNumberOfPoints()} points, {rings.GetNumberOfLines()} segments"));
      Assert.That(rings.GetNumberOfLines(), Is.GreaterThan(9 * 32));
      Assert.That(values, Is.EqualTo(Enumerable.Range(1, 9).Select(i => i / 10.0)).Within(1e-6));
    }
  }

  // runs a program's Main and returns what it printed
  private static string Run(Type program)
  {
    var main = program.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!;
    var console = Console.Out;
    using var output = new StringWriter();
    Console.SetOut(output);
    try
    {
      main.Invoke(null, null);
    }
    finally
    {
      Console.SetOut(console);
    }

    return output.ToString().Trim();
  }
}
