// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: an observer C# subclasses (a director), its event ids a nested enum (a flat name), a counting member
// redeclared below the root (covered).
#pragma once
#include "vtkObjectBase.h"

class vtkObject;

class vtkCommand : public vtkObjectBase
{
public:
  enum EventIds
  {
    NoEvent = 0,
    AnyEvent,
    ModifiedEvent = 33,
    UserEvent = 1000
  };
  virtual void Execute(vtkObject* caller, unsigned long eventId, void* callData) = 0;
  void UnRegister() {}

protected:
  vtkCommand() {}
  ~vtkCommand() override {}
};
