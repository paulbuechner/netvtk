// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NetVtkRuntime: what every wrapper library shares. The number of the exception a C# override threw
// in a callback (src/SWIG_files/common/Directors.i), per thread: the callback's module and the
// module of the call that led to it differ, and each wrapper library has its own statics. And a
// memory copy for the bulk copies of VTK's arrays (Vtk.DataArrays), on every .NET.

#ifndef NetVtkRuntime_HeaderFile
#define NetVtkRuntime_HeaderFile

#include <cstddef>

#if defined(_WIN32)
  #if defined(NETVTK_RUNTIME_BUILD)
    #define NETVTK_RUNTIME_EXPORT __declspec(dllexport)
  #else
    #define NETVTK_RUNTIME_EXPORT __declspec(dllimport)
  #endif
#else
  #define NETVTK_RUNTIME_EXPORT __attribute__((visibility("default")))
#endif

extern "C"
{
  // what a C# override threw, parked in Vtk.Directors under theNumber; the first one counts until
  // it's taken
  NETVTK_RUNTIME_EXPORT void NetVtk_DirectorFailed(long long theNumber);

  // the number of the exception a C# override threw on this thread since the last call, or 0;
  // clears it
  NETVTK_RUNTIME_EXPORT long long NetVtk_TakeDirectorFailure();

  // copies theSize bytes from theSource to theTarget, which don't overlap
  NETVTK_RUNTIME_EXPORT void NetVtk_CopyMemory(void*       theTarget,
                                               const void* theSource,
                                               size_t      theSize);
}

#endif
