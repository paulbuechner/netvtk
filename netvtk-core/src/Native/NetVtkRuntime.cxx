// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#include "NetVtkRuntime.hxx"

#include <vtkNetVtkBuild.h>

#include <cstring>

namespace
{
thread_local long long THE_DIRECTOR_FAILURE = 0;
}

void NetVtk_DirectorFailed(long long theNumber)
{
  if (THE_DIRECTOR_FAILURE == 0)
  {
    THE_DIRECTOR_FAILURE = theNumber;
  }
}

long long NetVtk_TakeDirectorFailure()
{
  const long long aNumber = THE_DIRECTOR_FAILURE;
  THE_DIRECTOR_FAILURE    = 0;
  return aNumber;
}

void NetVtk_CopyMemory(void* theTarget, const void* theSource, size_t theSize)
{
  if (theSize != 0)
  {
    std::memcpy(theTarget, theSource, theSize);
  }
}

const char* NetVtk_VtkBuild()
{
  return NETVTK_VTK_BUILD;
}
