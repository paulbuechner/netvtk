# Troubleshooting

## Loading

**`DllNotFoundException` for `NetVtkCommon` or another `NetVtk*` library**

- Windows: install the [Visual C++ 2015-2022 runtime](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) for the process's architecture.
- Check that the platform has natives: win-x64, win-x86, linux-x64, osx-arm64. See [Platforms](platforms.md).
- .NET Framework: the build copies the natives into `x64\` and `x86\` next to the app; check they're there.
- `NetVtkWin32`, inside a `TypeInitializationException`, off Windows: its classes, the Windows render windows, are Windows' only.

**`BadImageFormatException`**

A 32-bit process found 64-bit natives or the reverse. On .NET Framework with AnyCPU, NetVtk picks `x64\` or `x86\` itself: check both folders exist, or build for one platform.

## Messages

**A window with VTK's errors pops up (Windows), or they appear on the console**

That's VTK's output window. Call `VtkOutput.Install()` at startup, or handle `VtkOutput.Message`: [Observers and messages](observers.md#messages).

**A reader's output is empty**

VTK reports a file it can't read as an error, not an exception. Handle the reader's `ErrorEvent`, or `VtkOutput.Message`, and check the output's `GetNumberOfCells()`.

## Rendering

**Rendering fails, or `Render()` draws nothing**

It needs OpenGL 3.2. On a server or a CI runner without a GPU, use Mesa's llvmpipe; on Linux without a display, run under Xvfb (`xvfb-run`). See [Platforms](platforms.md#rendering).

**Rendering objects left to the finalizer**

The finalizer runs on its own thread, where their OpenGL context isn't current, and they release OpenGL resources when they go. Dispose them on the thread that renders, or set `VtkFinalizer.Context`: [Object lifetimes](lifetimes.md#threads).

## Results

**A filter's output doesn't change**

The pipeline runs when asked: call `Update()` on the last filter (a render asks too). Values changed through `AsSpan()` need `Modified()` on the array first.

**A cast to a derived class fails**

C# casts don't reach the C++ object: use `SafeDownCast`, which returns null for an object of another class. See [From C++ to C#](mapping.md#objects).

## Exceptions

**`VtkException` from a call that doesn't throw in C++**

A C# observer, `vtkCommand` override or `VtkOutput.Message` handler threw during that call: its exception is the `InnerException`. See [Observers and messages](observers.md#exceptions-in-callbacks).

## Performance

**Memory grows**

The garbage collector sees only the proxies, not the data behind them. Dispose large data sets, arrays, readers and filters when done: [Object lifetimes](lifetimes.md).

**Loops over points or values are slow**

Each call crosses into native code. Copy arrays in bulk (`ToArray()`, `ToDoubleArray()`, `FromArray()`, `AsSpan()` on .NET 6+): [Data and arrays](data.md#bulk-copies).

Measured on win-x64, .NET 10:

| Operation | Time |
|---|---|
| a wrapped call | 14 ns |
| a proxy for an existing object | 300 ns |
| `new vtkPoints()` and `Dispose()` | 5,000 ns, VTK's own factory and array |
| `CopyFrom`, 10 million doubles | 7 ms |
| `FromArray`, `ToArray`, 10 million doubles | about 45 ms, most of it the fresh memory's page faults |
| summing 10 million doubles through `AsSpan()` | 11 ms |
| `ToDoubleArray()`, 10 million doubles or floats | 18 ms, 12.5 ms |
