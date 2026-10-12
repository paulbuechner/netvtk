// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Value types: blittable C# structs with the exact C++ layout (netvtk-gen writes
 * src/NetVtk/<Module>/<Type>.g.cs next to a hand-written partial). SWIG never builds
 * proxies for them; these typemaps marshal them in other classes' signatures.
 *
 *   TYPE / const TYPE&   parameter  -> TYPE / in TYPE   (pointer to the C# struct)
 *   TYPE&                parameter  -> ref TYPE         (callee writes the caller's struct)
 *   TYPE* / const TYPE*  parameter  -> TYPE[]           (pinned array, as for numbers: see Types.i)
 *   TYPE / const TYPE&   return     -> TYPE             (via a thread-local buffer, see below)
 *   TYPE&                return     -> ref TYPE         (into the native owner, see References.i)
 *
 * Returns never pass a struct by value across the C ABI: on x86 __stdcall the hidden
 * return pointer is part of the export's _Name@N decoration, which .NET computes from
 * the managed signature (without it) -> EntryPointNotFoundException. The wrapper copies
 * the result into a thread-local buffer per type (NetVtk_Slot, NetVtk.i) and returns its
 * address; C# checks for a pending exception first, then reads the struct (one unmanaged copy).
 */
// TYPE as the C# struct CSTYPE of its layout, as above (not the arrays)
%define %netvtk_struct(TYPE, CSTYPE)
%typemap(ctype, out="void *") TYPE, const TYPE&, TYPE& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") TYPE, TYPE& "ref CSTYPE"
%typemap(imtype, out="global::System.IntPtr") const TYPE& "in CSTYPE"
%typemap(cstype, out="CSTYPE") TYPE "CSTYPE"
%typemap(cstype, out="CSTYPE") const TYPE& "in CSTYPE"
%typemap(cstype, out="ref CSTYPE") TYPE& "ref CSTYPE"
%typemap(csin) TYPE, TYPE& "ref $csinput"
%typemap(csin) const TYPE& "in $csinput"
%typemap(in) TYPE %{ $1 = *($&1_ltype)$input; %}
%typemap(in) const TYPE&, TYPE& %{ $1 = ($1_ltype)$input; %}
%typemap(out, null="0") TYPE %{
  { TYPE& netvtk_ret = NetVtk_Slot< TYPE >(); netvtk_ret = $1; $result = (void*)&netvtk_ret; }
%}
%typemap(out, null="0") const TYPE& %{
  { TYPE& netvtk_ret = NetVtk_Slot< TYPE >(); netvtk_ret = *$1; $result = (void*)&netvtk_ret; }
%}
%typemap(out, null="0") TYPE& %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE, const TYPE& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::Vtk.NativeStruct.Read<CSTYPE>(ptr);
  }
%typemap(csout, excode=SWIGEXCODE) TYPE& {
    global::System.IntPtr ptr = $imcall;$excode
    unsafe { return ref *(CSTYPE*)ptr; }
  }
// director callbacks (Directors.i): a parameter reaches the C# override as a copy, a TYPE& read in and written back
// after; a result comes back through a thread-local buffer (NativeStruct.Stage), which the C++ side copies
%typemap(directorin) TYPE, const TYPE&, TYPE& "$input = (void*)const_cast< TYPE* >(&$1);"
%typemap(csdirectorin) TYPE, const TYPE& "global::Vtk.NativeStruct.Read<CSTYPE>($iminput)"
%typemap(csdirectorin, pre="    CSTYPE temp$iminput = global::Vtk.NativeStruct.Read<CSTYPE>($iminput);",
         post="      global::Vtk.NativeStruct.Write($iminput, temp$iminput);") TYPE& "ref temp$iminput"
%typemap(csdirectorout) TYPE "global::Vtk.NativeStruct.Stage(global::Vtk.Directors.Call(() => $cscall))"
%typemap(directorout) TYPE %{ $result = *static_cast< TYPE* >($input); %}
%enddef

%define %netvtk_valuetype(TYPE)
%netvtk_struct(TYPE, TYPE)
%netvtk_pointer_arrays(TYPE, TYPE)
%enddef

/*
 * Value classes: copyable classes outside vtkObjectBase wrapped as proxies (std::pair instances, VTK's small classes
 * netvtk-gen doesn't make structs). A const& return points into its parent, so it is copied into a proxy that owns the
 * copy.
 */
%define %netvtk_valueclass(TYPE)
%typemap(out) const TYPE& %{ $result = (void*)new TYPE(*$1); %}
%typemap(csout, excode=SWIGEXCODE) const TYPE& {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
    return ret;
  }
// director callbacks (Directors.i): a parameter reaches the C# override as a copy its proxy owns (a TYPE& borrows, as
// SWIG's default does); a result is copied from the returned proxy, which Directors.Keep holds until then
%typemap(directorin) TYPE, const TYPE& "$input = (void*)new TYPE($1);"
%typemap(csdirectorin) TYPE "new $&csclassname($iminput, true)"
%typemap(csdirectorin) const TYPE& "new $csclassname($iminput, true)"
// a null result (an exception the override threw, Directors.Call, or a null it returned, parked as one) is TYPE()
%typemap(csdirectorout) TYPE
  "$&csclassname.getCPtr(global::Vtk.Directors.Keep(global::Vtk.Directors.Call(() => $cscall ?? throw new global::System.InvalidOperationException(\"a C# override returned null\")))).Handle"
%typemap(directorout) TYPE %{ $result = $input ? *static_cast< TYPE* >($input) : TYPE(); %}
%enddef
