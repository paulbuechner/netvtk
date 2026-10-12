// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: VTK's smart pointer, the object in C# as a returned pointer is.
#pragma once

template <class T>
class vtkSmartPointer
{
public:
  vtkSmartPointer() : Object(nullptr) {}
  vtkSmartPointer(T* object) : Object(object) {}
  T* Get() const { return Object; }

private:
  T* Object;
};
