// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Vtk;

// C# handlers as VTK observers, and C# events over them
public partial class vtkObject
{
  // the handlers the events of this proxy added, with their observers' tags
  private List<Subscription> _subscriptions;

  /// <summary>
  /// Calls <paramref name="handler"/> when the object invokes <paramref name="eventId"/>, on the
  /// thread that invokes it; observers of higher <paramref name="priority"/> come first. VTK holds
  /// the handler until <see cref="RemoveObserver(ulong)"/> takes the returned tag, or the object
  /// goes away. An exception the handler throws comes out of the call that invoked the event, as a
  /// <see cref="VtkException"/> carrying it.
  /// </summary>
  public ulong AddObserver(ulong eventId, EventHandler<VtkEventArgs> handler, float priority = 0f)
  {
    if (handler is null)
    {
      throw new ArgumentNullException(nameof(handler));
    }

    // VTK holds the command: its proxy goes, and it lives while VTK holds it
    using var command = new DelegateCommand(handler);
    return AddObserver(eventId, command, priority);
  }

  /// <inheritdoc cref="AddObserver(ulong, EventHandler{VtkEventArgs}, float)"/>
  public ulong AddObserver(vtkCommand_EventIds eventId, EventHandler<VtkEventArgs> handler,
                           float priority = 0f) =>
    AddObserver((ulong)eventId, handler, priority);

  /// <summary>
  /// Calls <paramref name="handler"/> when the object invokes <paramref name="eventId"/>, until the
  /// returned object is disposed (as <see cref="AddObserver(ulong, EventHandler{VtkEventArgs},
  /// float)"/>).
  /// </summary>
  public IDisposable Observe(vtkCommand_EventIds eventId, EventHandler<VtkEventArgs> handler,
                             float priority = 0f) =>
    new Observation(this, AddObserver((ulong)eventId, handler, priority));

  /// <summary>
  /// The object changed (<c>ModifiedEvent</c>). Remove a handler through the proxy that added it.
  /// </summary>
  public event EventHandler<VtkEventArgs> ModifiedEvent
  {
    add => Subscribe(vtkCommand_EventIds.ModifiedEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.ModifiedEvent, value);
  }

  /// <summary>
  /// The object reports an error (<c>ErrorEvent</c>, the message in
  /// <see cref="VtkEventArgs.GetText"/>), which then doesn't reach VTK's output window.
  /// </summary>
  public event EventHandler<VtkEventArgs> ErrorEvent
  {
    add => Subscribe(vtkCommand_EventIds.ErrorEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.ErrorEvent, value);
  }

  /// <summary>
  /// The object reports a warning (<c>WarningEvent</c>, the message in
  /// <see cref="VtkEventArgs.GetText"/>), which then doesn't reach VTK's output window.
  /// </summary>
  public event EventHandler<VtkEventArgs> WarningEvent
  {
    add => Subscribe(vtkCommand_EventIds.WarningEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.WarningEvent, value);
  }

  internal void Subscribe(vtkCommand_EventIds eventId, EventHandler<VtkEventArgs> handler)
  {
    if (handler is null)
    {
      return;
    }

    var tag = AddObserver((ulong)eventId, handler);
    lock (this)
    {
      (_subscriptions ??= []).Add(new Subscription(eventId, handler, tag));
    }
  }

  internal void Unsubscribe(vtkCommand_EventIds eventId, EventHandler<VtkEventArgs> handler)
  {
    Subscription found = null;
    lock (this)
    {
      // the last one added, as removing from a multicast delegate does
      var index = _subscriptions?.FindLastIndex(s => s.EventId == eventId && s.Handler == handler)
                  ?? -1;
      if (index >= 0)
      {
        found = _subscriptions[index];
        _subscriptions.RemoveAt(index);
      }
    }

    if (found is not null)
    {
      RemoveObserver(found.Tag);
    }
  }

  private sealed class Subscription(vtkCommand_EventIds eventId, EventHandler<VtkEventArgs> handler,
                                    ulong tag)
  {
    public vtkCommand_EventIds EventId { get; } = eventId;

    public EventHandler<VtkEventArgs> Handler { get; } = handler;

    public ulong Tag { get; } = tag;
  }
}
