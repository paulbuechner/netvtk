// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: one platform's header that a module of its own takes (DemoWin32: vtk_module Demo, its headers), though
// exclude_headers matches it too: a class deriving from another module's class (that module imported), an opaque
// pointer (an address both ways; its void* twin covered).
#pragma once
#include "vtkDemoSource.h"

struct ID3DDemoTexture;

class vtkWin32DemoRenderWindow : public vtkDemoSource
{
public:
  static vtkWin32DemoRenderWindow* New() { return new vtkWin32DemoRenderWindow; }
  void BlitToTexture(ID3DDemoTexture* color, ID3DDemoTexture* depth = nullptr)
  {
    (void)color;
    (void)depth;
  }
  void BlitToTexture(void* color, void* depth = nullptr)
  {
    this->BlitToTexture(static_cast<ID3DDemoTexture*>(color), static_cast<ID3DDemoTexture*>(depth));
  }
  ID3DDemoTexture* GetSharedTexture() { return nullptr; }

protected:
  vtkWin32DemoRenderWindow() = default;
  ~vtkWin32DemoRenderWindow() override = default;
};
