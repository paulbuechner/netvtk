// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * InteractionStyle's companion (hand-written; the generated module includes it). NetVtkInteraction's classes make
 * RenderingCore's objects in their constructors (an interactor style its outline's mapper, a widget representation its
 * actors, mappers and properties), maybe before NetVtkRendering loads: the library registers the object factories
 * that override them when it loads, as NetVtkRendering does (RenderingCore's companion). Once for the library, which
 * holds InteractionWidgets too.
 */

%netvtk_rendering_factories
