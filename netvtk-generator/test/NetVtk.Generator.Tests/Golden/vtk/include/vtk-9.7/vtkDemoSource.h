// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: VTK's conventions in one class. A size hint (a C# array), a string getter returning char*, a smart
// pointer both ways, a scoped enum at file scope (enum class in the .i), a nested enum a data member of its name hides
// (elaborated in the nested header), an anonymous enum's default (its value), plain data, VTK_WRAPEXCLUDE, a static
// overload taking the object by pointer first (the instance member is shadowed), a setter's sized array (copied), a
// function pointer by its typedef (its calling convention may differ per platform), a third-party template's instance
// (not wrapped).
#pragma once
#include "vtkObject.h"
#include "vtkSmartPointer.h"
#include "vtkdemojson/basic.h"

enum class vtkDemoMode
{
  Fast,
  Exact
};

typedef void (*vtkDemoCallback)(void*);

struct vtkDemoInfo
{
  double Area;
  int Count;
};

class vtkDemoSource : public vtkObject
{
public:
  static vtkDemoSource* New() { return new vtkDemoSource; }
  enum Notation
  {
    Mixed,
    Scientific
  };
  enum
  {
    ISO = 3
  };
  virtual void SetCenter(double, double, double) {}
  virtual void SetCenter(const double center[3]) { (void)center; }
  virtual double* GetCenter() VTK_SIZEHINT(3) { return this->Center; }
  virtual char* GetName() { return this->Name; }
  virtual void SetName(const char*) {}
  vtkSmartPointer<vtkObject> GetOutput() { return vtkSmartPointer<vtkObject>(); }
  void SetInput(vtkSmartPointer<vtkObject>) {}
  vtkObject* GetInput() { return nullptr; }
  void SetMode(vtkDemoMode) {}
  void SetNotation(int value) { this->Notation = value; }
  int GetNotation() { return this->Notation; }
  vtkDemoInfo GetInfo() const { return vtkDemoInfo(); }
  void Internal() VTK_WRAPEXCLUDE {}
  void Render(vtkDemoSource*) {}
  static void Render(vtkDemoSource*, vtkDemoSource*) {}
  void Format(int = ISO) {}
  void SetCallback(vtkDemoCallback, void*) {}
  vtkdemojson::basic<double> GetState() { return vtkdemojson::basic<double>(); }

protected:
  vtkDemoSource() : Name(nullptr), Notation(0) {}
  ~vtkDemoSource() override {}
  double Center[3] = { 0, 0, 0 };
  char* Name;
  int Notation;
};
