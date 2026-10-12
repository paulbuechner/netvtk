// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NetVtk's common SWIG setup, the first %include of every module .i. One SWIG module per VTK module, all in the C#
 * namespace Vtk; VTK's class and method names unchanged.
 */

%begin %{
#if defined(_MSC_VER)
// conversions in SWIG's wrappers (4244, 4267); VTK's exported classes with members or bases of other DLLs (4251, 4275)
#pragma warning(disable : 4244 4267 4251 4275)
#endif
%}

%{
#include <exception>
#include <functional>
#include <iterator>
#include <type_traits>

// The buffer a wrapper returns a value's address in, per thread, type and position (0: the return, n: argument n): C#
// reads it before its next native call, so one per type serves every wrapper of a library.
template <class T, int Position = 0>
inline T& NetVtk_Slot()
{
  static thread_local T theSlot;
  return theSlot;
}
%}

// Constructors are wrapped only when declared explicitly: VTK creates its objects through New() (%vtk_new).
%nodefaultctor;

// Proxies are partial: src/NetVtk/<Module>/<Class>.cs adds hand-written members (vtkObject's C# observers).
%typemap(csclassmodifiers) SWIGTYPE "public partial class"

// Module and intermediary classes are plumbing; the API is the proxy classes.
%pragma(csharp) moduleclassmodifiers="internal class"
%pragma(csharp) imclassclassmodifiers="internal class"

%include "Types.i"
%include "References.i"
%include "Exceptions.i"
%include "Directors.i"
%include "Strings.i"
%include "ValueTypes.i"
%include "Std.i"
%include "Streams.i"
%include "VtkObjects.i"
