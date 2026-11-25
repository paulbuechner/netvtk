// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Numbers: by value, by reference (parameters; the fixed-width builtins, which members return by reference too:
 * References.i), pointers to them (C# arrays) and addresses. netvtk-gen resolves VTK's typedefs (vtkIdType, vtkTypeBool).
 *
 * Contract: a non-const C++ reference is a C# `ref` parameter, never `out`,
 * because some VTK reference parameters are read as well as written.
 */

%include <typemaps.i>

// char is its byte (its sign differs per platform), by value and by reference: a C# char would pass through a code page.
// char* is a byte buffer (below), const char* a string (Strings.i). char32_t is a UTF-32 code point, a uint.
%apply unsigned char { char };
%apply const unsigned char& { const char& };
%apply unsigned char& INOUT { char& };
%apply unsigned int { char32_t };
%apply const unsigned int& { const char32_t& };
// width typedefs by their written names (int64_t is long on Linux, long long on Windows)
%apply unsigned char& INOUT { uint8_t& };
%apply signed char& INOUT { int8_t& };
%apply short& INOUT { int16_t& };
%apply unsigned short& INOUT { uint16_t& };
%apply int& INOUT { int32_t& };
%apply unsigned int& INOUT { uint32_t& };
%apply long long& INOUT { int64_t& };
%apply unsigned long long& INOUT { uint64_t& };

/*
 * C long: 32 bits on Windows, 64 on Linux and macOS. A C# long, 64 bits at the P/Invoke layer; a value a 32-bit long can't
 * hold throws VtkException (std::out_of_range) instead of wrapping around. long& is in/out through a C long local.
 */
%{
#include <climits>
%}
// ranked as the 64-bit integer it is in C#, so an overload taking int stays (vtkVariant(int), vtkVariant(long))
%typecheck(SWIG_TYPECHECK_INT64) long, const long& ""
%typemap(ctype) long, const long& "long long"
%typemap(imtype) long, const long& "long"
%typemap(cstype) long, const long& "long"
%typemap(csin) long, const long& "$csinput"
%typemap(in) long %{
  if ($input < LONG_MIN || $input > LONG_MAX) { NetVtk_SetPendingException("std::out_of_range", "the value doesn't fit a C long"); return $null; }
  $1 = (long)$input;
%}
%typemap(in) const long& (long temp) %{
  if ($input < LONG_MIN || $input > LONG_MAX) { NetVtk_SetPendingException("std::out_of_range", "the value doesn't fit a C long"); return $null; }
  temp = (long)$input;
  $1 = &temp;
%}
%typemap(out) long %{ $result = (long long)$1; %}
%typemap(csout, excode=SWIGEXCODE) long {
    long ret = $imcall;$excode
    return ret;
  }
// a value class's field (a property; SWIG's own getter reads an int)
%typemap(csvarout, excode=SWIGEXCODE2) long, const long& %{
    get {
      long ret = $imcall;$excode
      return ret;
    } %}
%typemap(ctype) long& "long long *"
%typemap(imtype) long& "ref long"
%typemap(cstype) long& "ref long"
%typemap(csin) long& "ref $csinput"
%typemap(in) long& (long temp) %{
  if (*$input < LONG_MIN || *$input > LONG_MAX) { NetVtk_SetPendingException("std::out_of_range", "the value doesn't fit a C long"); return $null; }
  temp = (long)*$input;
  $1 = &temp;
%}
%typemap(argout) long& %{ *$input = (long long)temp$argnum; %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const long& ""

/*
 * C unsigned long (VTK's event ids and observer tags): 32 bits on Windows, 64 on Linux and macOS. A C# ulong, 64 bits at the
 * P/Invoke layer; a value a 32-bit unsigned long can't hold throws VtkException (std::out_of_range).
 */
%typecheck(SWIG_TYPECHECK_UINT64) unsigned long, const unsigned long& ""
%typemap(ctype) unsigned long, const unsigned long& "unsigned long long"
%typemap(imtype) unsigned long, const unsigned long& "ulong"
%typemap(cstype) unsigned long, const unsigned long& "ulong"
%typemap(csin) unsigned long, const unsigned long& "$csinput"
%typemap(in) unsigned long %{
  if ($input > ULONG_MAX) { NetVtk_SetPendingException("std::out_of_range", "the value doesn't fit a C unsigned long"); return $null; }
  $1 = (unsigned long)$input;
%}
%typemap(in) const unsigned long& (unsigned long temp) %{
  if ($input > ULONG_MAX) { NetVtk_SetPendingException("std::out_of_range", "the value doesn't fit a C unsigned long"); return $null; }
  temp = (unsigned long)$input;
  $1 = &temp;
%}
%typemap(out) unsigned long %{ $result = (unsigned long long)$1; %}
%typemap(csout, excode=SWIGEXCODE) unsigned long {
    ulong ret = $imcall;$excode
    return ret;
  }
%typemap(csvarout, excode=SWIGEXCODE2) unsigned long, const unsigned long& %{
    get {
      ulong ret = $imcall;$excode
      return ret;
    } %}
%typemap(directorin) unsigned long "$input = (unsigned long long)$1;"
%typemap(csdirectorin) unsigned long "$iminput"

/*
 * size_t: SWIG's C# default is a 32-bit uint. A ulong in C#, pointer-sized at the P/Invoke layer (win-x86 is 32-bit): a
 * value a 32-bit size_t can't hold throws VtkException (std::out_of_range), as for C long. size_t& is in/out through a
 * UIntPtr.
 */
%typemap(ctype)  size_t, const size_t& "size_t"
%typemap(imtype) size_t, const size_t& "global::System.UIntPtr"
%typemap(cstype) size_t, const size_t& "ulong"
%typemap(csin)   size_t, const size_t& "global::Vtk.NativeSize.Of($csinput)"
%typemap(in)     size_t %{ $1 = ($1_ltype)$input; %}
%typemap(in)     const size_t& (size_t temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    size_t %{ $result = $1; %}
%typemap(out)    const size_t& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) size_t, const size_t& {
    ulong ret = (ulong)$imcall;$excode
    return ret;
  }
%typemap(csvarout, excode=SWIGEXCODE2) size_t, const size_t& %{
    get {
      ulong ret = (ulong)$imcall;$excode
      return ret;
    } %}
// a member's size_t& return is a ref UIntPtr into the object (References.i)
%typemap(ctype, out="void *") size_t& "size_t *"
%typemap(imtype, out="global::System.IntPtr") size_t& "ref global::System.UIntPtr"
%typemap(cstype, out="ref global::System.UIntPtr") size_t& "ref ulong"
// cshin: the ref also on the call to a constructor's SwigConstruct helper, which holds pre/post
%typemap(csin, pre="    global::System.UIntPtr temp$csinput = global::Vtk.NativeSize.Of($csinput);", post="      $csinput = temp$csinput.ToUInt64();",
         cshin="ref $csinput") size_t& "ref temp$csinput"
%typemap(in) size_t& %{ $1 = ($1_ltype)$input; %}

/*
 * Width typedefs. netvtk-gen spells pointers to them as written: a pointer to int64_t converts to no other
 * type where int64_t is long. SWIG gets no typedefs for them, which it would resolve in the wrappers; the
 * array typemaps below name them. intptr_t, uintptr_t and ptrdiff_t are pointer-sized: IntPtr and UIntPtr.
 */

%define %netvtk_pointer_sized(TYPE, CSTYPE)
%feature("novaluewrapper") TYPE;
%typemap(ctype)  TYPE, const TYPE& "TYPE"
%typemap(imtype) TYPE, const TYPE& "CSTYPE"
%typemap(cstype) TYPE, const TYPE& "CSTYPE"
%typemap(csin)   TYPE, const TYPE& "$csinput"
%typemap(in)     TYPE %{ $1 = $input; %}
%typemap(in)     const TYPE& ($*1_ltype temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    TYPE %{ $result = $1; %}
%typemap(out)    const TYPE& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) TYPE, const TYPE& {
    CSTYPE ret = $imcall;$excode
    return ret;
  }
%typemap(csvarout, excode=SWIGEXCODE2) TYPE, const TYPE& %{
    get {
      CSTYPE ret = $imcall;$excode
      return ret;
    } %}
%enddef

%netvtk_pointer_sized(intptr_t, global::System.IntPtr)
%netvtk_pointer_sized(uintptr_t, global::System.UIntPtr)
%netvtk_pointer_sized(ptrdiff_t, global::System.IntPtr)

/*
 * Pointer parameters to numbers: C# arrays, pinned for the call (null passes nullptr). The callee reads
 * and writes as many elements as its contract says; an array too short is as bad as in C++. netvtk-gen
 * passes a pointer the callee may keep (constructors, setters) as an address instead (NetVtk_Address).
 * Returned pointers are addresses too: C# can't know the length. The collections' bulk copies take them
 * too, with the length (bulk copies of data arrays).
 *
 *   TYPE* / const TYPE* / TYPE[N]     parameter  -> CSTYPE[]
 *   TYPE[N][M]                        parameter  -> CSTYPE[,]  (row-major, as C#)
 *   TYPE[N] / const TYPE[N]           a value class's field -> a property of a CSTYPE[] of N, copied both ways (one of
 *                                     another length doesn't go in); a const one read only
 *
 * char: char* is a byte buffer (const char* stays a string, Strings.i); char16_t: a UTF-16 char[]
 * (const char16_t* is a string); bool: one byte each.
 */
%define %netvtk_pointer_array(TYPE, CSTYPE)
%typemap(ctype)  TYPE *, TYPE [], TYPE [ANY][ANY] "TYPE *"
%typemap(ctype, out="void *") TYPE [ANY] "TYPE *"
%typemap(imtype) TYPE *, TYPE [] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[]"
%typemap(imtype, out="global::System.IntPtr") TYPE [ANY] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[]"
%typemap(imtype) TYPE [ANY][ANY] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[,]"
%typemap(cstype) TYPE *, TYPE [ANY], TYPE [] "CSTYPE[]"
%typemap(cstype) TYPE [ANY][ANY] "CSTYPE[,]"
%typemap(csin)   TYPE *, TYPE [], TYPE [ANY][ANY] "$csinput"
%typemap(csin)   TYPE [ANY] "global::Vtk.NativeArrays.Checked($csinput, $1_dim0, \"$csinput\")"
%typemap(in)     TYPE *, TYPE [ANY], TYPE [], TYPE [ANY][ANY] %{ $1 = ($1_ltype)$input; %}
%typemap(out)    TYPE [ANY] %{ $result = (void*)$1; %}
// a field's getter expands csout too, whose default would make an opaque class of the pointer
%typemap(csout, excode=SWIGEXCODE) TYPE [ANY] {
    global::System.IntPtr ptr = $imcall;$excode
    return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, $1_dim0);
  }
%typemap(csvarout, excode=SWIGEXCODE2) TYPE [ANY] %{
    get {
      global::System.IntPtr ptr = $imcall;$excode
      return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, $1_dim0);
    } %}
