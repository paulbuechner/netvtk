// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * RenderingCore's companion (hand-written; the generated module includes it): the modules whose object factories
 * override RenderingCore's classes, registered when its library loads, as VTK's autoinit does for an application
 * (vtkRenderWindow::New() returns the platform's OpenGL window, vtkRenderWindowInteractor::New() its interactor).
 *
 * %netvtk_rendering_factories registers them. A library whose classes make RenderingCore's objects in their
 * constructors invokes it too, in its first module's companion (RenderingOpenGL2Win32's, InteractionStyle's): its
 * class may be the first a process makes, before NetVtkRendering loads, and its objects would be VTK's base classes. A
 * macro, not a file they include: SWIG includes a file once per module, and the modules import this one first.
 */

%define %netvtk_rendering_factories
%{
#include <vtkAutoInit.h>
VTK_MODULE_INIT(vtkRenderingOpenGL2);
VTK_MODULE_INIT(vtkInteractionStyle);
VTK_MODULE_INIT(vtkRenderingFreeType);
VTK_MODULE_INIT(vtkRenderingUI);
%}
%enddef

%netvtk_rendering_factories
