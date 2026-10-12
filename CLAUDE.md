# netvtk

Monorepo of NetVtk, the SWIG-generated C# bindings for VTK 9.7.1. netvtk-core and netvtk-generator have their own `CLAUDE.md`; read it before working there. The demos are below. The roadmap, decisions and risks are in `PLAN.md`: keep it current as phases finish, and move what becomes fact into the `CLAUDE.md` files.

Sibling: `../netocc` (NetOcc, OCCT's bindings). This repo follows its conventions and starts from its code (same author, MIT); read `../netocc/CLAUDE.md` and its folders' `CLAUDE.md` before building something NetOcc already has (build.py, typemaps, generator, workflows, release). `NetOcc.Vtk` (OCCT's TKIVtk on NetVtk) lives in `../netocc/netocc-vtk`, not here.

| Folder | Content |
|---|---|
| `netvtk-core/` | VTK's vcpkg port, typemap library, generated `.i` files, native shims, the `NetVtk` assembly, tests, NuGet packs (`build.py`) |
| `netvtk-generator/` | `netvtk-gen`: reads VTK's install, writes `netvtk-core/src/SWIG_files` and the value-type structs |
| `netvtk-documentation/` | DocFX site for GitHub Pages |
| `netvtk-demos/` | WPF and Avalonia 12 viewers on the packed NetVtk |

- **Folders refer to each other by relative paths** (`--core ../netvtk-core`), so each builds on its own; netvtk-core builds without the generator.
- **Clean-room:** never look at or copy ActiViz (proprietary) or any other VTK or OCCT binding's code or generated output. VTK's own sources and wrapping tools (BSD-3) may guide rules. Analysis Situs (`../AnalysisSitus`, BSD-3) is a reference for CAE visualization pipelines; no copies.
- **License:** MIT. VTK and its dependencies are BSD-3 and others: their notices go into the runtime packages, as NetOcc's do.
- **Code style:** as netocc's (`.editorconfig`, `python format.py` for the hand-written C#, clang-format 20.1.8 for `netvtk-core/src/Native`). Source files (`.cs`, `.i`, `.hxx`, `.cxx`, `.py`) start with `SPDX-FileCopyrightText: 2026 Paul Büchner` and `SPDX-License-Identifier: MIT`.
- **Versions** are `<VTK version>.<revision>` (`9.7.1.1`), as NetOcc's; `build.py` computes and checks them.
- **Logs** go into each folder's gitignored `log/`.
- **Workflows** (`.github/workflows/`, from netocc's): every push to main or next runs them but `release.yml` (entries only) and `changelog.yml` (pull requests only); pull requests are filtered to each folder's paths.
  - `build.yml`: netvtk-core on four RIDs (VTK from vcpkg's binary cache, `.github/actions/vtk`; SWIG, `.github/actions/swig`), the tests (the rendering ones on Linux only, under Xvfb), pack, package tests, the demos and the documentation's samples on the packages (Windows), `build.py registry --check`. A push to main whose `CHANGELOG.md` has a release without its tag publishes it (nuget.org, the GitHub release that tags the commit); on next, a prerelease so. Keep the file name: nuget.org's Trusted Publishing policy names it.
  - `release.yml`: the release PR "Release NetVtk <version>" from the entries in `netvtk-core/changes/`, as netocc's.
  - `changelog.yml`, `generator.yml` (netvtk-gen's tests on three OSes), `format.yml` (`format.py --check`, clang-format 20.1.8), `docs.yml` (the site, warnings as errors; main deploys it to GitHub Pages).
  - Third-party actions with write or OIDC permissions are pinned by commit SHA; `.github/dependabot.yml` updates all actions monthly.

## netvtk-documentation

```bash
cd netvtk-documentation
dotnet tool restore                 # docfx, pinned in dotnet-tools.json
python classes.py                   # classes/*.md from netvtk-core's modules.json and classes.json (gitignored)
python classes.py --check           # the same, then every link fetched: fails on a page VTK's reference lacks
dotnet test samples -c Release      # the examples, on netvtk-core's packed NetVtk (build.py pack first)
dotnet docfx docfx.json --warningsAsErrors --serve   # _site/, http://localhost:8080
```

- **Content:** `index.md` (a whole program, `samples/Programs/Contours.cs`, its region `program`) and `articles/`, sectioned by `articles/toc.yml` (the files stay flat, so URLs don't move): Guide (introduction, getting started, pipelines, data and arrays, results, rendering and hosting, observers and messages), Concepts (C++ to C#, lifetimes, native interop), Reference (platforms, modules, building, troubleshooting), Development (design decisions, licensing). `template/public/` is NetOcc's (black dark theme, tabbed code; its diagram rules and lightbox wait for diagrams, none yet). No generated API reference and no VTK doc text: the class index links VTK's reference.
- **Examples are tests:** every C# example is a `#region` of an NUnit test in `samples/` (net10.0, on the packed NetVtk like the demos: `nuget.config`, `NetVtk.version.props`), which articles include with `[!code-csharp[](../samples/<File>.cs#<region>)]` (docfx dedents it); asserts after the region check what the article says. Say only what a sample or a check showed.
  - Code that needs a window compiles only: classes and methods outside `[Test]` (`Rendering.cs`'s `HostedView`).
  - Offscreen renders are `[Category("Rendering")]`: CI runs the samples with `TestCategory!=Rendering` (no GPU).
  - `TestSetUp` installs `VtkOutput`, `Files` gives a test a temporary current directory, `Beam` writes the results article's `result.vtu`.
- **READMEs** repeat samples, which they can't include: the repository's `README.md` shows `GettingStarted.cs`'s region `usings` and `Results.cs`'s `read`, the package's (`netvtk-core/src/NetVtk/README.md`) `usings` and `cone`; `Readmes` fails when they drift.
- **Class index:** `classes.py` links every type to VTK's Doxygen reference, nightly (`vtk.org/doc/nightly/html`, at 9.7: Kitware publishes none for 9.x releases). A template instance links its template's page, a nested enum its scope's, a file-scope enum its header's; Doxygen's file names keep capitals, double `_`, spell `::` as `_1_1` and `.` as `_8`. `NO_PAGE` lists the classes nightly has no page for (2026-10-12: the typed arrays VTK declares from its array templates, which link the template; `vtkType*Array` and nine others, unlinked): rerun `--check` after a generate.

## netvtk-demos

```bash
(cd netvtk-core && python build.py pack --rids win-x64,win-x86)   # the packages, and NetVtk.version.props the demos import
dotnet build netvtk-demos/NetVtk.Demos.slnx -c Release            # both viewers and the tests
dotnet test netvtk-demos/NetVtk.Demos.slnx -c Release --no-build  # the viewer library's tests (one renders off the screen)
netvtk-demos/src/NetVtk.Viewer.Wpf/bin/Release/net10.0-windows/NetVtk.Viewer.Wpf.exe --sample   # or a .vtu/.vtk path
```

- **Layout:** `src/NetVtk.Viewer` (net10.0) holds what both share:
  - `VtkViewer`: render window, renderer, interactor, the scene that shows;
  - `ResultDocument`: a `.vtu` (or legacy `.vtk`) unstructured grid and its fields, read off the UI thread;
  - `ResultScene`: the pipeline, built once: warp by the displacement, surface coloured by a field (a vector by its magnitude) with a scalar bar, a cut plane across the longest axis, contours of a scalar field;
  - `SampleResult`: a cantilever beam with displacement, stress and temperature, written as `.vtu`;
  - `FileOpener` (one open at a time), `ViewerSession` (the document and options, which outlive a viewer), `ViewerWindow` (Win32), `X11ViewerWindow` (Linux).

  `NetVtk.Viewer.Wpf` hosts the window in an `HwndHost`, `NetVtk.Viewer.Avalonia` (12.1.3) in a `NativeControlHost`. `test/NetVtk.Viewer.Tests` covers the parts without a window, the scene off the screen, and on Linux the X11 window in a host's (`X11HostingTests`, Xvfb).
- **Packages:** `Directory.Build.props` imports netvtk-core's `artifacts/packages/NetVtk.version.props`; `nuget.config` maps `NetVtk*` to that folder, the rest to nuget.org. Without a pack, the build stops with a message.
- **Startup:** `VtkViewer.InitializeOnUiThread()` routes VTK's messages to C# (`VtkOutput`, no window of VTK's own) and sets `VtkFinalizer.Context` to the UI thread, where the render windows' OpenGL contexts are.
- **The view's window** (`ViewerWindow`) is a Win32 child of our own class (`CS_OWNDC`); its procedure feeds size, paint and mouse messages to VTK's `vtkGenericRenderWindowInteractor`, VTK's own window procedure stays out. The host disposes the viewer (and its scene) before destroying the window.
- **Linux** (`X11ViewerWindow`): VTK creates the child window in the host's X11 window (`VtkViewer.AttachChild`: `SetDisplayId` with our display connection, `SetParentId`, `Initialize`), so it has the visual OpenGL needs; we select its mouse, expose and structure events on that connection, and the Avalonia host's `DispatcherTimer` (16 ms) reads them (`ProcessEvents`). X grabs the pointer while a button is down; buttons 4 and 5 are the wheel. VTK destroys the window with the render window: dispose the viewer, then close the connection.
- **Avalonia** creates the native control after the window shows, and again after the control moves: `ViewerHost.ViewerCreated` fires after that layout pass; the session shows its document in each new viewer, the command line's file opens in the first. Windows and Linux (X11); macOS would need an NSView child.
- **Never drive the viewers** (no clicks, keystrokes or screenshots): launch them with `--sample` or a file and ask the user to look.
