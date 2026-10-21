// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * References a member returns: T& (ChangeX()), and const T& of classes C# can't copy. Numbers and enums by reference
 * are C# refs both ways: parameters (in/out) and returns (into the object).
 *
 *   number, enum, struct  -> a C# ref into the native object: array.GetValueReference(0) = 1.5
 *   class, collection     -> a proxy that borrows the object: Dispose leaves the object alone, and the
 *                            proxy keeps its owner's proxy alive, since the object lives in the owner
 *
 * netvtk-gen emits these for non-static members only, so the owner is `this`; a member returning its
 * own class (for chaining) is void. A reference is as good as in C++: until the owner changes or dies.
 * Structs: ValueTypes.i. VTK objects: VtkObjects.i (a returned object owns a reference).
 *
 * Pointers to classes: T* and const T* are the proxy, null for nullptr.
 *
 *   T* return of a member        -> a proxy that borrows the object and keeps the owner's proxy alive
 *   T* const return              -> a proxy that borrows the object: netvtk-gen spells the returns of
 *                                   functions without an object so (static members, namespace functions)
 *   T* const field               -> a value class's: read only, a proxy that borrows the object and keeps
 *                                   the owner's proxy alive
 *   T*& parameter                -> ref T: the callee may replace the pointer; a new one comes back as a
 *                                   borrowing proxy (VTK objects: VtkObjects.i, which own a reference)
 */

// SWIG's proxy bodies (csharp.swg, SWIG_CSBODY_PROXY) plus netvtkOwner, what the native object depends on: a borrowed
// object's owner, or what the object keeps (below). Derived proxies inherit it; Directors.i's bodies declare it too. Without
// swigRelease: nothing transfers ownership (no DISOWN or unique_ptr typemaps).
%typemap(csbody) SWIGTYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  protected bool swigCMemOwn;
  // what the native object depends on: a borrowed object's owner, or what the object keeps (References.i)
  internal object netvtkOwner;

  protected internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) {
    swigCMemOwn = cMemoryOwn;
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}

%typemap(csbody_derived) SWIGTYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;

  protected internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base($imclassname.$csclazznameSWIGUpcast(cPtr), cMemoryOwn) {
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}

// a borrowed object
%typemap(csout, excode=SWIGEXCODE) SWIGTYPE & {
    $csclassname ret = new $csclassname($imcall, false);$excode
    ret.netvtkOwner = this;
    return ret;
  }

// pointers: a member's object lives in its owner; without an object there's no owner to keep
%typemap(csout, excode=SWIGEXCODE) SWIGTYPE * {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, false);$excode
    if (ret != null) ret.netvtkOwner = this;
    return ret;
  }
%typemap(csout, excode=SWIGEXCODE) SWIGTYPE *const {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, false);$excode
    return ret;
  }
// a value class's field of a pointer to a class, read only (netvtk-gen declares it T* const): the object lives in the
// field's owner, as a member's does
%typemap(csvarout, excode=SWIGEXCODE2) SWIGTYPE *const %{
    get {
      global::System.IntPtr cPtr = $imcall;
      $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, false);$excode
      if (ret != null) ret.netvtkOwner = this;
      return ret;
    } %}

/*
 * What a native object keeps a reference to stays alive with its proxy (NativeKeep), for that declaration only:
 *
 *   constructor argument  NETVTK_KEEP, by type and name, and %netvtk_keep_construct(CLASS): an iterator that holds its
 *                         container. SWIG converts a constructor's arguments in a static helper, before the proxy exists,
 *                         so after the call the argument waits in NativeKeep for the constructor body. Held until the GC
 *                         has collected the proxy: the destructor may use it, and finalizers run in any order. The helper
 *                         first drops what a constructor that never reached its body left (a managed exception in its call).
 *   by-value return       of a CLASS %netvtk_keep_construct marks, from a member: it keeps the member's object, held as a
 *                         constructor argument is (a guard points into its owner)
 *   member argument       %netvtk_keep_argument(SLOT, DECL): the proxy keeps it in place of what the member's previous call
 *                         kept at that position, SLOT (an Initialize(object) that keeps the object)
 *   view return           NETVTK_VIEW, by type and member name: a member's by-value or const& return of a value class that
 *                         refers to other objects is a copy that still refers to them (a view of a container), as the moved by-value return of such a move-only class does: it keeps the member's
 *                         object.
 *                         NETVTK_VIEW_HELD for a view that declares its destructor, which may use the object
 *                         (a scope's closes it): held as a constructor argument is.
 *
 * Kept in post, not pre: an argument check that fails later in the call leaves nothing behind.
 */
%typemap(csin, pre="    global::Vtk.NativeKeep.Begin();", post="      global::Vtk.NativeKeep.Push($csinput);")
         SWIGTYPE & NETVTK_KEEP, SWIGTYPE * NETVTK_KEEP "$csclassname.getCPtr($csinput)"

%define %netvtk_keep_construct(CLASS)
%typemap(csconstruct, excode=SWIGEXCODE) CLASS %{: this($imcall, true) {
    netvtkOwner = global::Vtk.NativeKeep.Take(this);$excode
  }
%}
%typemap(csout, excode=SWIGEXCODE) CLASS {
    $csclassname ret = new $csclassname($imcall, true);$excode
    ret.netvtkOwner = global::Vtk.NativeKeep.Hold(ret, ret.netvtkOwner, this);
    return ret;
  }
