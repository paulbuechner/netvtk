# Licensing

MIT for what NetVtk writes, BSD-3-Clause for VTK. This isn't legal advice. The repository carries `LICENSE` (MIT) and `netvtk-core/NOTICE`; the `NetVtk` package carries `NOTICE` and `VTK-LICENSE.txt`, each runtime package `THIRD-PARTY-NOTICES.txt` and `sbom/sbom.cdx.json`.

| Part | License | Notes |
|---|---|---|
| netvtk-gen, the typemap library, the runtime, the hand-written C#, tests, build scripts | MIT | NetVtk's own code |
| Generated SWIG modules, headers and structs | MIT, committed | declarations only, no VTK doc text |
| SWIG's output (`*_wrap.cxx`, `*.cs`) | build output | not committed |
| The XML documentation (`NetVtk.xml`, IntelliSense) | BSD-3-Clause, VTK's `Copyright.txt` | the doc comments of VTK's headers, made by `build.py docs`; never committed. With it, the package's license expression is `MIT AND BSD-3-Clause` |
| Native libraries (`NetVtk<Library>`, `NetVtkRuntime`) and `NetVtk.dll` | MIT | compiled with VTK's headers, under VTK's BSD-3-Clause |
| VTK's libraries (`vtk*`) | BSD-3-Clause, VTK's `Copyright.txt` | from Kitware's tag v9.7.1, built by `netvtk-core/vcpkg/ports/vtk` with four [patches](design.md#vtks-patches): two of `vtkWin32OpenGLDXRenderWindow` (its OpenGL context, its interop device), one of `vtkOpenGLHardwareSelector` (the framebuffer its passes render into), one of the render windows and renderers (reverse Z) |
| The third-party libraries VTK builds in | their own: zlib, libpng, FreeType, Expat, LZ4, LZMA, JsonCpp, pugixml, fmt, glad and others | VTK's own copies, with mangled symbols |
| SWIG | the tool GPL-3.0, its runtime library terms of your choice | the generated output is unrestricted |

`THIRD-PARTY-NOTICES.txt` holds NetVtk's license and NOTICE, VTK's `Copyright.txt`, and the 50 license files VTK installs for its modules and the 27 third-party libraries it builds in.

`sbom/sbom.cdx.json` is a CycloneDX 1.6 SBOM of the same, with the data fields of BSI TR-03183-2: every file of the package with its SHA-512, under the library it belongs to: NetVtk's, VTK with the port's patches, or one of the libraries VTK builds in (32 on win-x64, VTK's utilities among them). Each library comes with its creator, version, SPDX license and its texts, copyright and source, as VTK's sources declare them. The package is its root component; what the libraries load from the platform (the C and C++ runtime, the system's libraries, OpenGL) is not listed, and the SBOM says so. A consumer's SBOM tool takes it in under the runtime package, which NuGet lists as one component.

- **Clean-room:** NetVtk's rules come from VTK's headers and wrapping tools (BSD-3-Clause); no code or lists from ActiViz or other VTK bindings.
- **NetVtk isn't affiliated with Kitware, Inc.**, which develops VTK.