%enddef

%define %netvtk_const_pointer_array(TYPE, CSTYPE)
%typemap(ctype)  const TYPE *, const TYPE [], const TYPE [ANY][ANY] "TYPE *"
%typemap(ctype, out="void *") const TYPE [ANY] "TYPE *"
%typemap(imtype) const TYPE *, const TYPE [] "[global::System.Runtime.InteropServices.In] CSTYPE[]"
%typemap(imtype, out="global::System.IntPtr") const TYPE [ANY] "[global::System.Runtime.InteropServices.In] CSTYPE[]"
%typemap(imtype) const TYPE [ANY][ANY] "[global::System.Runtime.InteropServices.In] CSTYPE[,]"
%typemap(cstype) const TYPE *, const TYPE [ANY], const TYPE [] "CSTYPE[]"
%typemap(cstype) const TYPE [ANY][ANY] "CSTYPE[,]"
%typemap(csin)   const TYPE *, const TYPE [ANY], const TYPE [], const TYPE [ANY][ANY] "$csinput"
%typemap(in)     const TYPE *, const TYPE [ANY], const TYPE [], const TYPE [ANY][ANY] %{ $1 = ($1_ltype)$input; %}
%typemap(out)    const TYPE [ANY] %{ $result = (void*)$1; %}
// a field's getter expands csout too, whose default would make an opaque class of the pointer
%typemap(csout, excode=SWIGEXCODE) const TYPE [ANY] {
    global::System.IntPtr ptr = $imcall;$excode
    return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, $1_dim0);
  }
