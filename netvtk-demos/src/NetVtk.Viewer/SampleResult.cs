// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

//
using Vtk;

namespace NetVtk.Viewer;

/// <summary>
/// The sample result both demos open: a cantilever beam of hexahedra, clamped at x = 0, a load at
/// its tip bending it down, written as a .vtu with the fields of a static analysis: the
/// displacement (Euler-Bernoulli), the bending stress and a temperature.
/// </summary>
public static class SampleResult
{
  public const double Length = 10;
  public const double Width = 1;
  public const double Height = 2;

  /// <summary>The tip's deflection, in the beam's units.</summary>
  public const double TipDeflection = 0.5;

  public const string Displacement = "displacement";
  public const string Stress = "stress_xx";
  public const string Temperature = "temperature";

  // the mesh: elements along each axis
  private const int Nx = 40;
  private const int Ny = 4;
  private const int Nz = 8;

  // Young's modulus (MPa, steel); the load follows from the tip deflection
  private const double Modulus = 210_000;

  /// <summary>Writes the beam to <paramref name="path"/>.</summary>
  /// <exception cref="IOException">The writer failed.</exception>
  public static void Write(string path)
  {
    using var grid = Build();
    using var writer = new vtkXMLUnstructuredGridWriter();
    writer.SetFileName(path);
    writer.SetInputData(grid);
    if (writer.Write() != 1)
    {
      throw new IOException($"Couldn't write {Path.GetFileName(path)}.");
    }
  }

  /// <summary>The beam: its points, hexahedra and point fields.</summary>
  public static vtkUnstructuredGrid Build()
  {
    const int px = Nx + 1, py = Ny + 1, pz = Nz + 1;
    var count = px * py * pz;
    var coordinates = new double[3 * count];
    var displacement = new double[3 * count];
    var stress = new double[count];
    var temperature = new double[count];

    // the bending: w(x) = P x^2 (3L - x) / 6EI, the cross section turning by w'(x), the stress
    // M(x) z / I with M(x) = P (L - x)
    const double inertia = Width * Height * Height * Height / 12;
    const double load = TipDeflection * 3 * Modulus * inertia / (Length * Length * Length);
    for (var k = 0; k < pz; k++)
    {
      for (var j = 0; j < py; j++)
      {
        for (var i = 0; i < px; i++)
        {
          var index = Index(i, j, k);
          var x = Length * i / Nx;
          var y = Width * j / Ny - Width / 2;
          var z = Height * k / Nz - Height / 2;
          var deflection = load * x * x * (3 * Length - x) / (6 * Modulus * inertia);
          var slope = load * x * (2 * Length - x) / (2 * Modulus * inertia);
          coordinates[3 * index] = x;
          coordinates[3 * index + 1] = y;
          coordinates[3 * index + 2] = z;
          displacement[3 * index] = z * slope;
          displacement[3 * index + 2] = -deflection;
          stress[index] = load * (Length - x) * z / inertia;
          // a hot band across the middle, cooling toward the ends
          var band = (x - Length / 2) / (Length / 8);
          temperature[index] = 20 + 60 * Math.Exp(-band * band / 2);
        }
      }
    }

    var offsets = new long[Nx * Ny * Nz + 1];
    var connectivity = new long[8 * Nx * Ny * Nz];
    var cell = 0;
    for (var k = 0; k < Nz; k++)
    {
      for (var j = 0; j < Ny; j++)
      {
        for (var i = 0; i < Nx; i++)
        {
          // VTK's hexahedron: the bottom face counterclockwise, then the top
          long[] corners =
          [
            Index(i, j, k), Index(i + 1, j, k), Index(i + 1, j + 1, k), Index(i, j + 1, k),
            Index(i, j, k + 1), Index(i + 1, j, k + 1), Index(i + 1, j + 1, k + 1),
            Index(i, j + 1, k + 1),
          ];
          corners.CopyTo(connectivity, 8 * cell);
          offsets[++cell] = 8 * cell;
        }
      }
    }

    var grid = new vtkUnstructuredGrid();
    using (var points = new vtkPoints())
    using (var data = vtkDoubleArray.FromArray(coordinates, 3))
    {
      points.SetData(data);
      grid.SetPoints(points);
    }

    using (var offsetArray = vtkIdTypeArray.FromArray(offsets))
    using (var connectivityArray = vtkIdTypeArray.FromArray(connectivity))
    using (var cells = new vtkCellArray())
    {
      cells.SetData(offsetArray, connectivityArray);
      grid.SetCells((int)VTKCellType.VTK_HEXAHEDRON, cells);
    }

    using var pointData = grid.GetPointData();
    AddField(pointData, Displacement, displacement, 3);
    AddField(pointData, Stress, stress, 1);
    AddField(pointData, Temperature, temperature, 1);
    pointData.SetActiveVectors(Displacement);
    pointData.SetActiveScalars(Stress);
    return grid;

    static int Index(int i, int j, int k) => i + px * (j + py * k);
  }

  private static void AddField(vtkPointData pointData, string name, double[] values, int components)
  {
    using var array = vtkDoubleArray.FromArray(values, components);
    array.SetName(name);
    pointData.AddArray(array);
  }
}
