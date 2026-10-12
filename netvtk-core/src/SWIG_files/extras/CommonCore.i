// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * CommonCore's companion (hand-written; the generated module includes it):
 *   vtkObjectBase     its text (Print) for ToString; a reference for a proxy made from a native pointer
 *                     (src/NetVtk/CommonCore/vtkObjectBase.cs)
 *   vtkDataArray      every array's values as doubles, whatever it stores
 *   the typed arrays  bulk copies and views of the values of vtkAOSDataArrayTemplate<T> (vtkDoubleArray's base and its
 *                     siblings'), and FromArray on the arrays themselves
 */

%{
#include <algorithm>
#include <sstream>

#include <vtkArrayDispatch.h>
#include <vtkDataArrayRange.h>

namespace
{
// a dispatched array's values, converted to double: a typed loop over its memory, no virtual call per value
struct NetVtkCopyValues
{
  template <typename Array>
  void operator()(Array* array, double* values) const
  {
    const auto range = vtk::DataArrayValueRange(array);
    std::copy(range.cbegin(), range.cend(), values);
  }
};
}
%}

%extend vtkObjectBase {
  std::string NetVtk_Print() {
    std::ostringstream text;
    $self->Print(text);
    return text.str();
  }
  static void NetVtk_Register(void* object) { static_cast<vtkObjectBase*>(object)->Register(nullptr); }
}
%csmethodmodifiers vtkObjectBase::NetVtk_Print "internal";
%csmethodmodifiers vtkObjectBase::NetVtk_Register "internal";

%extend vtkDataArray {
  // every component of every tuple, converted: values holds GetNumberOfValues()
  void NetVtk_CopyValues(double* values) {
    // VTK's typed arrays (AOS, SOA) in a loop of their own; the others value by value
    if (vtkArrayDispatch::Dispatch::Execute($self, NetVtkCopyValues{}, values))
    {
      return;
    }
    const int components = $self->GetNumberOfComponents();
    const vtkIdType tuples = $self->GetNumberOfTuples();
    for (vtkIdType t = 0; t < tuples; ++t)
    {
      for (int c = 0; c < components; ++c)
      {
        values[t * components + c] = $self->GetComponent(t, c);
      }
    }
  }
}
%csmethodmodifiers vtkDataArray::NetVtk_CopyValues "internal";

// The values of a vtkAOSDataArrayTemplate<T> instance, one block of memory: copies in and out, and on .NET 6+ a Span over
// them, valid until the array resizes or goes away.
%define %netvtk_value_array(INSTANCE, CSTYPE)
%typemap(cscode) INSTANCE %{
  /// <summary>Every component of every tuple, copied into a new array.</summary>
  public CSTYPE[] ToArray()
  {
    var values = new CSTYPE[global::Vtk.DataArrays.Length(GetNumberOfValues())];
    global::Vtk.DataArrays.CopyOut(GetPointer(0), values);
    return values;
  }

  /// <summary>
  /// Replaces the values with <paramref name="values"/>, tuple after tuple: its length is a multiple of
  /// GetNumberOfComponents().
  /// </summary>
  public void CopyFrom(CSTYPE[] values)
  {
    SetNumberOfValues(global::Vtk.DataArrays.Values(values, GetNumberOfComponents()));
    global::Vtk.DataArrays.CopyIn(values, WritePointer(0, values.Length));
  }

#if NET5_0_OR_GREATER
  /// <summary>
  /// The values themselves, no copy: valid until the array resizes (SetNumberOfValues, Insert..., Squeeze) or is
  /// deleted.
  /// </summary>
  public unsafe global::System.Span<CSTYPE> AsSpan() =>
    new global::System.Span<CSTYPE>((void*)GetPointer(0), global::Vtk.DataArrays.Length(GetNumberOfValues()));

  /// <summary>
  /// Replaces the values with <paramref name="values"/>, tuple after tuple: its length is a multiple of
  /// GetNumberOfComponents().
  /// </summary>
  public void CopyFrom(global::System.ReadOnlySpan<CSTYPE> values)
  {
    SetNumberOfValues(global::Vtk.DataArrays.Values(values.Length, GetNumberOfComponents()));
    values.CopyTo(AsSpan());
  }
#endif
%}
%enddef

// An array class over INSTANCE's values: FromArray.
%define %netvtk_value_array_class(TYPE, CSTYPE)
%typemap(cscode) TYPE %{
  /// <summary>
  /// A new array holding a copy of <paramref name="values"/>, tuple after tuple of
  /// <paramref name="numberOfComponents"/> components.
  /// </summary>
  public static TYPE FromArray(CSTYPE[] values, int numberOfComponents = 1)
  {
    var array = new TYPE();
    array.SetNumberOfComponents(numberOfComponents);
    array.CopyFrom(values);
    return array;
  }
%}
%enddef

%netvtk_value_array(vtkAOSDataArrayTemplate_double, double)
%netvtk_value_array(vtkAOSDataArrayTemplate_float, float)
%netvtk_value_array(vtkAOSDataArrayTemplate_int, int)
%netvtk_value_array(vtkAOSDataArrayTemplate_unsigned_int, uint)
%netvtk_value_array(vtkAOSDataArrayTemplate_short, short)
%netvtk_value_array(vtkAOSDataArrayTemplate_unsigned_short, ushort)
%netvtk_value_array(vtkAOSDataArrayTemplate_char, byte)
%netvtk_value_array(vtkAOSDataArrayTemplate_signed_char, sbyte)
%netvtk_value_array(vtkAOSDataArrayTemplate_unsigned_char, byte)
%netvtk_value_array(vtkAOSDataArrayTemplate_long_long, long)
%netvtk_value_array(vtkAOSDataArrayTemplate_unsigned_long_long, ulong)

%netvtk_value_array_class(vtkDoubleArray, double)
%netvtk_value_array_class(vtkFloatArray, float)
%netvtk_value_array_class(vtkIntArray, int)
%netvtk_value_array_class(vtkUnsignedIntArray, uint)
%netvtk_value_array_class(vtkShortArray, short)
%netvtk_value_array_class(vtkUnsignedShortArray, ushort)
%netvtk_value_array_class(vtkCharArray, byte)
%netvtk_value_array_class(vtkSignedCharArray, sbyte)
%netvtk_value_array_class(vtkUnsignedCharArray, byte)
%netvtk_value_array_class(vtkIdTypeArray, long)
%netvtk_value_array_class(vtkLongLongArray, long)
%netvtk_value_array_class(vtkUnsignedLongLongArray, ulong)
