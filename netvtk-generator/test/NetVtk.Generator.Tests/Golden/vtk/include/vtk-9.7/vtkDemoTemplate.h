// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: a class template a class derives from (vtkAOSDataArrayTemplate<double> under vtkDoubleArray): the
// instance is a class of its own, named by its arguments, which the class derives from in C#; not named after the type
// macro's Superclass typedef. One over vtkIdType, which is long long on every platform (vtkIdTypeArray's base).
#pragma once
#include "vtkObject.h"

template <class T>
class vtkDemoTemplate : public vtkObject
{
public:
  T GetValue(vtkIdType) const { return T(); }
  void SetValue(vtkIdType, T) {}
};

class vtkDemoArray : public vtkDemoTemplate<double>
{
public:
  typedef vtkDemoTemplate<double> Superclass;
  static vtkDemoArray* New() { return new vtkDemoArray; }

protected:
  vtkDemoArray() {}
  ~vtkDemoArray() override {}
};

class vtkDemoIdArray : public vtkDemoTemplate<vtkIdType>
{
public:
  static vtkDemoIdArray* New() { return new vtkDemoIdArray; }

protected:
  vtkDemoIdArray() {}
  ~vtkDemoIdArray() override {}
};
