// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * RenderingCore's companion (hand-written; the generated module includes it): the modules whose object factories
 * override RenderingCore's classes, registered when its library loads, as VTK's autoinit does for an application
 * (vtkRenderWindow::New() returns the platform's OpenGL window, vtkRenderWindowInteractor::New() its interactor).
 */

%{
#include <vtkAutoInit.h>
VTK_MODULE_INIT(vtkRenderingOpenGL2);
VTK_MODULE_INIT(vtkInteractionStyle);
VTK_MODULE_INIT(vtkRenderingFreeType);
VTK_MODULE_INIT(vtkRenderingUI);
%}
