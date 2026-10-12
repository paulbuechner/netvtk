# Observers and messages

An object invokes events (`ModifiedEvent`, `ProgressEvent`, `ErrorEvent`, ...), and its observers are called on the thread that invokes them. C# handlers are observers.

## Handlers

[!code-csharp[](../samples/Observers.cs#handler)]

`sender` is the object that invoked the event, `e.EventId` the event, `e.CallData` its data: `e.GetDouble()` for `ProgressEvent`, `e.GetText()` for `ErrorEvent` and `WarningEvent`. VTK holds the handler until it's removed or the object goes.

[!code-csharp[](../samples/Observers.cs#observe)]

## Events

[!code-csharp[](../samples/Observers.cs#events)]

Remove a handler (`-=`) through the proxy that added it.

## vtkCommand subclasses

[!code-csharp[](../samples/Observers.cs#command)]

[!code-csharp[](../samples/Observers.cs#command-use)]

`AddObserver` takes the event id as `ulong` here. VTK keeps a command it holds alive, after its last C# reference too. `caller` is a new proxy, holding a reference: dispose it.

## Messages

VTK writes errors, warnings and text to its output window: on Windows a window of its own, elsewhere the console. `VtkOutput` routes them to C#:

[!code-csharp[](../samples/Observers.cs#messages)]

Adding a handler installs the routing, `VtkOutput.Install()` does it without one: messages then go to `System.Diagnostics.Trace` while no handler is attached. Install it at startup; `Uninstall()` gives VTK its window back.

An object with an `ErrorEvent` handler reports its errors there instead:

[!code-csharp[](../samples/Observers.cs#error-event)]

## Exceptions in callbacks

[!code-csharp[](../samples/Observers.cs#exceptions)]

The same holds for `vtkCommand` overrides and `VtkOutput.Message` handlers. A C++ exception from a VTK call (`std::bad_alloc`, any `std::exception`) arrives as a `VtkException` too, its type in `NativeType`, its message in `NativeMessage`.
