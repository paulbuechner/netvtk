// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

//
using Vtk;

namespace NetVtk.Viewer;

/// <summary>What a result shows: the field it's coloured by, and the views of it.</summary>
/// <param name="Field">The field the surface, the cut and the contours are coloured by.</param>
/// <param name="Warp">Moves the points by the result's displacement, times
/// <paramref name="WarpScale"/>.</param>
/// <param name="Cut">A plane across the longest axis at <paramref name="CutPosition"/> (0 to 1),
/// the surface see-through.</param>
/// <param name="Contours">Isosurfaces of a scalar field, the surface see-through.</param>
public sealed record SceneOptions(ResultField Field, bool Warp, double WarpScale, bool Cut,
                                  double CutPosition, bool Contours)
{
  /// <summary>
  /// The isosurfaces <see cref="Contours"/> shows, evenly over the field's range.
  /// </summary>
  public const int ContourCount = 8;
}

/// <summary>
/// A result in a renderer: its surface coloured by a field with a scalar bar, a warp by the
/// displacement, a cut plane and contours. The pipeline is built once; <see cref="Apply"/> changes
/// what it shows. Create, use and dispose it on the thread that renders.
/// </summary>
public sealed class ResultScene : IDisposable
{
  private readonly ResultDocument _document;
  private readonly vtkRenderer _renderer;

  // the displacement field the warp moves the points by, or null
  private readonly ResultField? _displacement;

  private readonly vtkWarpVector _warp = new();
  private readonly vtkDataSetSurfaceFilter _surface = new();
  private readonly vtkPolyDataMapper _surfaceMapper = new();
  private readonly vtkActor _surfaceActor = new();
  private readonly vtkPlane _plane = new();
  private readonly vtkCutter _cutter = new();
  private readonly vtkPolyDataMapper _cutMapper = new();
  private readonly vtkActor _cutActor = new();
  private readonly vtkCellDataToPointData _toPoints = new();
  private readonly vtkContourFilter _contour = new();
  private readonly vtkPolyDataMapper _contourMapper = new();
  private readonly vtkActor _contourActor = new();
  private readonly vtkLookupTable _colors = new();
  private readonly vtkScalarBarActor _scalarBar = new();

