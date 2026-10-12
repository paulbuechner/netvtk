// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace Vtk;

/// <summary>
/// A VTK event a C# handler observes (<see cref="vtkObject.AddObserver(ulong,
/// EventHandler{VtkEventArgs}, float)"/>); the sender is the object that invoked it.
/// </summary>
public sealed class VtkEventArgs : EventArgs
{
  internal VtkEventArgs(ulong eventId, IntPtr callData)
  {
    EventId = eventId;
    CallData = callData;
  }

  /// <summary>The event: a <see cref="vtkCommand_EventIds"/> value, or a user event's id.</summary>
  public ulong EventId { get; }

  /// <summary>
  /// The event's data, valid during the call only; what it points to depends on the event:
  /// <c>ProgressEvent</c> a double (<see cref="GetDouble"/>), <c>ErrorEvent</c> and
  /// <c>WarningEvent</c> a message (<see cref="GetText"/>), most others nothing.
  /// </summary>
  public IntPtr CallData { get; }

  /// <summary>The call data as a UTF-8 string, or null without call data.</summary>
  public string GetText() => CallData == IntPtr.Zero ? null : Utf8.Decode(CallData);

  /// <summary>The call data as a double, ProgressEvent's progress from 0 to 1.</summary>
  public unsafe double GetDouble() =>
    CallData == IntPtr.Zero
      ? throw new InvalidOperationException("The event has no call data.")
      : *(double*)CallData;
}

// a C# handler as a VTK observer: VTK holds the command, which holds the handler
[CSharpOverrides]
internal sealed class DelegateCommand(EventHandler<VtkEventArgs> handler) : vtkCommand
{
  public override void Execute(vtkObject caller, ulong eventId, IntPtr callData) =>
    handler(caller, new VtkEventArgs(eventId, callData));
}

// an observer until disposed
internal sealed class Observation(vtkObject subject, ulong tag) : IDisposable
{
  private vtkObject _subject = subject;

  public void Dispose()
  {
    var subject = _subject;
    _subject = null;
    subject?.RemoveObserver(tag);
  }
}
