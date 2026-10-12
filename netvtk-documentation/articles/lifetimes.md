# Object lifetimes

VTK counts references and frees an object when the last one goes. A C# proxy holds one; `Dispose` drops it at once, the finalizer once the garbage collector finds the proxy unreachable.

[!code-csharp[](../samples/Lifetimes.cs#references)]

`GetReferenceCount()` counts VTK's own references too: a new `vtkSphereSource` reports 2.

[!code-csharp[](../samples/Lifetimes.cs#using)]

## What VTK keeps

What VTK holds stays alive, whatever happens to its proxies:

[!code-csharp[](../samples/Lifetimes.cs#kept)]

The same goes for a filter's input and output, a renderer's actors, and an observer VTK holds, a C# `vtkCommand` included ([Observers and messages](observers.md)). A proxy isn't the object: `GetMapper()` twice gives two proxies, each with a reference; `NativePointer` tells whether they're the same object.

## When to dispose

The garbage collector sees only the small proxy, not the native memory behind it.

| Dispose promptly | Why |
|---|---|
| render windows, renderers, mappers, actors, textures | they release OpenGL resources, which needs the thread that renders |
| large data sets and arrays | the memory goes only with the last reference |
| readers and filters | each holds its output |
| small objects | no need: the finalizer is fine |

## Threads

The finalizer releases on its own thread, where no OpenGL context is current: fine for data, not for rendering objects. Dispose those on the thread that renders, or set `VtkFinalizer.Context` to its `SynchronizationContext`: the finalizer then posts the releases there ([Rendering and hosting](rendering.md#the-rendering-thread)). `Dispose` releases on the calling thread. A release posted to a context whose thread has ended doesn't happen.
