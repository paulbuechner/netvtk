# CI

- **The patched VTK on macOS:** the port's patches (port-version 1) are built and tested on win-x64, win-x86 and linux-x64 (Docker, 2026-10-21), not on osx-arm64: CI builds it on the next push, VTK cold. `0003`, `0004` and `0005` change code every platform builds.
