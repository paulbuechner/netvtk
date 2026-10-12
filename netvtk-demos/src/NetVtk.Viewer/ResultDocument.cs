// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

//
using Vtk;

namespace NetVtk.Viewer;

/// <summary>Where a field's values sit: on the points, or one per cell.</summary>
public enum FieldLocation
{
  Points,
  Cells,
}

/// <summary>A field of a result: its name, where it sits, and its components.</summary>
public sealed record ResultField(string Name, FieldLocation Location, int Components)
{
  /// <summary>One value per point or cell, not a vector.</summary>
  public bool IsScalar => Components == 1;

  /// <summary>How a field list shows it.</summary>
  public override string ToString() =>
    $"{Name} ({(Location == FieldLocation.Points ? "points" : "cells")}{(IsScalar ? "" : $", {Components} components")})";
}

/// <summary>
/// A simulation result read from a file: an unstructured grid (.vtu, or legacy .vtk) and its
/// fields. Reading needs no window, so it may run off the UI thread.
/// </summary>
public sealed class ResultDocument : IDisposable
{
  private ResultDocument(string name, vtkUnstructuredGrid grid, IReadOnlyList<ResultField> fields)
  {
    Name = name;
    Grid = grid;
    Fields = fields;
  }

  /// <summary>The file's name.</summary>
  public string Name { get; }

  public vtkUnstructuredGrid Grid { get; }

  /// <summary>The point fields, then the cell fields, as the file lists them.</summary>
  public IReadOnlyList<ResultField> Fields { get; }

  /// <summary>Reads <paramref name="path"/>.</summary>
  /// <exception cref="InvalidDataException">
  /// The file isn't an unstructured grid VTK reads; the message says why.
  /// </exception>
  public static ResultDocument Read(string path)
  {
    var name = Path.GetFileName(path);
    if (!File.Exists(path))
    {
      throw new InvalidDataException("The file doesn't exist.");
    }

    // the reader's errors, which its ErrorEvent takes instead of VTK's output window
    var errors = new List<string>();
    using var reader = Reader(path);
    reader.ErrorEvent += (_, e) => errors.Add(e.GetText() ?? "");
    reader.Update();
    using var output = reader.GetOutputDataObject(0);
    var grid = output is null ? null : vtkUnstructuredGrid.SafeDownCast(output);
    if (errors.Count > 0 || grid is null || grid.GetNumberOfCells() == 0)
    {
      grid?.Dispose();
      throw new InvalidDataException(errors.Count > 0
                                       ? FirstLine(errors[0])
                                       : "The file holds no unstructured grid with cells.");
    }

    return new ResultDocument(name, grid, FieldsOf(grid));
  }

  public void Dispose() => Grid.Dispose();

  // legacy .vtk by its extension, XML otherwise
  private static vtkAlgorithm Reader(string path)
  {
    if (string.Equals(Path.GetExtension(path), ".vtk", StringComparison.OrdinalIgnoreCase))
    {
      var legacy = new vtkUnstructuredGridReader();
      legacy.SetFileName(path);
      return legacy;
    }

    var xml = new vtkXMLUnstructuredGridReader();
    xml.SetFileName(path);
    return xml;
  }

  private static List<ResultField> FieldsOf(vtkUnstructuredGrid grid)
  {
    List<ResultField> fields = [];
    using (var points = grid.GetPointData())
    {
      Add(points, FieldLocation.Points);
    }

    using (var cells = grid.GetCellData())
    {
      Add(cells, FieldLocation.Cells);
    }

    return fields;

    void Add(vtkFieldData data, FieldLocation location)
    {
      for (var i = 0; i < data.GetNumberOfArrays(); i++)
      {
        // numbers only: a string array isn't a field to colour by
        using var array = data.GetArray(i);
        if (array?.GetName() is { Length: > 0 } arrayName)
        {
          fields.Add(new ResultField(arrayName, location, array.GetNumberOfComponents()));
        }
      }
    }
  }

  // VTK's errors name the source file and line first: the message is the rest
  private static string FirstLine(string error)
  {
    var lines = error.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
    return lines.Length > 1 ? lines[1].Trim() : error.Trim();
  }
}
