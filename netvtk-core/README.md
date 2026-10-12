# netvtk-core

The Visualization Toolkit (VTK) 9.7.1 for .NET: SWIG-generated C# bindings in one namespace, `Vtk`, VTK's names unchanged. One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, .NET 8 and .NET 10; natives for win-x64, win-x86, linux-x64, osx-arm64.

Status: the phase 0 spike modules (34 classes of 7 VTK modules, hand-written); netvtk-generator writes the first module set from phase 2. Roadmap: [PLAN.md](../PLAN.md).

## Install

```bash
dotnet add package NetVtk
```

`NetVtk` pulls in the natives of all four platforms (`NetVtk.runtime.<rid>`, VTK's libraries included). They need:
- **Windows:** the Visual C++ 2015-2022 runtime, x64 or x86.
- **Linux:** glibc 2.35 or newer, an X server or EGL for rendering.
- **macOS:** 14 or newer, on Apple silicon.

## Build

Needs Python 3.10+, vcpkg (`VCPKG_ROOT`), CMake + Ninja, the .NET 10 SDK with the .NET 6 and 8 runtimes, and on Windows Visual Studio with C++ tools.

```bash
python build.py tools                        # SWIG 4.5.1 from conda-forge into .tools/ (micromamba)
python build.py vtk --triplet x64-windows    # VTK 9.7.1 via vcpkg (about 17 min first time; cached after)
python build.py generate                     # SWIG over src/SWIG_files/wrapper/*.i
python build.py native --triplet x64-windows # NetVtk<Group> libraries + VTK's -> artifacts/runtimes/win-x64/native
python build.py test --arch x64              # every framework, net35 on CLR 2; also --arch x86
python build.py pack --rids win-x64,win-x86  # NuGet into artifacts/packages
python build.py test-package --arch x64      # the packed NetVtk as an app consumes it
```

Linux, in Docker (VTK builds once into the named volume):

```bash
docker build -f docker/linux-x64.Dockerfile -t netvtk-linux .
docker run --rm -v netvtk-vcpkg-linux:/src/.vcpkg netvtk-linux
```

## License

MIT ([LICENSE](LICENSE)). VTK and the libraries it builds in: [NOTICE](NOTICE), and `THIRD-PARTY-NOTICES.txt` in each runtime package.
