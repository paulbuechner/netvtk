# netvtk-generator

`netvtk-gen` (C#, net10.0): reads VTK's install and writes netvtk-core's SWIG modules. Forked from `../../netocc/netocc-generator` (same author, MIT): its `CLAUDE.md` explains the pipeline and every rule this fork kept (overloads, links, vtables, keeps, views, directors, value types, nested names, skips). This file holds what differs. Monorepo rules: `../CLAUDE.md`; the wrapper contract: `../netvtk-core/CLAUDE.md`.

## Commands

```bash
dotnet build NetVtk.Generator.slnx -c Release
dotnet test NetVtk.Generator.slnx -c Release            # unit + golden tests, no VTK needed
NETVTK_UPDATE_GOLDEN=1 dotnet test NetVtk.Generator.slnx -c Release   # rewrite Golden/expected, then review the diff

# VTK's install prefix: include/vtk-9.7, share/vtk (module properties), bin (DLLs for the export check); Windows
VTK=../netvtk-core/.vcpkg/installed-x64-windows/x64-windows
dotnet run --project src/NetVtk.Generator -c Release -- generate --vtk $VTK --core ../netvtk-core           # about 1 min
dotnet run --project src/NetVtk.Generator -c Release -- generate --vtk $VTK --core ../netvtk-core --check   # exit 1 if out of date
dotnet run --project src/NetVtk.Generator -c Release -- dump --vtk $VTK CommonCore                          # what the parser sees
```

Then in netvtk-core: `build.py generate`, `native` (x64 and x86), `test`. Skip logs: `log/skips/<Module>.txt`, reviewed as a diff after each generate.

## What differs from netocc-gen

- **Modules, not packages:** `config/modules.yaml` lists the native libraries (`NetVtk<Library>`) and their VTK modules in build order. A module's headers and dependencies come from VTK's install (`share/vtk/VTK-vtk-module-properties.cmake`, `Parsing/VtkSource.cs`). One SWIG module per VTK module, all in the C# namespace `Vtk`.
- **libclang setup:** on Windows the MSVC environment (`INCLUDE`) comes from vswhere and vcvarsall (`Parsing/MsvcEnvironment.cs`); the third-party include directories (`vtk-9.7/vtkscn/include`, ...) are added.
- **Wrapping hints** are clang annotations: the parser defines `VTK_SIZEHINT`, `VTK_NEWINSTANCE`, `VTK_WRAPEXCLUDE` and the others as `annotate` attributes, not `__VTK_WRAP__` (which changes what headers declare). `MethodModel.Hints`:
  - `vtk::sizehint(N)`: a returned `T*` is a C# array of N (`%netvtk_sized_return`, `%apply`/`%clear` around the member);
  - `vtk::newinstance`: owned return;
  - `vtk::wrapexclude`: left out, not logged. On a class, the class is left out and logged.
- **VTK objects** (`vtkObjectBase` subclasses, `WrapKind.Transient`): `%vtk_object(T)`, and `%vtk_new(T)` (a C# constructor) when the class isn't abstract, has no public constructor and has a static `New()`. `vtkSmartPointer<T>` is the object. The reference-count members are covered by the proxy (`RefCountMembers`, `exclude_methods`).
- **Template instances** a signature uses (`vtkAOSDataArrayTemplate<double>`, `vtkGenericDataArray<...>`) are classes named by their arguments (`vtkAOSDataArrayTemplate_double`), declared in the module's nested header; the classes deriving from them derive from them in C# too. A `Superclass` typedef never names one.
  - Out: instances over `size_t`, `long`, `unsigned long`, `wchar_t` (spelled or sized per platform); `long long` is in (VTK's 64-bit types are `long long` everywhere).
  - Out: templates of VTK's third-party copies (`vtknlohmannjson/`, `vtkfmt/`: any `vtk*/` directory below the include directory).
- **Nested enums:** unscoped ones get an elaborated alias (`using vtkCommand_EventIds = enum vtkCommand::EventIds;`), since VTK hides some enum names behind data members; scoped ones stay `enum class`. Only definitions count, not forward declarations.
- **Width typedefs** keep their names in declarations: `vtkIdType`, `vtkMTimeType`, `vtkTypeInt64` and the other `vtkType*` ones by value too (`SignatureMapper.ValueSpelling`: a director's override must declare what VTK declares, and `vtkMTimeType` is `unsigned long` on Linux); `size_t` and `<cstdint>`'s in pointers and references (`PackageParser.Types.WidthTypedefs`). Function pointer typedefs too (`vtkThreadFunctionType` is `__stdcall` on win-x86, `void* (*)(void*)` on Linux): `PointerType.Written`.
- **Strings:** a `char*` getter returns a C# string.
- **`unsigned long`** (event ids, observer tags) maps to `ulong`, in signatures and directors.
- **Links:** members are checked against the export tables of `bin/vtk*.dll`. Compiler builtins in inline bodies (`__assume`, `__builtin_expect`) link by themselves.
- **Directors:** `directors` in `modules.yaml` (`vtkCommand`, `vtkOutputWindow`); VTK objects only. A director gets no `%vtk_new`: its protected constructor is the C# subclasses'.
- **Gone:** OCCT's collections and their aliases, range-for enumerators, legacy `Handle_T` classes, window handles, TCollection strings and GUIDs, `std::pair`.
- **Golden fixture:** `test/NetVtk.Generator.Tests/Golden/vtk` is a fake VTK install (headers, module properties, no DLLs) with modules Core and Demo; each header's comment says which rule it covers.

## Rules

As netocc-gen's (deterministic declarations-only output, no doc text in committed files, C# style, tests), with VTK in place of OCCT. VTK's wrapping tools (BSD-3) may guide rules; never copy code or lists from ActiViz or other VTK bindings.
