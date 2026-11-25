# Wrapping gaps

What netvtk-gen leaves out, logged in `netvtk-generator/log/skips/<Module>.txt`:

- **Vectors of other elements:** of vectors, pairs or pointers to classes that aren't VTK objects (`vtkOverlappingAMRMetaData::GetChildrenAtLevel`, `vtkOpenGLGlyph3DHelper::SetLODs`, three value classes' fields).
- **Value classes' fields of maps and sets:** three.
- **Size hints that call members** (`VTK_SIZEHINT(3 * GetNumberOfPoints())` on `vtkCell::GetParametricCoords`): such returns stay addresses, and aren't logged.
