// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: VTK's creation (New: a reference of its own, spelled T*; a C# constructor), a new instance
// (VTK_NEWINSTANCE: T* too), a borrowed object (T* const), event ids and tags (C unsigned long), a width typedef.
#pragma once
#include "vtkObjectBase.h"

class vtkCommand;

class vtkObject : public vtkObjectBase
{
public:
  static vtkObject* New() { return new vtkObject; }
  static vtkObject* SafeDownCast(vtkObjectBase* o) { return static_cast<vtkObject*>(o); }
  VTK_NEWINSTANCE vtkObject* NewInstance() const { return New(); }
  virtual void Modified() {}
  virtual vtkMTimeType GetMTime() { return 0; }
  unsigned long AddObserver(unsigned long, vtkCommand*, float = 0.0f) { return 0; }
  void RemoveObserver(unsigned long) {}

protected:
  vtkObject() {}
  ~vtkObject() override {}
};
