// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * VTK's objects: vtkObjectBase and every class deriving from it count their references. The C# proxy is the object and
 * owns exactly one reference.
 *
 *   T::New(), NewInstance()   hand out a reference of their own (VTK_NEWINSTANCE): the proxy takes it. netvtk-gen spells
 *                             these returns T*.
 *   a C# constructor          calls T::New() (%vtk_new), whose reference the proxy takes
 *   any other returned T*     the wrapper adds one for the proxy: netvtk-gen spells these returns T* const
 *   a callback's parameter    the same, for the override's proxy (directors)
 *   a value class's field     the same, read only: netvtk-gen spells it T* const; a vtkSmartPointer<T> one is read and
 *                             written
 *   std::vector<T*>, std::vector<vtkSmartPointer<T>>
 *                             a C# array (Std.i), each element's proxy with a reference of its own
 *   Dispose, the finalizer    drop the proxy's reference (UnRegister, in place of a destructor)
 *
 * VTK counts atomically, so the finalizer thread may release. It may delete an object there, too, which a rendering
 * object (render window, mapper, actor, texture) mustn't: its destructor releases OpenGL resources, which needs the
 * thread that renders. Dispose them there, or set Vtk.VtkFinalizer.Context to its context: the finalizer posts the
 * release there.
 */

%{
#include <vtkObjectBase.h>
#include <vtkSmartPointer.h>
%}

template <class T> class vtkSmartPointer;

%feature("unref") vtkObjectBase "$this->UnRegister(nullptr);"
// the finalizer releases on VtkFinalizer.Context's thread, when one is set
%typemap(csdispose) vtkObjectBase %{
  ~$csclassname() {
    if (!global::Vtk.VtkFinalizer.Defer(this)) {
      Dispose(false);
    }
  }

  public void Dispose() {
    Dispose(true);
    global::System.GC.SuppressFinalize(this);
  }
%}

