// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace Vtk;

// any array's values, whatever it stores
public partial class vtkDataArray
{
  /// <summary>
  /// Every component of every tuple, converted to double, whatever the array stores (a typed
  /// array, separate components, values computed on the fly). A typed array's own
  /// <c>ToArray()</c> copies without converting.
  /// </summary>
  public double[] ToDoubleArray()
  {
    var values = new double[DataArrays.Length(GetNumberOfValues())];
    NetVtk_CopyValues(values);
    return values;
  }
}
