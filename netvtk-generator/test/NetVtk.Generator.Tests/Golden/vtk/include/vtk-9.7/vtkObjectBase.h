// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: the root of VTK's objects, which count their references: the proxies own one (VtkObjects.i), so the
// counting members stay unwrapped (exclude_methods here, covered in the classes below it).
#pragma once
#include "vtkType.h"

class vtkObjectBase
{
public:
  const char* GetClassName() const { return "vtkObjectBase"; }
  virtual vtkTypeBool IsA(const char*) { return 0; }
  virtual void Delete() {}
  virtual void Register(vtkObjectBase*) {}
  virtual void UnRegister(vtkObjectBase*) {}
  int GetReferenceCount() { return this->ReferenceCount; }

protected:
  vtkObjectBase() : ReferenceCount(1) {}
  virtual ~vtkObjectBase() {}
  int ReferenceCount;
};
