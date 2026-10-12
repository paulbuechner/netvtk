// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Vtk;

/// <summary>
/// Where the finalizer releases the VTK objects whose proxies weren't disposed. By default it
/// releases them on its own thread, which is fine for data, but a rendering object (a render
/// window, renderer, mapper, actor, texture) frees OpenGL resources when it goes, which needs the
/// thread that renders. An application that renders sets <see cref="Context"/> to that thread's
/// context; Dispose releases at once, on the caller's thread.
/// </summary>
public static class VtkFinalizer
{
  private static volatile SynchronizationContext _context;

  /// <summary>
  /// The context the finalizer posts releases to (a UI thread's:
  /// <c>SynchronizationContext.Current</c> there), or null to release on the finalizer thread.
  /// Releases posted to a context whose thread has ended don't happen.
  /// </summary>
  public static SynchronizationContext Context
  {
    get => _context;
    set => _context = value;
  }

  // the finalizer of a proxy (VtkObjects.i): true when the release went to the context
  internal static bool Defer(vtkObjectBase proxy)
  {
    var context = _context;
    if (context is null)
    {
      return false;
    }

    try
    {
      context.Post(static state => ((vtkObjectBase)state).ReleaseFinalized(), proxy);
      return true;
    }
    catch (Exception)
    {
      // a context that no longer takes posts: release here, as without one
      return false;
    }
  }
}