%typemap(csvarout, excode=SWIGEXCODE2) const TYPE [ANY] %{
    get {
      global::System.IntPtr ptr = $imcall;$excode
      return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, $1_dim0);
    } %}
%enddef

%define %netvtk_pointer_arrays(TYPE, CSTYPE)
%netvtk_pointer_array(TYPE, CSTYPE)
%netvtk_const_pointer_array(TYPE, CSTYPE)
%enddef

%netvtk_pointer_arrays(double, double)
%netvtk_pointer_arrays(int64_t, long)
%netvtk_pointer_arrays(uint64_t, ulong)
%netvtk_pointer_arrays(int32_t, int)
%netvtk_pointer_arrays(uint32_t, uint)
%netvtk_pointer_arrays(int16_t, short)
%netvtk_pointer_arrays(uint16_t, ushort)
%netvtk_pointer_arrays(int8_t, sbyte)
%netvtk_pointer_arrays(uint8_t, byte)
%netvtk_pointer_arrays(intptr_t, global::System.IntPtr)
%netvtk_pointer_arrays(uintptr_t, global::System.UIntPtr)
%netvtk_pointer_arrays(ptrdiff_t, global::System.IntPtr)
%netvtk_pointer_arrays(float, float)
%netvtk_pointer_arrays(int, int)
%netvtk_pointer_arrays(unsigned int, uint)
%netvtk_pointer_arrays(short, short)
%netvtk_pointer_arrays(unsigned short, ushort)
%netvtk_pointer_arrays(long long, long)
%netvtk_pointer_arrays(unsigned long long, ulong)
%netvtk_pointer_arrays(signed char, sbyte)
%netvtk_pointer_arrays(unsigned char, byte)
%netvtk_pointer_array(char, byte)
%netvtk_pointer_array(char16_t, char)
%netvtk_pointer_arrays(bool, bool)
// bool: bytes P/Invoke pins, copied from and back to the C# array (NativeArrays), since the marshaler's bool[] conversion
// differs between runtimes (CLR 2 writes back four-byte BOOLs)
%typemap(imtype) bool *, bool [ANY], bool [], const bool *, const bool [ANY], const bool [] "byte[]"
%typemap(csin, pre="    byte[] temp$csinput = global::Vtk.NativeArrays.ToBytes($csinput);",
         post="      global::Vtk.NativeArrays.CopyBack(temp$csinput, $csinput);")
         bool *, bool [ANY], bool [], const bool *, const bool [ANY], const bool [] "temp$csinput"