// Before each class deriving from vtkObjectBase, the root included.
%define %vtk_object(TYPE)
%typemap(out) TYPE *const, const TYPE *const %{
  { TYPE* p = const_cast< TYPE* >($1); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) TYPE *, const TYPE *, TYPE *const, const TYPE *const {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
    return ret;
  }
// a value class's object field, which netvtk-gen declares T* const (a read-only property): its getter's object gets a
// reference for the proxy, as a returned one does (the out typemap above)
%typemap(csvarout, excode=SWIGEXCODE2) TYPE *const, const TYPE *const %{
    get {
      global::System.IntPtr cPtr = $imcall;
      $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
      return ret;
    } %}
%typemap(directorin) TYPE *, const TYPE * %{
  { TYPE* p = const_cast< TYPE* >($1); if (p) p->Register(nullptr); $input = (void*)p; }
%}
%typemap(csdirectorin) TYPE *, const TYPE * "(($iminput == global::System.IntPtr.Zero) ? null : new $csclassname($iminput, true))"

// vtkSmartPointer<TYPE> is the object, as a returned TYPE*: a parameter takes the proxy's pointer, a return gets a
// reference of its own for the proxy
%typemap(ctype)  vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "void *"
%typemap(imtype, out="global::System.IntPtr") vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "global::System.Runtime.InteropServices.HandleRef"
%typemap(cstype) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "TYPE"
%typemap(csin)   vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& "TYPE.getCPtr($csinput)"
%typemap(in) vtkSmartPointer< TYPE > %{ $1 = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input)); %}
%typemap(in) const vtkSmartPointer< TYPE >& (vtkSmartPointer< TYPE > temp) %{
  temp = vtkSmartPointer< TYPE >(static_cast< TYPE* >($input));
  $1 = &temp;
%}
%typemap(out) vtkSmartPointer< TYPE > %{
  { TYPE* p = $1.Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(out) const vtkSmartPointer< TYPE >&, vtkSmartPointer< TYPE >& %{
  { TYPE* p = $1->Get(); if (p) p->Register(nullptr); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >&, vtkSmartPointer< TYPE >& {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new TYPE(cPtr, true);$excode
    return ret;
  }
// a value class's smart pointer field: a property, read as a returned one, written as a parameter (the field counts a
// reference)
%typemap(csvarout, excode=SWIGEXCODE2) const vtkSmartPointer< TYPE >& %{
    get {
      global::System.IntPtr cPtr = $imcall;
      TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new TYPE(cPtr, true);$excode
      return ret;
    } %}

// vtkSmartPointer<TYPE>& and TYPE*& (in/out) -> ref TYPE: the slot carries the caller's object in; one the callee put in
// its place comes back with a reference of its own for its new proxy. Otherwise the variable keeps its proxy.
%typemap(ctype, out="void *") vtkSmartPointer< TYPE >& "void **"
%typemap(imtype, out="global::System.IntPtr") vtkSmartPointer< TYPE >& "ref global::System.IntPtr"
%typemap(cstype, out="TYPE") vtkSmartPointer< TYPE >& "ref TYPE"
%typemap(in) vtkSmartPointer< TYPE >& (vtkSmartPointer< TYPE > temp) %{
  temp = vtkSmartPointer< TYPE >(static_cast< TYPE* >(*$input));
  $1 = &temp;
%}
%typemap(argout) vtkSmartPointer< TYPE >& %{
  { TYPE* p = temp$argnum.Get(); if (p != static_cast< TYPE* >(*$input)) { if (p) p->Register(nullptr); *$input = (void*)p; } }
%}
// SWIG falls back from const T& to T& for typemaps missing on const T&: keep argout off const smart pointers
%typemap(argout) const vtkSmartPointer< TYPE >& ""
%typemap(argout) TYPE *& %{
  if (temp$argnum != *(TYPE**)$input && temp$argnum) temp$argnum->Register(nullptr);
  *$input = (void*)temp$argnum;
%}
// cshin: in a constructor SWIG moves pre/post into a SwigConstruct helper, whose call needs the ref too
%typemap(csin,
         pre="    TYPE keep$csinput = $csinput; global::System.IntPtr slot$csinput = TYPE.getCPtr(keep$csinput).Handle, old$csinput = slot$csinput;",
         post="      global::System.GC.KeepAlive(keep$csinput);"
              " if (slot$csinput != old$csinput) $csinput = (slot$csinput == global::System.IntPtr.Zero) ? null : new TYPE(slot$csinput, true);",
         cshin="ref $csinput")
         vtkSmartPointer< TYPE >&, TYPE *& "ref slot$csinput"

// director callbacks: a smart pointer reaches the C# override with a reference for its proxy
%typemap(directorin) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >& %{
  { TYPE* p = $1.Get(); if (p) p->Register(nullptr); $input = (void*)p; }
%}
%typemap(csdirectorin) vtkSmartPointer< TYPE >, const vtkSmartPointer< TYPE >&
  "(($iminput == global::System.IntPtr.Zero) ? null : new TYPE($iminput, true))"
%enddef

// A C# constructor for a class VTK creates: T::New(), which may return a subclass its object factory chose
// (vtkRenderWindow::New() returns the platform's OpenGL window).
%define %vtk_new(TYPE)
%extend TYPE {
  TYPE() { return TYPE::New(); }
}
%enddef

/*
 * A vector of VTK objects, by pointer or vtkSmartPointer (Std.i's vectors): in, the objects whose addresses C# passes
 * (null ones nullptr); out, the objects, each with a reference for its proxy.
 */
%{
template <class T>
void NetVtk_ObjectsIn(const void* theBlock, std::vector<T*>& theVector)
{
  theVector.resize(NetVtk_BlockCount(theBlock));
  for (std::size_t i = 0; i < theVector.size(); ++i)
  {
    theVector[i] = static_cast<T*>(NetVtk_BlockAddress(theBlock, i));
  }
}

template <class T>
void NetVtk_ObjectsIn(const void* theBlock, std::vector< vtkSmartPointer<T> >& theVector)
{
  theVector.resize(NetVtk_BlockCount(theBlock));
  for (std::size_t i = 0; i < theVector.size(); ++i)
  {
    theVector[i] = static_cast<T*>(NetVtk_BlockAddress(theBlock, i));
  }
}

template <int Position, class T>
void* NetVtk_ObjectsOut(const std::vector<T*>& theVector)
{
  std::vector<void*>& anObjects = NetVtk_Slot< std::vector<void*>, Position >();
  anObjects.clear();
  for (T* anObject : theVector)
  {
    if (anObject)
    {
      anObject->Register(nullptr);
    }
    anObjects.push_back(anObject);
  }
  return NetVtk_VectorOf< Position >(anObjects.size(), anObjects.data());
}

template <int Position, class T>
void* NetVtk_ObjectsOut(const std::vector< vtkSmartPointer<T> >& theVector)
{
  std::vector<void*>& anObjects = NetVtk_Slot< std::vector<void*>, Position >();
  anObjects.clear();
  for (const vtkSmartPointer<T>& anObject : theVector)
  {
    if (anObject)
    {
      anObject->Register(nullptr);
    }
    anObjects.push_back(anObject.Get());
  }
  return NetVtk_VectorOf< Position >(anObjects.size(), anObjects.data());
}
%}

%define %netvtk_object_vector(TYPE)
%netvtk_vector_typemaps(%arg(std::vector< TYPE* >), TYPE, %arg(global::Vtk.NativeArrays.Pointers($csinput, TYPE.getCPtr)),
                        %arg(global::Vtk.NativeArrays.Proxies(ptr, netvtkItem => new TYPE(netvtkItem, true))),
                        NetVtk_ObjectsIn, NetVtk_ObjectsOut)
%netvtk_vector_typemaps(%arg(std::vector< vtkSmartPointer< TYPE > >), TYPE,
                        %arg(global::Vtk.NativeArrays.Pointers($csinput, TYPE.getCPtr)),
                        %arg(global::Vtk.NativeArrays.Proxies(ptr, netvtkItem => new TYPE(netvtkItem, true))),
                        NetVtk_ObjectsIn, NetVtk_ObjectsOut)
%enddef

/*
 * VTK's width typedefs, by their names: vtkIdType is long long (VTK_USE_64BIT_IDS, on every architecture: the port sets
 * it), vtkMTimeType unsigned long long on Windows and unsigned long on 64-bit Linux and macOS. The wrappers spell them as
 * written, so they compile everywhere; C# sees long and ulong. vtkTypeBool is int.
 */
%{
#include <vtkType.h>
%}
// by value and const& (the name kept: it differs per platform), by reference (in/out), pointers (C# arrays)
%define %netvtk_width_typedef(NAME, CANONICAL, CSTYPE, CHECK)
%netvtk_pointer_sized(NAME, CSTYPE)
%typecheck(CHECK) NAME, const NAME& ""
%apply CANONICAL& INOUT { NAME& };
%netvtk_pointer_arrays(NAME, CSTYPE)
%typemap(directorin) NAME "$input = $1;"
%typemap(csdirectorin) NAME "$iminput"
%typemap(csdirectorout) NAME "global::Vtk.Directors.Call(() => $cscall)"
%typemap(directorout) NAME %{ $result = ($1_ltype)$input; %}
%enddef

%netvtk_width_typedef(vtkIdType, long long, long, SWIG_TYPECHECK_INT64)
%netvtk_width_typedef(vtkMTimeType, unsigned long long, ulong, SWIG_TYPECHECK_UINT64)
%netvtk_width_typedef(vtkTypeInt64, long long, long, SWIG_TYPECHECK_INT64)
%netvtk_width_typedef(vtkTypeUInt64, unsigned long long, ulong, SWIG_TYPECHECK_UINT64)
%netvtk_width_typedef(vtkTypeInt32, int, int, SWIG_TYPECHECK_INT32)
%netvtk_width_typedef(vtkTypeUInt32, unsigned int, uint, SWIG_TYPECHECK_UINT32)
%netvtk_width_typedef(vtkTypeInt16, short, short, SWIG_TYPECHECK_INT16)
%netvtk_width_typedef(vtkTypeUInt16, unsigned short, ushort, SWIG_TYPECHECK_UINT16)
%netvtk_width_typedef(vtkTypeInt8, signed char, sbyte, SWIG_TYPECHECK_INT8)
%netvtk_width_typedef(vtkTypeUInt8, unsigned char, byte, SWIG_TYPECHECK_UINT8)
typedef int vtkTypeBool;

/*
 * A pointer VTK returns with a size hint (VTK_SIZEHINT(N): GetBounds() is 6 doubles, GetColor() 3) -> a C# array, copied
 * right after the call: the pointer is into the object, valid until its next change. netvtk-gen applies NETVTK_SIZE<N> by
 * return type and member name around the declaration (%apply, then %clear).
 */
%define %netvtk_sized_return(TYPE, CSTYPE, N)
%typemap(ctype)  TYPE *NETVTK_SIZE##N, const TYPE *NETVTK_SIZE##N "void *"
%typemap(imtype) TYPE *NETVTK_SIZE##N, const TYPE *NETVTK_SIZE##N "global::System.IntPtr"
%typemap(cstype) TYPE *NETVTK_SIZE##N, const TYPE *NETVTK_SIZE##N "CSTYPE[]"
%typemap(out)    TYPE *NETVTK_SIZE##N, const TYPE *NETVTK_SIZE##N %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE *NETVTK_SIZE##N, const TYPE *NETVTK_SIZE##N {
    global::System.IntPtr ptr = $imcall;$excode
    return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, N);
  }
%enddef

%netvtk_sized_return(double, double, 2)
%netvtk_sized_return(double, double, 3)
%netvtk_sized_return(double, double, 4)
%netvtk_sized_return(double, double, 6)
%netvtk_sized_return(int, int, 2)
%netvtk_sized_return(int, int, 3)
%netvtk_sized_return(int, int, 4)
%netvtk_sized_return(int, int, 6)

/*
 * A pointer VTK hands back through a T*& parameter whose size hint names it (VTK_SIZEHINT(pts, npts): GetCellPoints'
 * points, as many as npts says after the call) -> ref CSTYPE[], copied right after the call (null for nullptr); the array
 * passed in isn't read, and after a C++ exception the variable keeps it. netvtk-gen declares the parameter as written
 * and calls the macro around the declaration, %clear after: SIZE is the count as the wrapper spells it, its arguments
 * (*arg3 for a reference) and its result.
 */
%{
// the pointer and the count C# copies, in the block it passes (two long longs; the count -1 until the call is over)
template <class T>
void NetVtk_SizedResult(long long* theBlock, T* thePointer, long long theCount)
{
  theBlock[0] = static_cast<long long>(reinterpret_cast<std::intptr_t>(thePointer));
  theBlock[1] = thePointer && theCount > 0 ? theCount : 0;
}
%}

%define %netvtk_sized_ref(TYPE, NAME, CSTYPE, SIZE)
%typemap(ctype)  TYPE *&NAME "long long *"
%typemap(imtype) TYPE *&NAME "long[]"
%typemap(cstype) TYPE *&NAME "ref CSTYPE[]"
%typemap(csin, pre="    long[] sized$csinput = new long[] { 0, -1 };",
         post="      if (sized$csinput[1] >= 0) $csinput = global::Vtk.NativeArrays.CopyOut<CSTYPE>(new global::System.IntPtr(sized$csinput[0]), (int)sized$csinput[1]);",
         cshin="ref $csinput") TYPE *&NAME "sized$csinput"
%typemap(in) TYPE *&NAME ($*1_ltype temp) %{
  temp = 0;
  $1 = &temp;
%}
%typemap(argout) TYPE *&NAME %{ NetVtk_SizedResult($input, temp$argnum, static_cast<long long>(SIZE)); %}
%enddef