%enddef

%typemap(csout, excode=SWIGEXCODE) SWIGTYPE NETVTK_VIEW, const SWIGTYPE & NETVTK_VIEW {
    $csclassname ret = new $csclassname($imcall, true);$excode
    ret.netvtkOwner = this;
    return ret;
  }
%typemap(csout, excode=SWIGEXCODE) SWIGTYPE NETVTK_VIEW_HELD, const SWIGTYPE & NETVTK_VIEW_HELD {
    $csclassname ret = new $csclassname($imcall, true);$excode
    ret.netvtkOwner = global::Vtk.NativeKeep.Hold(ret, ret.netvtkOwner, this);
    return ret;
  }

%define %netvtk_keep_argument(SLOT, DECL)
%typemap(csin, post="      netvtkOwner = global::Vtk.NativeKeep.Keep(netvtkOwner, \"SLOT\", $csinput);") DECL "$csclassname.getCPtr($csinput)"
%enddef

// A reference a member returns as a C# ref into the object, read and written in place: TYPE, the returned reference;
// CSTYPE, the C# type it refers to.
%define %netvtk_ref_out(TYPE, CSTYPE)
%typemap(out) TYPE %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE {
    global::System.IntPtr ptr = $imcall;$excode
    unsafe { return ref *(CSTYPE*)ptr; }
  }
%enddef

// T*& parameter -> ref T. The slot carries the caller's pointer in; a pointer the callee put there instead comes back
// as a proxy that borrows the object. After a C++ exception the variable keeps its value: the callee writes a copy.
// T*& a member returns -> ref IntPtr into the object, the pointer it holds.
%typemap(ctype, out="void *") SWIGTYPE *& "void **"
%typemap(imtype, out="global::System.IntPtr") SWIGTYPE *& "ref global::System.IntPtr"
%typemap(cstype, out="ref global::System.IntPtr") SWIGTYPE *& "ref $*csclassname"
%typemap(csin,
         pre="    global::System.IntPtr slot$csinput = $*csclassname.getCPtr($csinput).Handle; global::System.IntPtr old$csinput = slot$csinput;",
         post="      if (slot$csinput != old$csinput) $csinput = (slot$csinput == global::System.IntPtr.Zero) ? null : new $*csclassname(slot$csinput, false);",
         cshin="ref $csinput")
         SWIGTYPE *& "ref slot$csinput"
%typemap(in) SWIGTYPE *& ($*1_ltype temp) %{ temp = ($*1_ltype)*$input; $1 = &temp; %}
%typemap(argout) SWIGTYPE *& %{ *$input = (void*)temp$argnum; %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) SWIGTYPE *const& ""
%netvtk_ref_out(SWIGTYPE *&, global::System.IntPtr)

// T& of a number: a parameter in/out (typemaps.i's INOUT), a member's return a C# ref into the object. Width typedefs are
// parameters by their written names (Types.i); returns are spelled canonically.
%define %netvtk_ref_number(TYPE, CSTYPE)
%apply TYPE& INOUT { TYPE& };
%typemap(cstype, out="ref CSTYPE") TYPE& "ref CSTYPE"
%netvtk_ref_out(TYPE&, CSTYPE)
// director callbacks (Directors.i): read in, written back after the C# override
%typemap(directorin) TYPE& "$input = (void*)&$1;"
%typemap(csdirectorin, pre="    CSTYPE temp$iminput = global::Vtk.NativeStruct.Read<CSTYPE>($iminput);",
         post="      global::Vtk.NativeStruct.Write($iminput, temp$iminput);") TYPE& "ref temp$iminput"
%enddef

%netvtk_ref_number(bool, bool)
%netvtk_ref_number(int, int)
%netvtk_ref_number(unsigned int, uint)
%netvtk_ref_number(short, short)
%netvtk_ref_number(unsigned short, ushort)
%netvtk_ref_number(signed char, sbyte)
%netvtk_ref_number(unsigned char, byte)
%netvtk_ref_number(long long, long)
%netvtk_ref_number(unsigned long long, ulong)
%netvtk_ref_number(float, float)
%netvtk_ref_number(double, double)

// size_t& a member returns: a ref UIntPtr, pointer-sized as size_t (Types.i's parameters are ref ulong)
%netvtk_ref_out(size_t&, global::System.UIntPtr)

// T& of an enum: a C# ref through an int slot (enums are int-sized, checked), as a parameter and as a member's
// return. A const enum& is passed by value.
%typemap(ctype, out="void *") enum SWIGTYPE & "int *"
%typemap(imtype, out="global::System.IntPtr") enum SWIGTYPE & "ref int"
%typemap(cstype, out="ref $*csclassname") enum SWIGTYPE & "ref $*csclassname"
// cshin: the ref also on the call to a constructor's SwigConstruct helper, which holds pre/post
%typemap(csin, pre="    int temp$csinput = (int)$csinput;", post="      $csinput = ($*csclassname)temp$csinput;", cshin="ref $csinput") enum SWIGTYPE & "ref temp$csinput"
%typemap(in) enum SWIGTYPE & %{
  static_assert(sizeof(*$1) == sizeof(int), "enum reference needs an int-sized enum");
  $1 = ($1_ltype)$input;
%}
%netvtk_ref_out(enum SWIGTYPE &, $*csclassname)