%typemap(imtype, inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = global::System.Runtime.InteropServices.UnmanagedType.U2)]")
         char16_t *, char16_t [ANY], char16_t [] "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] char[]"

/*
 * Addresses: IntPtr, for void* and for pointers C# can't type (a pointer the callee may keep, a function,
 * a pointer to a pointer).
 *
 *   void* / const void*                  parameter, return, a value class's field  -> IntPtr
 *   NetVtk_Address< T >                  parameter, return  -> IntPtr; converts to and from T in the wrapper
 *   NetVtk_AddressRef< T >               parameter          -> ref IntPtr; binds to a T& (T*&: the callee may replace it)
 *
 * netvtk-gen declares the pointers as NetVtk_Address< T > and calls %netvtk_address(T) once per module,
 * with %arg(): a function type holds commas.
 */
%typemap(ctype)  void *, const void * "void *"
%typemap(imtype) void *, const void * "global::System.IntPtr"
%typemap(cstype) void *, const void * "global::System.IntPtr"
%typemap(csin)   void *, const void * "$csinput"
%typemap(in)     void *, const void * %{ $1 = ($1_ltype)$input; %}
%typemap(out)    void *, const void * %{ $result = (void *)$1; %}
%typemap(csout, excode=SWIGEXCODE) void *, const void * {
    global::System.IntPtr ret = $imcall;$excode
    return ret;
  }
// a value class's field (netvtk-gen declares any pointer C# can't type void*, read only unless it is one)
%typemap(csvarout, excode=SWIGEXCODE2) void *, const void * %{
    get {
      global::System.IntPtr ret = $imcall;$excode
      return ret;
    } %}
// director callbacks (Directors.i): an address reaches the C# override as it is (vtkCommand::Execute's callData)
%typemap(directorin) void *, const void * "$input = (void *)$1;"
%typemap(csdirectorin) void *, const void * "$iminput"

%{
// an address C# passes as IntPtr, for a pointer type C# can't type
template <class T> struct NetVtk_Address {
  void* myAddress;
  NetVtk_Address() : myAddress(nullptr) {}
  NetVtk_Address(T theAddress) : myAddress((void*)theAddress) {}
  operator T() const { return (T)myAddress; }
};

// the slot of an address the callee may replace: a T* the caller's IntPtr holds
template <class T> struct NetVtk_AddressRef {
  void** mySlot;
  NetVtk_AddressRef() : mySlot(nullptr) {}
  explicit NetVtk_AddressRef(void** theSlot) : mySlot(theSlot) {}
  operator T&() const { return *static_cast<T*>(static_cast<void*>(mySlot)); }
};

// a C array a function takes by reference (int (&)[3]): the elements of the C# array P/Invoke passes for the call
template <class T, int N> struct NetVtk_ArrayRef {
  typedef T Array[N];
  T* myData;
  NetVtk_ArrayRef() : myData(nullptr) {}
  explicit NetVtk_ArrayRef(T* theData) : myData(theData) {}
  operator Array&() const { return *reinterpret_cast<Array*>(myData); }
};
%}

template <class T> struct NetVtk_Address;
template <class T> struct NetVtk_AddressRef;
template <class T, int N> struct NetVtk_ArrayRef;

