// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: VTK's conventions in one class. A size hint (a C# array), a string getter returning char*, a smart
// pointer both ways, a scoped enum at file scope (enum class in the .i), a nested enum a data member of its name hides
// (elaborated in the nested header), an anonymous enum's default (its value), plain data, VTK_WRAPEXCLUDE, a static
// overload taking the object by pointer first (the instance member is shadowed), a setter's sized array (copied), a
// function pointer by its typedef (its calling convention may differ per platform), a third-party template's instance
// (not wrapped), a value class's public data (properties: numbers, enums, a C array; a VTK object and a C string read
// only), vectors both ways (a width typedef by its name, strings written back), a pointer a size hint names (a C# array
// of the count after the call).
#pragma once
#include "vtkObject.h"
#include "vtkSmartPointer.h"
#include "demostd/std.h"
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

struct vtkDemoHit
{
  bool Valid = false;
  vtkIdType Cell = -1;
  vtkDemoMode Mode = vtkDemoMode::Fast;
  vtkObject* Object = nullptr;
  double Point[3] = { 0, 0, 0 };
  const char* Label = nullptr;
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
  vtkDemoHit GetHit() const { return vtkDemoHit(); }
  void Internal() VTK_WRAPEXCLUDE {}
  void Render(vtkDemoSource*) {}
  static void Render(vtkDemoSource*, vtkDemoSource*) {}
  void Format(int = ISO) {}
  void SetCallback(vtkDemoCallback, void*) {}
  vtkdemojson::basic<double> GetState() { return vtkdemojson::basic<double>(); }
  void SetWeights(const std::vector<double>&) {}
  std::vector<vtkIdType> GetIds() const { return std::vector<vtkIdType>(); }
  void GetNames(std::vector<std::string>&) const {}
  void GetPoints(vtkIdType& npts, const vtkIdType*& pts) VTK_SIZEHINT(pts, npts)
  {
    npts = 0;
    pts = nullptr;
  }

protected:
  vtkDemoSource() : Name(nullptr), Notation(0) {}
  ~vtkDemoSource() override {}
  double Center[3] = { 0, 0, 0 };
  char* Name;
  int Notation;
};
