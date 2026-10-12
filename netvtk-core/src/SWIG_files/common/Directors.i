// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Directors: VTK classes C# subclasses (netvtk-gen's config directors: vtkCommand for observers), whose overrides VTK
 * calls through SWIG's director callbacks.
 *
 * Exceptions. A C# exception can't unwind through C++, and VTK isn't written to be unwound through (an algorithm left in
 * the middle of an update stays marked as executing). The callback's C# side catches what an override throws, parks it
 * in Vtk.Directors under a number and hands the number to NetVtkRuntime (NetVtk_DirectorFailed); the callback returns as
 * if the override had (one with a result returns its type's default) and VTK goes on. When the wrapped call that led to
 * the callback returns, its wrapper takes the number (Exceptions.i) and C# throws a VtkException whose InnerException is
 * the parked one. NetVtkRuntime holds the number per thread for every module: the callback's module and the call's
 * differ (a vtkCommand of CommonCore, called back from a render of RenderingCore).
 *
 * Lifetime and identity (Vtk.Directors): a director object lives while VTK holds a reference, and VTK handing it back
 * gives C# the same instance. %netvtk_director marks a director class, %netvtk_directed the director classes and their
 * bases, whose pointers may be director objects.
 *
 * The conversions per type sit with the type's typemaps: VtkObjects.i, Strings.i, References.i, Types.i.
 */

%{
#include <NetVtkRuntime.hxx>
#include <vtkObjectBase.h>

// drops the reference a returned object carried for a new proxy, when C# gives back the director object's own
#ifdef __cplusplus
extern "C"
#endif
SWIGEXPORT void SWIGSTDCALL NetVtkDirectorRelease_$module(void* theObject) {
  static_cast<vtkObjectBase*>(theObject)->UnRegister(nullptr);
}
%}

%pragma(csharp) imclasscode=%{
  [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="NetVtkDirectorRelease_$module")]
  public static extern void NetVtkDirectorRelease(global::System.IntPtr theObject);

  // for the identity of returned objects (Directors.Adopt)
  internal static readonly global::System.Action<global::System.IntPtr> netvtkRelease = NetVtkDirectorRelease;
%}

// the C# side of a callback: an exception the override throws is parked for the wrapped call that led here
%typemap(csdirectorout) void "try { $cscall; } catch (global::System.Exception netvtkError) { global::Vtk.Directors.Fail(netvtkError); }"
%define %netvtk_director_call(TYPE)
%typemap(csdirectorout) TYPE "global::Vtk.Directors.Call(() => $cscall)"
%enddef
%netvtk_director_call(bool)
%netvtk_director_call(signed char)
%netvtk_director_call(unsigned char)
%netvtk_director_call(short)
%netvtk_director_call(unsigned short)
%netvtk_director_call(int)
%netvtk_director_call(unsigned int)
%netvtk_director_call(unsigned long)
%netvtk_director_call(long long)
%netvtk_director_call(unsigned long long)
%netvtk_director_call(float)
%netvtk_director_call(double)
%typemap(csdirectorout) enum SWIGTYPE "(int)global::Vtk.Directors.Call(() => $cscall)"

// the C++ side converts the result; nothing is thrown there
%typemap(directorout) bool %{ $result = $input ? true : false; %}
%define %netvtk_director_number_out(TYPE)
%typemap(directorout) TYPE %{ $result = ($1_ltype)$input; %}
%enddef
%netvtk_director_number_out(signed char)
%netvtk_director_number_out(unsigned char)
%netvtk_director_number_out(short)
%netvtk_director_number_out(unsigned short)
%netvtk_director_number_out(int)
%netvtk_director_number_out(unsigned int)
%netvtk_director_number_out(unsigned long)
%netvtk_director_number_out(long long)
%netvtk_director_number_out(unsigned long long)
%netvtk_director_number_out(float)
%netvtk_director_number_out(double)
%netvtk_director_number_out(enum SWIGTYPE)

/*
 * A director class: SWIG's C++ subclass calls the C# overrides back. After SWIG connects them, the object joins
 * Vtk.Directors, which keeps it alive while VTK holds a reference. Dispose keeps the reference while VTK does: explicitly
 * disposed, the object is released once VTK lets go; finalized, it lives on.
 */
%define %netvtk_director(TYPE)
%feature("director") TYPE;
%typemap(csconstruct, excode=SWIGEXCODE,
         directorconnect="\n    SwigDirectorConnect();\n    global::Vtk.Directors.Register(this, swigCPtr.Handle);") TYPE %{: this($imcall, true) {$excode$directorconnect
  }
%}
%typemap(csdisposing_derived, methodname="Dispose", methodmodifiers="protected", parameters="bool disposing") TYPE {
    lock(this) {
      if (global::Vtk.Directors.Holds(this, swigCPtr.Handle, disposing)) {
        return;
      }
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
      }
      base.Dispose(disposing);
    }
  }
%enddef

/*
 * A director class or one of its bases: a pointer to it may be a director object. Returned to C#, it is the object
 * itself (Directors.Adopt), and passed to VTK, which may keep it, it stays alive (Directors.Passed, in getCPtr).
 */
%define %netvtk_directed(TYPE)
%typemap(csout, excode=SWIGEXCODE) TYPE *const, const TYPE *const {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : (global::Vtk.Directors.Adopt<TYPE>(cPtr, $imclassname.netvtkRelease) ?? new TYPE(cPtr, true));$excode
    return ret;
  }
%typemap(csdirectorin) TYPE *, const TYPE *
  "(($iminput == global::System.IntPtr.Zero) ? null : (global::Vtk.Directors.Adopt<TYPE>($iminput, $imclassname.netvtkRelease) ?? new TYPE($iminput, true)))"
%typemap(csbody) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  protected bool swigCMemOwn;
  // what the native object depends on: a borrowed object's owner, or what the object keeps (References.i)
  internal object netvtkOwner;

  protected internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) {
    swigCMemOwn = cMemoryOwn;
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  // passed to VTK, which may keep a reference: a director object then stays alive (Directors.Passed)
  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    if (obj == null) {
      return new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
    }
    global::Vtk.Directors.Passed(obj.swigCPtr.Handle);
    return obj.swigCPtr;
  }
%}
%typemap(csbody_derived) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;

  protected internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base($imclassname.$csclazznameSWIGUpcast(cPtr), cMemoryOwn) {
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  // passed to VTK, which may keep a reference: a director object then stays alive (Directors.Passed)
  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    if (obj == null) {
      return new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
    }
    global::Vtk.Directors.Passed(obj.swigCPtr.Handle);
    return obj.swigCPtr;
  }
%}
%enddef
