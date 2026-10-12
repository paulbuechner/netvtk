# Licensing

MIT for what NetVtk writes, BSD-3-Clause for VTK. This isn't legal advice. The repository carries `LICENSE` (MIT) and `netvtk-core/NOTICE`; the `NetVtk` package carries `NOTICE` and `VTK-LICENSE.txt`, each runtime package `THIRD-PARTY-NOTICES.txt`.

| Part | License | Notes |
|---|---|---|
| netvtk-gen, the typemap library, the runtime, the hand-written C#, tests, build scripts | MIT | NetVtk's own code |
| Generated SWIG modules, headers and structs | MIT, committed | declarations only, no VTK doc text |
| SWIG's output (`*_wrap.cxx`, `*.cs`) | build output | not committed |
| The XML documentation (`NetVtk.xml`, IntelliSense) | BSD-3-Clause, VTK's `Copyright.txt` | the doc comments of VTK's headers, made by `build.py docs`; never committed. With it, the package's license expression is `MIT AND BSD-3-Clause` |
| Native libraries (`NetVtk<Library>`, `NetVtkRuntime`) and `NetVtk.dll` | MIT | compiled with VTK's headers, under VTK's BSD-3-Clause |
| VTK's libraries (`vtk*`) | BSD-3-Clause, VTK's `Copyright.txt` | from Kitware's tag v9.7.1, built by `netvtk-core/vcpkg/ports/vtk` |
| The third-party libraries VTK builds in | their own: zlib, libpng, FreeType, Expat, LZ4, LZMA, JsonCpp, pugixml, fmt, glad and others | VTK's own copies, with mangled symbols |
| SWIG | the tool GPL-3.0, its runtime library terms of your choice | the generated output is unrestricted |

`THIRD-PARTY-NOTICES.txt` holds NetVtk's license and NOTICE, VTK's `Copyright.txt`, and the 50 license files VTK installs for its modules and the 27 third-party libraries it builds in.

- **Clean-room:** NetVtk's rules come from VTK's headers and wrapping tools (BSD-3-Clause); no code or lists from ActiViz or other VTK bindings.
- **NetVtk isn't affiliated with Kitware, Inc.**, which develops VTK.