%define %netvtk_address(TYPE)
%typemap(ctype)  NetVtk_Address< TYPE > "void *"
%typemap(imtype) NetVtk_Address< TYPE > "global::System.IntPtr"
%typemap(cstype) NetVtk_Address< TYPE > "global::System.IntPtr"
%typemap(csin)   NetVtk_Address< TYPE > "$csinput"
%typemap(in)     NetVtk_Address< TYPE > %{ $1 = NetVtk_Address< TYPE >((TYPE)$input); %}
%typemap(out)    NetVtk_Address< TYPE > %{ $result = ((const NetVtk_Address< TYPE >&)$1).myAddress; %}
%typemap(csout, excode=SWIGEXCODE) NetVtk_Address< TYPE > {
    global::System.IntPtr ret = $imcall;$excode
    return ret;
  }
%enddef

/*
 * T (&)[N] parameter (const too) -> a C# array of N elements, checked: the callee reads and writes it in place. netvtk-gen
 * declares it as NetVtk_ArrayRef< T, N > and calls the macro per instantiation; bool goes through bytes, as bool* does.
 */
%define %netvtk_array_ref(TYPE, N, CSTYPE)
%typemap(ctype)  NetVtk_ArrayRef< TYPE, N > "TYPE *"
%typemap(imtype) NetVtk_ArrayRef< TYPE, N > "[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.Out] CSTYPE[]"
%typemap(cstype) NetVtk_ArrayRef< TYPE, N > "CSTYPE[]"
%typemap(csin)   NetVtk_ArrayRef< TYPE, N > "global::Vtk.NativeArrays.Checked($csinput, N, \"$csinput\")"
%typemap(in)     NetVtk_ArrayRef< TYPE, N > %{ $1 = NetVtk_ArrayRef< TYPE, N >($input); %}
%enddef

%define %netvtk_bool_array_ref(N)
%netvtk_array_ref(bool, N, bool)
%typemap(imtype) NetVtk_ArrayRef< bool, N > "byte[]"
%typemap(csin, pre="    byte[] temp$csinput = global::Vtk.NativeArrays.ToBytes(global::Vtk.NativeArrays.Checked($csinput, N, \"$csinput\"));",
         post="      global::Vtk.NativeArrays.CopyBack(temp$csinput, $csinput);")
         NetVtk_ArrayRef< bool, N > "temp$csinput"
%enddef

%define %netvtk_address_ref(TYPE)
%typemap(ctype)  NetVtk_AddressRef< TYPE > "void **"
%typemap(imtype) NetVtk_AddressRef< TYPE > "ref global::System.IntPtr"
%typemap(cstype) NetVtk_AddressRef< TYPE > "ref global::System.IntPtr"
%typemap(csin)   NetVtk_AddressRef< TYPE > "ref $csinput"
%typemap(in)     NetVtk_AddressRef< TYPE > %{ $1 = NetVtk_AddressRef< TYPE >($input); %}
%enddef

/*
 * char16_t: a UTF-16 code unit, C# char. P/Invoke would marshal a char as one ANSI byte, so the
 * P/Invoke layer passes a ushort. Its typecheck (C# char's) tells SWIG it overloads a const char16_t*, a string
 * (Strings.i): without, both rank as pointers, and SWIG drops one (Warning 516).
 */
%typecheck(SWIG_TYPECHECK_CHAR) char16_t, const char16_t& ""
%typemap(ctype)  char16_t, const char16_t& "char16_t"
%typemap(imtype) char16_t, const char16_t& "ushort"
%typemap(cstype) char16_t, const char16_t& "char"
%typemap(csin)   char16_t, const char16_t& "(ushort)$csinput"
%typemap(in)     char16_t %{ $1 = $input; %}
%typemap(in)     const char16_t& ($*1_ltype temp) %{ temp = $input; $1 = &temp; %}
%typemap(out)    char16_t %{ $result = $1; %}
%typemap(out)    const char16_t& %{ $result = *$1; %}
%typemap(csout, excode=SWIGEXCODE) char16_t, const char16_t& {
    char ret = (char)$imcall;$excode
    return ret;
  }
%typemap(csvarout, excode=SWIGEXCODE2) char16_t, const char16_t& %{
    get {
      char ret = (char)$imcall;$excode
      return ret;
    } %}
%typemap(ctype)  char16_t& "char16_t *"
%typemap(imtype) char16_t& "ref ushort"
%typemap(cstype) char16_t& "ref char"
%typemap(csin, pre="    ushort temp$csinput = (ushort)$csinput;", post="      $csinput = (char)temp$csinput;", cshin="ref $csinput") char16_t& "ref temp$csinput"
%typemap(in)     char16_t& %{ $1 = ($1_ltype)$input; %}
