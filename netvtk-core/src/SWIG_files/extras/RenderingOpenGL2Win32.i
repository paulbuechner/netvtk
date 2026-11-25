// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * RenderingOpenGL2Win32's companion (hand-written; the generated module includes it).
 *
 * The render windows make RenderingCore's objects in their constructors (a render timer), maybe before NetVtkRendering
 * loads: NetVtkWin32 registers the object factories that override them when it loads, as NetVtkRendering does
 * (RenderingCore's companion), so a window made first in a process gets OpenGL's timer.
 *
 * SetAdapterId takes Windows' LUID, an adapter's 64-bit id (DXGI_ADAPTER_DESC1::AdapterLuid), which C# has no type
 * for: a long here, HighPart in its upper 32 bits and LowPart in its lower ones. The LUID overload is excluded in
 * netvtk-gen's config.
 */

%netvtk_rendering_factories

%extend vtkWin32OpenGLDXRenderWindow {
  void SetAdapterId(long long uid) {
    LUID adapter;
    adapter.LowPart = static_cast<DWORD>(static_cast<unsigned long long>(uid) & 0xFFFFFFFFull);
    adapter.HighPart = static_cast<LONG>(uid >> 32);
    $self->SetAdapterId(adapter);
  }
}
