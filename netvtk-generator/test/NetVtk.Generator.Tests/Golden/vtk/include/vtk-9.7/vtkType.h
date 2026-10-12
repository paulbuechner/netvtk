// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: VTK's width typedefs (64-bit ids), which the .i spells by name.
#pragma once
typedef long long vtkTypeInt64;
typedef unsigned long long vtkTypeUInt64;
typedef vtkTypeInt64 vtkIdType;
typedef vtkTypeUInt64 vtkMTimeType;
typedef int vtkTypeBool;
