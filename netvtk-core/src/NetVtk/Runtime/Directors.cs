// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Vtk;

/// <summary>
/// The objects of director classes (src/SWIG_files/common/Directors.i): C# subclasses whose
/// overrides VTK calls.
/// <list type="bullet">
/// <item>Lifetime: VTK holds an object by its reference count, which the GC doesn't see, and the
/// callbacks reach the C# object. Its GC handle turns strong when the object is passed to VTK,
/// and weak again when a sweep after a full collection finds the proxy's reference the only one
/// left: an object neither C# nor VTK refers to is collected. Disposed while VTK holds it, the
/// object is released once VTK lets go; a finalizer that finds VTK holding it (a reference VTK
/// took through the object's own members) keeps it alive.</item>
/// <item>Identity: VTK handing an object back gives C# the same instance, not a new proxy of the
/// declared class.</item>
/// <item>Exceptions: what an override throws is parked here under a number, which goes to
/// NetVtkRuntime; the callback returns normally (an override with a result returns its default)
/// and VTK goes on, which isn't written to be unwound through. The wrapped call that led to the
/// callback finds the number when it returns and throws a VtkException carrying the parked
/// exception as its InnerException.</item>
/// </list>
/// </summary>
internal static class Directors
{
  private static readonly Dictionary<IntPtr, Entry> Objects = [];

  // how many objects are registered: getCPtr and returns skip the table while there are none
  private static volatile int _live;

  private static readonly Dictionary<long, Exception> Parked = [];
  private static readonly Queue<long> ParkedOrder = new();
  private static long _parkedNumber;

  // exceptions VTK caught itself never come back: the newest are kept
  private const int MaxParked = 64;

  // the object an override returned, until the next one on this thread: the callback's C++ side
  // takes its reference (or copy) right after the callback returns
  [ThreadStatic] private static object _returned;

  internal const string ManagedException = "NetVtk_ManagedException";

  [DllImport(NativeLibraryLoader.RuntimeLibrary, EntryPoint = "NetVtk_DirectorFailed",
             CallingConvention = CallingConvention.Cdecl)]
  private static extern void DirectorFailed(long number);

  /// <summary>A new director object, once SWIG connected its overrides.</summary>
  public static void Register(object director, IntPtr cPtr)
  {
    NativeKeep.Start();
    lock (Objects)
    {
      if (Objects.TryGetValue(cPtr, out var stale))
      {
        stale.Handle.Free();
      }

      Objects[cPtr] = new Entry(director);
      _live = Objects.Count;
    }
  }

  /// <summary>
  /// An object passed to VTK, which may keep a reference (getCPtr of the director classes and
  /// their bases): a director object's handle turns strong.
  /// </summary>
  public static void Passed(IntPtr cPtr)
  {
    if (_live == 0)
    {
      return;
    }

    lock (Objects)
    {
      if (Objects.TryGetValue(cPtr, out var entry) && entry.Handle.Target is { } target)
      {
        entry.Passed = true;
        entry.Strengthen(target);
      }
    }
  }

  /// <summary>
  /// The director object at <paramref name="cPtr"/>, which VTK returned with a reference for a new
  /// proxy, or null: C# gets the object itself, whose proxy owns a reference already, so
  /// <paramref name="release"/> drops the new one.
  /// </summary>
  public static T Adopt<T>(IntPtr cPtr, Action<IntPtr> release) where T : class
  {
    if (_live == 0)
    {
      return null;
    }

    T found;
    lock (Objects)
    {
      found = Objects.TryGetValue(cPtr, out var entry) ? entry.Handle.Target as T : null;
    }

    if (found != null)
    {
      release(cPtr);
    }

    return found;
  }

  /// <summary>
  /// Whether the Dispose of <paramref name="director"/> must keep its reference, VTK holding the
  /// object still: disposed explicitly, it is released once VTK lets go (<see cref="Sweep"/>);
  /// finalized, it lives on. Otherwise the object leaves the table and Dispose releases it.
  /// </summary>
  public static bool Holds(vtkObjectBase director, IntPtr cPtr, bool disposing)
  {
    // the Dispose of every proxy deriving from a director class comes here
    if (_live == 0 || cPtr == IntPtr.Zero)
    {
      return false;
    }

    lock (Objects)
    {
      if (!Objects.TryGetValue(cPtr, out var entry))
      {
        return false;
      }

      if (director.GetReferenceCount() > 1)
      {
        entry.Strengthen(director);
        if (disposing)
        {
          entry.DisposeRequested = true;
        }
        else
        {
          Trace.TraceWarning(
            $"NetVtk: a {director.GetType().Name} VTK still holds lost its last C# reference; it stays alive. Pass a C# object to VTK as an argument, or keep a reference to it.");
          global::System.GC.ReRegisterForFinalize(director);
        }

        return true;
      }

      entry.Handle.Free();
      Objects.Remove(cPtr);
      _live = Objects.Count;
      return false;
    }
  }

