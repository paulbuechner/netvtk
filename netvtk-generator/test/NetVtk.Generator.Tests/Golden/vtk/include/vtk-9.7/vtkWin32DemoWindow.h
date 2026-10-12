// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: one platform's header, which exclude_headers leaves out (VTK installs the platform's only).
#pragma once
#include "vtkObject.h"

class vtkWin32DemoWindow : public vtkObject
{
};
