// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// The result the samples read: a cantilever beam of hexahedra along x, clamped at x = 0, a load at
/// its tip bending it down, with the point fields of a static analysis: its displacement
/// (Euler-Bernoulli) and the bending stress. A smaller netvtk-demos' SampleResult.
/// </summary>
internal static class Beam
{
  public const double Length = 10;
  public const double Width = 1;
  public const double Height = 2;
  public const double TipDeflection = 0.5;

  // elements along each axis
  public const int Nx = 20;
  public const int Ny = 2;
  public const int Nz = 4;

  public const int Points = (Nx + 1) * (Ny + 1) * (Nz + 1);
  public const int Cells = Nx * Ny * Nz;

  // Young's modulus (MPa, steel); the load follows from the tip deflection
  private const double Modulus = 210_000;

  /// <summary>The largest bending stress, at the clamp's top and bottom: 3 E w h / 2 L².</summary>
  public const double MaxStress = TipDeflection * 3 * Modulus * (Height / 2) / (Length * Length);

  /// <summary>Writes the beam to <paramref name="path"/> as a .vtu.</summary>
  public static void Write(string path)
  {
    using var grid = Build();
    using var writer = new vtkXMLUnstructuredGridWriter();
    writer.SetFileName(path);
    writer.SetInputData(grid);
    if (writer.Write() != 1)
    {
      throw new IOException($"Couldn't write {path}.");
    }
  }

  private static vtkUnstructuredGrid Build()
  {
    const int px = Nx + 1, py = Ny + 1;
    var coordinates = new double[3 * Points];
    var displacement = new double[3 * Points];
    var stress = new double[Points];

    // w(x) = P x² (3L - x) / 6EI, the cross section turning by w'(x), the stress M(x) z / I
    const double inertia = Width * Height * Height * Height / 12;
    const double load = TipDeflection * 3 * Modulus * inertia / (Length * Length * Length);
    for (var k = 0; k <= Nz; k++)
    {
      for (var j = 0; j <= Ny; j++)
      {
        for (var i = 0; i <= Nx; i++)
        {
          var index = Index(i, j, k);
          var x = Length * i / Nx;
          var z = Height * k / Nz - Height / 2;
          coordinates[3 * index] = x;
          coordinates[3 * index + 1] = Width * j / Ny - Width / 2;
          coordinates[3 * index + 2] = z;
          displacement[3 * index] = z * load * x * (2 * Length - x) / (2 * Modulus * inertia);
          displacement[3 * index + 2] = -load * x * x * (3 * Length - x) / (6 * Modulus * inertia);
          stress[index] = load * (Length - x) * z / inertia;
        }
      }
    }

    var offsets = new long[Cells + 1];
    var connectivity = new long[8 * Cells];
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
    using (var array = vtkDoubleArray.FromArray(displacement, 3))
    {
      array.SetName("displacement");
      pointData.AddArray(array);
    }

    using (var array = vtkDoubleArray.FromArray(stress))
    {
      array.SetName("stress");
      pointData.AddArray(array);
    }

    return grid;

    static int Index(int i, int j, int k) => i + px * (j + py * k);
  }
}