  /// <summary>
  /// After a full collection (NativeKeep's sweeper): the handles of objects only their proxy holds
  /// turn weak, and the objects disposed while VTK held them are released once it let go.
  /// </summary>
  internal static void Sweep()
  {
    if (_live == 0 || !Monitor.TryEnter(Objects))
    {
      return;
    }

    List<vtkObjectBase> released = null;
    try
    {
      foreach (var entry in Objects.Values)
      {
        if (!entry.IsStrong || entry.Handle.Target is not vtkObjectBase target)
        {
          continue;
        }

        // VTK may not have counted the reference it takes of an object just passed to it
        if (entry.Passed)
        {
          entry.Passed = false;
        }
        else if (target.GetReferenceCount() <= 1)
        {
          if (entry.DisposeRequested)
          {
            (released ??= []).Add(target);
          }
          else
          {
            entry.Weaken(target);
          }
        }
      }
    }
    finally
    {
      Monitor.Exit(Objects);
    }

    foreach (var target in released ?? [])
    {
      target.Dispose();
    }
  }

  /// <summary>
  /// What an override threw: parked under a number, which goes to NetVtkRuntime for the wrapped
  /// call that led to the callback.
  /// </summary>
  public static void Fail(Exception exception) => DirectorFailed(Park(exception));

  private static long Park(Exception exception)
  {
    lock (Parked)
    {
      var number = ++_parkedNumber;
      Parked[number] = exception;
      ParkedOrder.Enqueue(number);
      while (ParkedOrder.Count > MaxParked)
      {
        Parked.Remove(ParkedOrder.Dequeue());
      }

      return number;
    }
  }

  /// <summary>
  /// The exception parked under the number the wrapper of a call reports as a
  /// <c>NetVtk_ManagedException</c>'s message, or null.
  /// </summary>
  internal static Exception Take(string message)
  {
    if (!long.TryParse(message, out var number))
    {
      return null;
    }

    lock (Parked)
    {
      if (!Parked.TryGetValue(number, out var exception))
      {
        return null;
      }

      Parked.Remove(number);
      return exception;
    }
  }

  /// <summary>
  /// A callback's call of an override with a result: an exception is parked (<see cref="Fail"/>)
  /// and the result is the default.
  /// </summary>
  public static T Call<T>(Func<T> call)
  {
    try
    {
      return call();
    }
    catch (Exception exception)
    {
      Fail(exception);
      return default;
    }
  }

  /// <summary>
  /// An object an override returns, kept alive until the next one on this thread: the callback's
  /// C++ side takes its reference right after the callback.
  /// </summary>
  public static T Keep<T>(T value)
  {
    if (!ReferenceEquals(_returned, value))
    {
      _returned = value;
    }

    return value;
  }

  /// <summary>
  /// Whether <paramref name="type"/>, a C# subclass of the director class
  /// <paramref name="director"/>, overrides its virtual member <paramref name="name"/> taking
  /// <paramref name="types"/>: SWIG's SwigDerivedClassHasMethod (build.py generate puts this in its
  /// place), which reflected over the type on every call of a virtual member. Once per type and
  /// member; NetVtk's own proxies connect no callbacks and reach their members directly (their
  /// overrides are C++'s), but its own C# subclasses (<see cref="CSharpOverridesAttribute"/>) do.
  /// </summary>
  public static bool Overrides(Type type, Type director, string name, Type[] types)
  {
    if (type == director)
    {
      return false;
    }

    lock (OverridesOf)
    {
      if (!OverridesOf.TryGetValue(type, out var members))
      {
        OverridesOf[type] = members = [];
      }

      // SWIG's parameter types are a static array per member: its identity is the member's
      if (!members.TryGetValue(types, out var overrides))
      {
        members[types] = overrides = FindOverride(type, director, name, types);
      }

      return overrides;
    }
  }

  private static readonly Dictionary<Type, Dictionary<Type[], bool>> OverridesOf = [];

  private static bool FindOverride(Type type, Type director, string name, Type[] types)
  {
    if (type.Assembly == director.Assembly
        && !type.IsDefined(typeof(CSharpOverridesAttribute), false))
    {
      return false;
    }

    const BindingFlags instance =
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    foreach (var method in type.GetMethods(instance))
    {
      if (method.DeclaringType is not { } declaring || method.Name != name)
      {
        continue;
      }

      var parameters = method.GetParameters();
      var matches = parameters.Length == types.Length;
      for (var i = 0; matches && i < parameters.Length; i++)
      {
        matches = parameters[i].ParameterType == types[i];
      }

      if (matches
          && method.IsVirtual
          && declaring.IsSubclassOf(director)
          && declaring != method.GetBaseDefinition().DeclaringType)
      {
        return true;
      }
    }

    return false;
  }

  private sealed class Entry(object target)
  {
    // strong while VTK may hold the object, weak otherwise
    public GCHandle Handle = GCHandle.Alloc(target, GCHandleType.Weak);
    public bool IsStrong;

    // passed to VTK since the last sweep: its reference may not be counted yet
    public bool Passed;

    // disposed while VTK held it: released once VTK has let go
    public bool DisposeRequested;

    public void Strengthen(object target)
    {
      if (!IsStrong)
      {
        Handle.Free();
        Handle = GCHandle.Alloc(target, GCHandleType.Normal);
        IsStrong = true;
      }
    }

    public void Weaken(object target)
    {
      Handle.Free();
      Handle = GCHandle.Alloc(target, GCHandleType.Weak);
      IsStrong = false;
    }
  }
}

/// <summary>
/// A C# subclass of a director class in NetVtk itself (VtkOutput's output window, the handlers'
/// command): VTK calls its overrides, unlike the overrides of NetVtk's proxies, which are C++'s.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class CSharpOverridesAttribute : Attribute;
