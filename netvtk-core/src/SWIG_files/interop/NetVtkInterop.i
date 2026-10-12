// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NetVtk's objects in another SWIG binding (NetOcc.Vtk's), through NetVtk's public API only. The binding %includes this
 * file (the NetVtk package's swig/interop/) next to its own typemap library, not NetVtk's: NetVtk's typemaps use its
 * internals, and its global ones (proxy bodies, strings) would apply to the binding's own classes too.
 *
 *   %netvtk_object(TYPE)          a NetVtk class (global::Vtk.TYPE) in the binding's signatures: TYPE* and
 *                                 vtkSmartPointer<TYPE>. In: the proxy's NativePointer. Out: a proxy with a reference of
 *                                 its own (vtkObjectBase.FromNativePointer). As in NetVtk, a TYPE* return hands over a
 *                                 reference (New(), NewInstance()); a TYPE* const return gets one from the wrapper.
 *   %netvtk_derived(TYPE, CSBASE) the binding's own VTK class, deriving from a NetVtk class (CSBASE:
 *                                 global::Vtk.vtkPolyDataAlgorithm) or from another such class of the binding: its
 *                                 proxy derives from CSBASE's through the protected internal pointer constructor, and
 *                                 NetVtk's proxy at the root holds and drops the object's reference. VTK classes have
 *                                 one base each: a TYPE* is its base's pointer as it is.
 *   %netvtk_new(TYPE)             a C# constructor calling TYPE::New(), whose reference the proxy takes.
 */

%{
#include <vtkObjectBase.h>
#include <vtkSmartPointer.h>
%}

template <class T> class vtkSmartPointer;

%define %netvtk_object(TYPE)
%typemap(ctype) TYPE *, const TYPE *, TYPE *const, const TYPE *const, vtkSmartPointer< TYPE >,
                const vtkSmartPointer< TYPE >& "void *"
%typemap(imtype, out="global::System.IntPtr") TYPE *, const TYPE *, TYPE *const, const TYPE *const,
                                              vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >&
  "global::System.Runtime.InteropServices.HandleRef"
%typemap(cstype) TYPE *, const TYPE *, TYPE *const, const TYPE *const, vtkSmartPointer< TYPE >,
                 const vtkSmartPointer< TYPE >& "global::Vtk.TYPE"
%typemap(csin) TYPE *, const TYPE *, TYPE *const, const TYPE *const, vtkSmartPointer< TYPE >,
               const vtkSmartPointer< TYPE >&
  "new global::System.Runtime.InteropServices.HandleRef($csinput, ($csinput == null) ? global::System.IntPtr.Zero : $csinput.NativePointer)"
%typemap(in) TYPE *, const TYPE *, TYPE *const, const TYPE *const %{ $1 = ($1_ltype)$input; %}
%typemap(in) vtkSmartPointer< TYPE > %{ $1 = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input)); %}
%typemap(in) const vtkSmartPointer< TYPE >& (vtkSmartPointer< TYPE > temp) %{
  temp = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input));
  $1 = &temp;
%}
%typemap(typecheck, precedence=SWIG_TYPECHECK_POINTER) TYPE *, const TYPE *, TYPE *const, const TYPE *const,
                                                      vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& ""
// TYPE*: the function hands over a reference; the others get one for the proxy
%typemap(out) TYPE *, const TYPE * %{ $result = (void*)$1; %}
%typemap(out) TYPE *const, const TYPE *const %{
  { TYPE* p = const_cast< TYPE* >($1); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(out) vtkSmartPointer< TYPE > %{
  { TYPE* p = $1.Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(out) const vtkSmartPointer< TYPE >& %{
  { TYPE* p = $1->Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) TYPE *, const TYPE *, TYPE *const, const TYPE *const, vtkSmartPointer< TYPE >,
                                   const vtkSmartPointer< TYPE >& {
    global::System.IntPtr cPtr = $imcall;$excode
    return global::Vtk.vtkObjectBase.FromNativePointer<global::Vtk.TYPE>(cPtr, true);
  }
%enddef

%define %netvtk_derived(TYPE, CSBASE)
%typemap(csbase) TYPE "CSBASE"
%typemap(csinterfaces) TYPE ""
%typemap(csbody) TYPE %{
  // the object as NetVtk's proxy holds it (zero once disposed): one base each, so the same pointer
  private global::System.Runtime.InteropServices.HandleRef swigCPtr =>
    new global::System.Runtime.InteropServices.HandleRef(this, NativePointer);

  protected internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base(cPtr, cMemoryOwn) {
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}
// NetVtk's proxy at the root has the finalizer and Dispose, and releases the reference: nothing of its own
%typemap(csdispose) TYPE ""
%typemap(csdisposing) TYPE ""
%nodefaultdtor TYPE;
// its own pointers as NetVtk's: TYPE* hands over a reference (New()), TYPE* const gets one for the proxy
%typemap(out) TYPE *const, const TYPE *const %{
  { TYPE* p = const_cast< TYPE* >($1); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) TYPE *, const TYPE *, TYPE *const, const TYPE *const {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
    return ret;
  }
// vtkSmartPointer<TYPE> is the object, as a TYPE*: a reference of its own for the proxy
%typemap(ctype) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "void *"
%typemap(imtype, out="global::System.IntPtr") vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >&
  "global::System.Runtime.InteropServices.HandleRef"
%typemap(cstype) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "TYPE"
%typemap(csin) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "TYPE.getCPtr($csinput)"
%typemap(in) vtkSmartPointer< TYPE > %{ $1 = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input)); %}
%typemap(in) const vtkSmartPointer< TYPE >& (vtkSmartPointer< TYPE > temp) %{
  temp = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input));
  $1 = &temp;
%}
%typemap(out) vtkSmartPointer< TYPE > %{
  { TYPE* p = $1.Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(out) const vtkSmartPointer< TYPE >& %{
  { TYPE* p = $1->Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new TYPE(cPtr, true);$excode
    return ret;
  }
%enddef

%define %netvtk_new(TYPE)
%extend TYPE {
  TYPE() { return TYPE::New(); }
}
%enddef
