// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace Vtk;

// an algorithm's run as C# events
public partial class vtkAlgorithm
{
  /// <summary>
  /// The algorithm starts executing (<c>StartEvent</c>). Remove a handler through the proxy that
  /// added it.
  /// </summary>
  public event EventHandler<VtkEventArgs> StartEvent
  {
    add => Subscribe(vtkCommand_EventIds.StartEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.StartEvent, value);
  }

  /// <summary>
  /// The algorithm progresses (<c>ProgressEvent</c>, from 0 to 1 in
  /// <see cref="VtkEventArgs.GetDouble"/>), on the thread that executes it.
  /// </summary>
  public event EventHandler<VtkEventArgs> ProgressEvent
  {
    add => Subscribe(vtkCommand_EventIds.ProgressEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.ProgressEvent, value);
  }

  /// <summary>The algorithm finished executing (<c>EndEvent</c>).</summary>
  public event EventHandler<VtkEventArgs> EndEvent
  {
    add => Subscribe(vtkCommand_EventIds.EndEvent, value);
    remove => Unsubscribe(vtkCommand_EventIds.EndEvent, value);
  }
}
