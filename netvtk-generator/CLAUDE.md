# netvtk-generator

`netvtk-gen` (C#, net10.0): reads VTK's install and writes netvtk-core's SWIG modules. Monorepo rules: `../CLAUDE.md`; the wrapper contract: `../netvtk-core/CLAUDE.md`.

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

## Pipeline

`Program.cs` dispatches the commands (`CommandLine.cs`), `Workspace.cs` loads the config and VTK's module list, `Generate.cs` runs the phases:

1. **Parse** (`Parsing/PackageParser*.cs`): libclang, one translation unit per module, function bodies included; `.Collector.cs` turns declarations into the model (`Model/CppModel.cs`), `.Links.cs` decides what links, `.Traits.cs` what a class allows (copies, construction, destruction), `.Types.cs` types and names. `LibraryExports.cs` reads the DLLs' export tables.
2. **Map** (`Mapping/SignatureMapper*.cs`, `TypeRegistry*.cs`): each type to its C++ spelling and C# type, or a skip reason; the registry knows every type a module may use, and scans the `.i` files a run doesn't write.
3. **Write** (`Emit/InterfaceWriter*.cs`, `ModuleHeaders.cs`, `ValueTypeWriter.cs`): `<Module>.i` and `<Module>_module.hxx`, in passes (uses, imports, text). Order in the `.i`: imports (the closure, base modules first: SWIG drops a base it meets later), typemap macros, enums, the companion (`extras/<Module>.i`), template instances, classes. `ClassRules.cs` decides what a class becomes, `MemberRules.cs` the checks every member passes, `Overloads.cs` which of the overloads that meet in C# it gets.

Skipped members go to `log/skips/<Module>.txt` with a reason; covered ones (a twin C# calls instead, the proxy's own members) aren't logged.

## Rules

- Emit a member only if every type in it maps; otherwise skip it and log why.
- **Overloads that meet in C#** are one call (`Overloads.Claim`): the first declared, unless a twin is preferred; a member whose shorter default overloads are claimed keeps its defaults past them. A `T&&` twin of a `const T&` overload, and a mutable twin of a const member, are covered.
- **Ambiguous calls:** an arity another overload also takes (the same first arguments, the rest defaulted) is left out, as C++ can't make it either; the member keeps its other arities.
- **Default arguments** go into the `.i` as written; one spelled in a macro is its constant's value when it's a number, bool or C string, else dropped with the defaults before it (`MemberRules.Initializers`).
- **Links:** a member is callable when it's pure virtual, in the export tables, a template instance, or defined in a header with calls that link themselves. An inline constructor of a polymorphic class makes MSVC emit the vtable, which names every virtual: one that doesn't link rules out that constructor. Destructors must link, or the class gets `%nodefaultdtor` and no constructors.
- **Kept arguments:** a class argument by reference or pointer that the class refers to (a pointer or reference field of its own or a base) is kept alive with the proxy (`NativeKeep`), except VTK objects, whose reference counts do it; a pointer to numbers a constructor or `Set*` keeps (`MemberRules.MayKeep`) is an address, not a pinned array.
- **Views:** a returned value that refers to the object it came from keeps that object alive (`ViewPattern`).
- **Shadowed members:** an instance member next to a static overload taking the object first is skipped: SWIG can't tell them apart.
- **Accessors:** a return SWIG can't declare goes through an `%extend` member (`InterfaceWriter.Accessor`).
- **Value types** (`value_types` in the config) are C# structs, `src/NetVtk/<Module>/<Type>.g.cs` beside a hand-written partial, whose members the generator leaves out (`Emit/HandWritten.cs`, Roslyn).
- **Nested types** get flat names, the scopes joined by `_`, declared as C++ aliases in `headers/<Module>_nested.hxx`.
- **Output:** deterministic and declarations-only (stable order, no timestamps); no VTK doc text in committed files or golden fixtures. Never copy code or lists from ActiViz or other VTK bindings; VTK's wrapping tools (BSD-3) may guide rules.
- **C#:** as netvtk-core's (`../netvtk-core/CLAUDE.md`, Rules); dependencies ClangSharp with the libclang runtime packages, YamlDotNet, Roslyn, nothing else without asking. Tests: synthetic models for the mapper and writer, libclang on temporary headers for the parser, and the golden run below; none needs VTK.

## VTK's rules

- **`docs`** (`Docs.cs`, `Parsing/PackageParser.Docs.cs`, `Parsing/DocComments.cs`): VTK's Doxygen comments as NetVtk's XML documentation file, matched to the assembly's members by type (classes.json), name and parameter names; netvtk-core's `build.py docs` runs it. VTK's layout needs two rules: a class's comment sits at the top of its header, before the include guard (`/** @class vtkPolyData @brief ... */`), so each `@class` block is parsed again as the comment of a stand-in declaration (`ClassBlocks`); and a comment documents a group of members (`///@{` ... `///@}`), which libclang attaches to the first only, so the others get it, told by the markers in the text between declarations (`RecordDocs`). The markers and verbatim lines (`@class`, `@fn`) aren't text. The output is VTK's text: never committed.

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
- **Golden fixture:** `test/NetVtk.Generator.Tests/Golden/vtk` is a fake VTK install (headers, module properties, no DLLs) with modules Core and Demo; each header's comment says which rule it covers.
