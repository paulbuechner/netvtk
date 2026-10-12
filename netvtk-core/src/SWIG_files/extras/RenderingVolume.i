// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * RenderingVolume's companion (hand-written; the generated module includes it): RenderingVolumeOpenGL2's object factory,
 * which overrides RenderingVolume's mappers (vtkGPUVolumeRayCastMapper::New() returns the OpenGL one), registered when
 * its library loads, as RenderingCore's companion registers RenderingOpenGL2.
 */

%{
#include <vtkAutoInit.h>
VTK_MODULE_INIT(vtkRenderingVolumeOpenGL2);
%}