  /// <summary>
  /// Builds the pipeline for <paramref name="document"/> into <paramref name="renderer"/>.
  /// </summary>
  public ResultScene(ResultDocument document, vtkRenderer renderer)
  {
    _document = document;
    _renderer = renderer;
    _displacement = DisplacementOf(document);
    var bounds = document.Grid.GetBounds();
    Diagonal = Math.Sqrt(Square(bounds[1] - bounds[0])
                         + Square(bounds[3] - bounds[2])
                         + Square(bounds[5] - bounds[4]));
    AutoWarpScale = AutoScale(document, _displacement, Diagonal);

    // the grid, warped when the result has a displacement (a scale of 0 when off)
    _warp.SetInputData(document.Grid);
    if (_displacement is not null)
    {
      _warp.SetInputArrayToProcess(0, 0, 0,
                                   (int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_POINTS,
                                   _displacement.Name);
    }

    using var warped = _warp.GetOutputPort();
    _surface.SetInputConnection(warped);
    using (var surface = _surface.GetOutputPort())
    {
      _surfaceMapper.SetInputConnection(surface);
    }

    _cutter.SetCutFunction(_plane);
    _cutter.SetInputConnection(warped);
    using (var cut = _cutter.GetOutputPort())
    {
      _cutMapper.SetInputConnection(cut);
    }

    // contours of a cell field: its values on the points first
    _toPoints.SetInputConnection(warped);
    _toPoints.PassCellDataOn();
    using (var points = _toPoints.GetOutputPort())
    {
      _contour.SetInputConnection(points);
    }

    _contour.ComputeScalarsOn();
    using (var contour = _contour.GetOutputPort())
    {
      _contourMapper.SetInputConnection(contour);
    }

    // blue to red, as results are usually shown
    _colors.SetHueRange(0.667, 0.0);
    _colors.Build();
    foreach (var mapper in new[] { _surfaceMapper, _cutMapper, _contourMapper })
    {
      mapper.SetLookupTable(_colors);
      mapper.UseLookupTableScalarRangeOn();
      mapper.ScalarVisibilityOn();
    }

    _surfaceActor.SetMapper(_surfaceMapper);
    _cutActor.SetMapper(_cutMapper);
    _contourActor.SetMapper(_contourMapper);
    using (var contourLook = _contourActor.GetProperty())
    {
      contourLook.SetLineWidth(2);
    }

    _scalarBar.SetLookupTable(_colors);
    _scalarBar.SetNumberOfLabels(5);
    _scalarBar.SetMaximumWidthInPixels(110);
    _scalarBar.UnconstrainedFontSizeOn();

    _renderer.AddActor(_surfaceActor);
    _renderer.AddActor(_cutActor);
    _renderer.AddActor(_contourActor);
    _renderer.AddViewProp(_scalarBar);
  }

  /// <summary>The bounding box's diagonal, in the result's units.</summary>
  public double Diagonal { get; }

  /// <summary>
  /// The warp scale that moves the largest displacement a tenth of the diagonal; 1 without a
  /// displacement.
  /// </summary>
  public double AutoWarpScale { get; }

  /// <summary>Whether the result has a displacement to warp by.</summary>
  public bool CanWarp => _displacement is not null;

  /// <summary>
  /// The options as the result first shows: the first field, warped, nothing else.
  /// </summary>
  public SceneOptions DefaultOptions() =>
    new(DefaultField(), CanWarp, AutoWarpScale, false, 0.5, false);

  /// <summary>Shows <paramref name="options"/>; the caller redraws.</summary>
  public void Apply(SceneOptions options)
  {
    var field = options.Field;
    using var array = ArrayOf(field);
    // a vector by its magnitude
    var range = array?.GetRange(field.IsScalar ? 0 : -1) ?? new[] { 0.0, 1.0 };
    if (field.IsScalar)
    {
      _colors.SetVectorModeToComponent();
    }
    else
    {
      _colors.SetVectorModeToMagnitude();
    }

    _colors.SetRange(range[0], range[1]);
    _scalarBar.SetTitle(field.IsScalar ? field.Name : $"|{field.Name}|");
    foreach (var mapper in new[] { _surfaceMapper, _cutMapper })
    {
      Colour(mapper, field);
    }

    _warp.SetScaleFactor(options.Warp && CanWarp ? options.WarpScale : 0);

    // the plane across the longest axis
    var bounds = _document.Grid.GetBounds();
    var axis = LongestAxis(bounds);
    double[] origin =
    [
      (bounds[0] + bounds[1]) / 2, (bounds[2] + bounds[3]) / 2,
      (bounds[4] + bounds[5]) / 2
    ];
    origin[axis] = bounds[2 * axis]
                   + options.CutPosition * (bounds[2 * axis + 1] - bounds[2 * axis]);
    double[] normal = [0, 0, 0];
    normal[axis] = 1;
    _plane.SetOrigin(origin);
    _plane.SetNormal(normal);
    _cutActor.SetVisibility(options.Cut ? 1 : 0);

    // contours of a scalar field, coloured by their values (on the points: a cell field's too)
    var contours = options.Contours && field.IsScalar;
    if (contours)
    {
      _contour.SetInputArrayToProcess(0, 0, 0,
                                      (int)vtkDataObject_FieldAssociations.FIELD_ASSOCIATION_POINTS,
                                      field.Name);
      _contour.GenerateValues(SceneOptions.ContourCount, range[0], range[1]);
      _contourMapper.SetScalarModeToUsePointFieldData();
      _contourMapper.SelectColorArray(field.Name);
    }

    _contourActor.SetVisibility(contours ? 1 : 0);

    // the surface see-through while it would hide the cut or the contours
    using var surfaceLook = _surfaceActor.GetProperty();
    surfaceLook.SetOpacity(options.Cut || contours ? 0.25 : 1.0);
  }

  /// <summary>Takes the scene's actors out of the renderer, then releases them.</summary>
  public void Dispose()
  {
    _renderer.RemoveActor(_surfaceActor);
    _renderer.RemoveActor(_cutActor);
    _renderer.RemoveActor(_contourActor);
    _renderer.RemoveViewProp(_scalarBar);
    foreach (IDisposable item in new IDisposable[]
             {
               _scalarBar, _colors, _contourActor, _contourMapper, _contour, _toPoints,
               _cutActor, _cutMapper, _cutter, _plane, _surfaceActor, _surfaceMapper, _surface,
               _warp,
             })
    {
      item.Dispose();
    }
  }

  // for the tests: what the cut and the contours produced at the last render or Update
  internal vtkCutter Cutter => _cutter;

  internal vtkContourFilter Contour => _contour;

  internal vtkScalarBarActor ScalarBar => _scalarBar;

  internal vtkWarpVector WarpFilter => _warp;

  private ResultField DefaultField()
  {
    // a scalar first: a vector's magnitude says less
    foreach (var field in _document.Fields)
    {
      if (field.IsScalar)
      {
        return field;
      }
    }

    return _document.Fields.Count > 0
      ? _document.Fields[0]
      : throw new InvalidOperationException($"{_document.Name} has no fields.");
  }

  private vtkDataArray? ArrayOf(ResultField field)
  {
    using vtkFieldData data = field.Location == FieldLocation.Points
      ? _document.Grid.GetPointData()
      : _document.Grid.GetCellData();
    return data.GetArray(field.Name);
  }

  private static void Colour(vtkPolyDataMapper mapper, ResultField field)
  {
    if (field.Location == FieldLocation.Points)
    {
      mapper.SetScalarModeToUsePointFieldData();
    }
    else
    {
      mapper.SetScalarModeToUseCellFieldData();
    }

    mapper.SelectColorArray(field.Name);
  }

  // the point field of three components named like a displacement, or the active vectors
  private static ResultField? DisplacementOf(ResultDocument document)
  {
    ResultField? vectors = null;
    foreach (var field in document.Fields)
    {
      if (field is not { Location: FieldLocation.Points, Components: 3 })
      {
        continue;
      }

      if (field.Name.StartsWith("disp", StringComparison.OrdinalIgnoreCase)
          || field.Name.Equals("U", StringComparison.Ordinal))
      {
        return field;
      }

      vectors ??= field;
    }

    return vectors;
  }

  private static double AutoScale(ResultDocument document, ResultField? displacement,
                                  double diagonal)
  {
    if (displacement is null)
    {
      return 1;
    }

    using var points = document.Grid.GetPointData();
    using var array = points.GetArray(displacement.Name);
    var largest = array.GetRange(-1)[1];
    return largest > 0 ? 0.1 * diagonal / largest : 1;
  }

  private static int LongestAxis(double[] bounds)
  {
    var axis = 0;
    for (var i = 1; i < 3; i++)
    {
      if (bounds[2 * i + 1] - bounds[2 * i] > bounds[2 * axis + 1] - bounds[2 * axis])
      {
        axis = i;
      }
    }

    return axis;
  }

  private static double Square(double value) => value * value;
}
