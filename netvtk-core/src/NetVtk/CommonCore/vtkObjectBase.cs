// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Vtk;

// the object's text, and native pointers in both directions
public partial class vtkObjectBase
{
  /// <summary>
  /// VTK's description of the object (<c>Print</c>): its class, references and state.
  /// </summary>
  public override string ToString() =>
    swigCPtr.Handle == IntPtr.Zero ? $"{GetType().Name} (disposed)" : NetVtk_Print();

  /// <summary>
  /// The native object (a <c>vtkObjectBase*</c>), for C++ code that takes VTK objects (C++/CLI, a
  /// native library of your own): valid while the proxy holds its reference, so keep the proxy
  /// alive and undisposed while the pointer is in use. <see cref="IntPtr.Zero"/> once disposed.
  /// </summary>
  public IntPtr NativePointer => swigCPtr.Handle;

  /// <summary>
  /// A proxy of type <typeparamref name="T"/> for the VTK object at <paramref name="pointer"/>,
  /// or null for <see cref="IntPtr.Zero"/>. The proxy holds a reference of its own, which Dispose
  /// drops: with <paramref name="ownsReference"/>, the one the caller hands over (a pointer
  /// <c>New()</c> returned); otherwise a new one.
  /// </summary>
  /// <exception cref="InvalidCastException">
  /// The object isn't a <typeparamref name="T"/>.
  /// </exception>
  public static T FromNativePointer<T>(IntPtr pointer, bool ownsReference = false)
    where T : vtkObjectBase
  {
    if (pointer == IntPtr.Zero)
    {
      return null;
    }

    if (!ownsReference)
    {
      NetVtk_Register(pointer);
    }

    var proxy = new vtkObjectBase(pointer, true);
    if (typeof(T) == typeof(vtkObjectBase))
    {
      return (T)proxy;
    }

    using (proxy)
    {
      return Downcast<T>.SafeDownCast(proxy) as T
             ?? throw new InvalidCastException(
               $"The object is a {proxy.GetClassName()}, not a {typeof(T).Name}.");
    }
  }

  // T's SafeDownCast, looked up once per type: the object as a T, with a reference for its proxy,
  // or null
  private static class Downcast<T> where T : vtkObjectBase
  {
    public static readonly Func<vtkObjectBase, vtkObjectBase> SafeDownCast = Find();

    private static Func<vtkObjectBase, vtkObjectBase> Find()
    {
      var method = typeof(T).GetMethod("SafeDownCast",
                                       BindingFlags.Public
                                       | BindingFlags.Static
                                       | BindingFlags.FlattenHierarchy, null,
                                       [typeof(vtkObjectBase)], null);
      return method is null
        ? _ => throw new NotSupportedException(
          $"{typeof(T).Name} has no SafeDownCast to check the object against.")
        : (Func<vtkObjectBase, vtkObjectBase>)Delegate.CreateDelegate(
          typeof(Func<vtkObjectBase, vtkObjectBase>), method);
    }
  }

  // the finalizer's release (VtkObjects.i), on VtkFinalizer.Context's thread
  internal void ReleaseFinalized() => Dispose(false);
}
