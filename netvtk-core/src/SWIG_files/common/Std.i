// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Standard library types VTK's signatures use (strings: Strings.i). netvtk-gen calls a macro per instantiation, before
 * the declarations. An array, optional or complex number returned is copied into a thread-local buffer first, like a
 * struct (ValueTypes.i).
 *
 *   std::streampos                   long, a byte offset
 *   %netvtk_bitset(N)                std::bitset<N> (N <= 64): a ulong mask, bit i the i-th flag
 *   %netvtk_std_array(T, N, CSTYPE)  std::array<T, N> of numbers: a C# array of N elements; a non-const & writes it back;
 *                                    a value class's field of one a property
 *   %netvtk_optional(T, CSTYPE)      std::optional<T> of a number, enum or struct: a C# nullable
 *   std::complex<double>             Vtk.Complex, a C# struct with its layout: passed like a value type (ValueTypes.i)
 *   %netvtk_pair(NAME, T1, T2, CS1, CS2)
 *                                    std::pair<T1, T2>: a class with First and Second, copies of the values; netvtk-gen
 *                                    instantiates it (a class's alias, or std_pair_<T1>_<T2>)
 *   %netvtk_vector(T, CSTYPE)        std::vector<T> of numbers or structs: a C# array (below)
 *   std::vector<bool>, std::vector<std::string>
 *                                    bool[] and string[] (UTF-8)
 *   %netvtk_valueclass_vector(T)     std::vector<T> of a value class: a C# array of copies, each owned by its proxy
 *   %netvtk_object_vector(T)         std::vector<T*> and std::vector<vtkSmartPointer<T>> of VTK objects (VtkObjects.i)
 */

// declared for %naturalvar, which makes a value class's field of them a property of const& getters and setters
namespace std {
template <class T, size_t N> class array;
template <class T> class vector;
}

%{
#include <algorithm>
#include <array>
#include <bitset>
#include <complex>
#include <cstdint>
#include <cstring>
#include <ios>
#include <optional>
#include <string>
#include <utility>
#include <vector>
%}

%typemap(ctype)  std::streampos "long long"
%typemap(imtype) std::streampos "long"
%typemap(cstype) std::streampos "long"
%typemap(csin)   std::streampos "$csinput"
%typemap(in)     std::streampos %{ $1 = std::streampos(static_cast<std::streamoff>($input)); %}
%typemap(out)    std::streampos %{ $result = static_cast<long long>(static_cast<std::streamoff>($1)); %}
%typemap(csout, excode=SWIGEXCODE) std::streampos {
    long ret = $imcall;$excode
    return ret;
  }

// a C# struct of its layout, like a value type
%netvtk_struct(std::complex<double>, global::Vtk.Complex)

%define %netvtk_bitset(N)
%typemap(ctype)  std::bitset< N >, const std::bitset< N >& "unsigned long long"
%typemap(imtype) std::bitset< N >, const std::bitset< N >& "ulong"
%typemap(cstype) std::bitset< N >, const std::bitset< N >& "ulong"
%typemap(csin)   std::bitset< N >, const std::bitset< N >& "$csinput"
%typemap(in) std::bitset< N > %{ $1 = std::bitset< N >($input); %}
%typemap(in) const std::bitset< N >& (std::bitset< N > temp) %{ temp = std::bitset< N >($input); $1 = &temp; %}
%typemap(out) std::bitset< N > %{ $result = static_cast<const std::bitset< N >&>($1).to_ullong(); %}
%typemap(out) const std::bitset< N >& %{ $result = $1->to_ullong(); %}
%typemap(csout, excode=SWIGEXCODE) std::bitset< N >, const std::bitset< N >& {
    ulong ret = $imcall;$excode
    return ret;
  }
%enddef

%define %netvtk_std_array(TYPE, N, CSTYPE)
%typemap(ctype, out="void *") std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "CSTYPE[]"
%typemap(cstype) std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >& "CSTYPE[]"
%typemap(csin) std::array< TYPE, N >, const std::array< TYPE, N >&, std::array< TYPE, N >&
         "global::Vtk.NativeArrays.Checked($csinput, N, \"$csinput\")"
%typemap(in) std::array< TYPE, N > %{ std::copy($input, $input + N, $1.begin()); %}
// each type its local ($*1_ltype: the array without const)
%typemap(in) const std::array< TYPE, N >& ($*1_ltype temp), std::array< TYPE, N >& ($*1_ltype temp) %{
  std::copy($input, $input + N, temp.begin());
  $1 = &temp;
%}
%typemap(argout) std::array< TYPE, N >& %{ std::copy(temp$argnum.begin(), temp$argnum.end(), $input); %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const std::array< TYPE, N >& ""
%typemap(out) std::array< TYPE, N > %{
  { std::array< TYPE, N >& netvtk_ret = NetVtk_Slot< std::array< TYPE, N > >(); netvtk_ret = $1; $result = netvtk_ret.data(); }
%}
%typemap(out) const std::array< TYPE, N >& %{
  { std::array< TYPE, N >& netvtk_ret = NetVtk_Slot< std::array< TYPE, N > >(); netvtk_ret = *$1; $result = netvtk_ret.data(); }
%}
%typemap(csout, excode=SWIGEXCODE) std::array< TYPE, N >, const std::array< TYPE, N >& {
    global::System.IntPtr ptr = $imcall;$excode
    var ret = new CSTYPE[N];
    unsafe { for (int i = 0; i < N; i++) ret[i] = ((CSTYPE*)ptr)[i]; }
    return ret;
  }
// a value class's field (a property): read through the const& return, written through the const& parameter
%typemap(csvarout, excode=SWIGEXCODE2) const std::array< TYPE, N >& %{
    get {
      global::System.IntPtr ptr = $imcall;$excode
      return global::Vtk.NativeArrays.CopyOut<CSTYPE>(ptr, N);
    } %}
%enddef

// in: a C# array of one element, or null, which the marshaler pins; out: the value in the thread-local buffer, or null
%define %netvtk_optional(TYPE, CSTYPE)
%typemap(ctype, out="void *") std::optional< TYPE >, const std::optional< TYPE >& "TYPE *"
%typemap(imtype, out="global::System.IntPtr") std::optional< TYPE >, const std::optional< TYPE >& "CSTYPE[]"
%typemap(cstype) std::optional< TYPE >, const std::optional< TYPE >& "CSTYPE?"
%typemap(csin) std::optional< TYPE >, const std::optional< TYPE >& "$csinput.HasValue ? new CSTYPE[] { $csinput.Value } : null"
%typemap(in) std::optional< TYPE > %{ if ($input) $1 = *$input; %}
%typemap(in) const std::optional< TYPE >& (std::optional< TYPE > temp) %{
  if ($input) temp = *$input;
  $1 = &temp;
%}
%typemap(out) std::optional< TYPE > %{
  { std::optional< TYPE >& netvtk_ret = NetVtk_Slot< std::optional< TYPE > >(); netvtk_ret = $1; $result = netvtk_ret ? (void*)&*netvtk_ret : nullptr; }
%}
%typemap(out) const std::optional< TYPE >& %{
  { std::optional< TYPE >& netvtk_ret = NetVtk_Slot< std::optional< TYPE > >(); netvtk_ret = *$1; $result = netvtk_ret ? (void*)&*netvtk_ret : nullptr; }
%}
%typemap(csout, excode=SWIGEXCODE) std::optional< TYPE >, const std::optional< TYPE >& {
    global::System.IntPtr ptr = $imcall;$excode
    unsafe { return ptr == global::System.IntPtr.Zero ? (CSTYPE?)null : *(CSTYPE*)ptr; }
  }
%enddef

// the accessors are %extend: a pair can't hold a nested enum, whose flat alias SWIG's casts ("enum X") can't follow
%csmethodmodifiers NetVtk_First "internal";
%csmethodmodifiers NetVtk_Second "internal";

namespace std {
template <class T1, class T2> struct pair {
  pair();
  pair(const T1& theFirst, const T2& theSecond);
  %extend {
    T1 NetVtk_First() const { return $self->first; }
    T2 NetVtk_Second() const { return $self->second; }
  }
};
}

%define %netvtk_pair(NAME, T1, T2, CS1, CS2)
%typemap(cscode) std::pair< T1, T2 > %{
  public CS1 First => NetVtk_First();

  public CS2 Second => NetVtk_Second();
%}
%netvtk_valueclass(%arg(std::pair< T1, T2 >))
%template(NAME) std::pair< T1, T2 >;
%enddef

/*
 * std::vector<T> -> a C# array, copied both ways. A parameter by value or const& goes in; a non-const & and a pointer go
 * in and come back (ref CSTYPE[]; a null array is the pointer's nullptr); a return (by value, by reference or by pointer,
 * null for nullptr) comes back, and so does a value class's field, a property (written too, unless const). C# passes the
 * elements in a block of bytes P/Invoke pins (NativeArrays): their count, a cell for the address of what comes back, the
 * elements. What comes back is a copy in a thread-local buffer, the elements' count and address (NetVtk_Vector), which C#
 * reads before its next native call.
 */

%{
// a vector's block from C#: the elements' count, a cell for what comes back, the elements
inline std::size_t NetVtk_BlockCount(const void* theBlock)
{
  long long aCount = 0;
  std::memcpy(&aCount, theBlock, sizeof(aCount));
  return aCount > 0 ? static_cast<std::size_t>(aCount) : 0;
}

inline const unsigned char* NetVtk_BlockElements(const void* theBlock)
{
  return static_cast<const unsigned char*>(theBlock) + 2 * sizeof(long long);
}

// the address of a block's element (VTK objects, value classes)
inline void* NetVtk_BlockAddress(const void* theBlock, std::size_t theIndex)
{
  long long anAddress = 0;
  std::memcpy(&anAddress, NetVtk_BlockElements(theBlock) + theIndex * sizeof(anAddress), sizeof(anAddress));
  return reinterpret_cast<void*>(static_cast<std::intptr_t>(anAddress));
}

// what comes back for a non-const reference or pointer: into its block's cell
inline void NetVtk_BlockResult(void* theBlock, const void* theVector)
{
  const long long anAddress = static_cast<long long>(reinterpret_cast<std::intptr_t>(theVector));
  std::memcpy(static_cast<unsigned char*>(theBlock) + sizeof(anAddress), &anAddress, sizeof(anAddress));
}

// a vector for C#: its elements' count and address, in this thread's buffer of a position (NetVtk_Slot)
struct NetVtk_Vector
{
  long long Count;
  const void* Elements;
};

template <int Position>
void* NetVtk_VectorOf(std::size_t theCount, const void* theElements)
{
  NetVtk_Vector& aVector = NetVtk_Slot< NetVtk_Vector, Position >();
  aVector.Count = static_cast<long long>(theCount);
  aVector.Elements = theElements;
  return &aVector;
}

// numbers and structs: C# lays them out as C++ does
template <class T>
void NetVtk_VectorIn(const void* theBlock, std::vector<T>& theVector)
{
  theVector.resize(NetVtk_BlockCount(theBlock));
  if (!theVector.empty())
  {
    std::memcpy(static_cast<void*>(theVector.data()), NetVtk_BlockElements(theBlock), theVector.size() * sizeof(T));
  }
}

template <int Position, class T>
void* NetVtk_VectorOut(const std::vector<T>& theVector)
{
  std::vector<T>& aCopy = NetVtk_Slot< std::vector<T>, Position >();
  aCopy = theVector;
  return NetVtk_VectorOf< Position >(aCopy.size(), aCopy.data());
}

// bool: a byte each
inline void NetVtk_VectorIn(const void* theBlock, std::vector<bool>& theVector)
{
  const unsigned char* anElements = NetVtk_BlockElements(theBlock);
  theVector.assign(NetVtk_BlockCount(theBlock), false);
  for (std::size_t i = 0; i < theVector.size(); ++i)
  {
    theVector[i] = anElements[i] != 0;
  }
}

template <int Position>
void* NetVtk_VectorOut(const std::vector<bool>& theVector)
{
  std::vector<unsigned char>& aCopy = NetVtk_Slot< std::vector<unsigned char>, Position >();
  aCopy.assign(theVector.begin(), theVector.end());
  return NetVtk_VectorOf< Position >(aCopy.size(), aCopy.data());
}

// strings: in, each its UTF-8 bytes' count and the bytes; out, the addresses of null-terminated copies
inline void NetVtk_VectorIn(const void* theBlock, std::vector<std::string>& theVector)
{
  const unsigned char* aText = NetVtk_BlockElements(theBlock);
  theVector.resize(NetVtk_BlockCount(theBlock));
  for (std::string& anItem : theVector)
  {
    long long aLength = 0;
    std::memcpy(&aLength, aText, sizeof(aLength));
    anItem.assign(reinterpret_cast<const char*>(aText + sizeof(aLength)), static_cast<std::size_t>(aLength));
    aText += sizeof(aLength) + static_cast<std::size_t>(aLength);
  }
}

template <int Position>
void* NetVtk_VectorOut(const std::vector<std::string>& theVector)
{
  std::vector<std::string>& aCopy = NetVtk_Slot< std::vector<std::string>, Position >();
  std::vector<const char*>& aTexts = NetVtk_Slot< std::vector<const char*>, Position >();
  aCopy = theVector;
  aTexts.clear();
  for (const std::string& anItem : aCopy)
  {
    aTexts.push_back(anItem.c_str());
  }
  return NetVtk_VectorOf< Position >(aTexts.size(), aTexts.data());
}

// value classes: in, copies of the objects whose addresses C# passes; out, copies, each owned by its proxy
template <class T>
void NetVtk_CopiesIn(const void* theBlock, std::vector<T>& theVector)
{
  const std::size_t aCount = NetVtk_BlockCount(theBlock);
  theVector.clear();
  theVector.reserve(aCount);
  for (std::size_t i = 0; i < aCount; ++i)
  {
    theVector.push_back(*static_cast<const T*>(NetVtk_BlockAddress(theBlock, i)));
  }
}

template <int Position, class T>
void* NetVtk_CopiesOut(const std::vector<T>& theVector)
{
  std::vector<void*>& anAddresses = NetVtk_Slot< std::vector<void*>, Position >();
  anAddresses.clear();
  for (const T& anItem : theVector)
  {
    anAddresses.push_back(new T(anItem));
  }
  return NetVtk_VectorOf< Position >(anAddresses.size(), anAddresses.data());
}
%}

/*
 * VECTOR's typemaps: CSIN makes the block of a C# array ($csinput), CSOUT the C# array of a vector that comes back (ptr,
 * null for zero); IN(block, vector) fills a vector in C++, OUT< Position >(vector) gives one to C# (NetVtk_Slot's
 * positions: 0 the return, n argument n).
 */
%define %netvtk_vector_typemaps(VECTOR, CSTYPE, CSIN, CSOUT, IN, OUT)
%typemap(ctype, out="void *") VECTOR, const VECTOR&, VECTOR&, VECTOR* "void *"
%typemap(imtype, out="global::System.IntPtr") VECTOR, const VECTOR&, VECTOR&, VECTOR* "byte[]"
%typemap(cstype) VECTOR, const VECTOR& "CSTYPE[]"
%typemap(cstype, out="CSTYPE[]") VECTOR&, VECTOR* "ref CSTYPE[]"
// the C# array, and the proxies it holds, live until the call is over
%typemap(csin, post="      global::System.GC.KeepAlive($csinput);") VECTOR, const VECTOR& "CSIN"
// cshin: constructors call a SwigConstruct helper that holds pre/post; its call needs the ref too
%typemap(csin, pre="    byte[] block$csinput = CSIN;",
         post="      global::System.GC.KeepAlive($csinput);"
              " { global::System.IntPtr ptr = global::Vtk.NativeArrays.Result(block$csinput); if (ptr != global::System.IntPtr.Zero) $csinput = CSOUT; }",
         cshin="ref $csinput") VECTOR& "block$csinput"
%typemap(csin, pre="    byte[] block$csinput = $csinput == null ? null : CSIN;",
         post="      global::System.GC.KeepAlive($csinput);"
              " { global::System.IntPtr ptr = global::Vtk.NativeArrays.Result(block$csinput); if (ptr != global::System.IntPtr.Zero) $csinput = CSOUT; }",
         cshin="ref $csinput") VECTOR* "block$csinput"
// by value: SWIG holds a class it has no definition of in a SwigValueWrapper, which *& reaches into
%typemap(in) VECTOR (VECTOR temp) %{
  IN($input, temp);
  $1 = SWIG_STD_MOVE(temp);
%}
%typemap(in) const VECTOR& (VECTOR temp), VECTOR& (VECTOR temp) %{
  IN($input, temp);
  $1 = &temp;
%}
%typemap(in) VECTOR* (VECTOR temp) %{
  $1 = 0;
  if ($input)
  {
    IN($input, temp);
    $1 = &temp;
  }
%}
%typemap(argout) VECTOR&, VECTOR* %{ if ($1) NetVtk_BlockResult($input, OUT< $argnum >(*$1)); %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const VECTOR& ""
%typemap(out) VECTOR %{ $result = OUT< 0 >(*&$1); %}
%typemap(out) const VECTOR&, VECTOR& %{ $result = OUT< 0 >(*$1); %}
%typemap(out) VECTOR* %{ $result = $1 ? OUT< 0 >(*$1) : 0; %}
// the array before the exception check: its proxies take what the wrapper gave them either way
%typemap(csout, excode=SWIGEXCODE) VECTOR, const VECTOR&, VECTOR&, VECTOR* {
    global::System.IntPtr ptr = $imcall;
    CSTYPE[] ret = CSOUT;$excode
    return ret;
  }
// a value class's field (netvtk-gen declares it VECTOR, const if it is): read through the const& return, written through
// the const& parameter
%typemap(csvarout, excode=SWIGEXCODE2) const VECTOR& %{
    get {
      global::System.IntPtr ptr = $imcall;
      CSTYPE[] ret = CSOUT;$excode
      return ret;
    } %}
%typemap(csvarin, excode=SWIGEXCODE2) const VECTOR& %{
    set {
      $imcall;$excode
      global::System.GC.KeepAlive(value);
    } %}
%enddef

%define %netvtk_vector(TYPE, CSTYPE)
%netvtk_vector_typemaps(%arg(std::vector< TYPE >), CSTYPE, global::Vtk.NativeArrays.Block($csinput),
                        global::Vtk.NativeArrays.Vector<CSTYPE>(ptr), NetVtk_VectorIn, NetVtk_VectorOut)
%enddef

%netvtk_vector_typemaps(std::vector< bool >, bool, global::Vtk.NativeArrays.Block($csinput),
                        global::Vtk.NativeArrays.Bools(ptr), NetVtk_VectorIn, NetVtk_VectorOut)
%netvtk_vector_typemaps(std::vector< std::string >, string, global::Vtk.NativeArrays.Block($csinput),
                        global::Vtk.NativeArrays.Strings(ptr), NetVtk_VectorIn, NetVtk_VectorOut)

// a null element doesn't go in: C++ can't copy it
%define %netvtk_valueclass_vector(TYPE)
%netvtk_vector_typemaps(%arg(std::vector< TYPE >), TYPE,
                        %arg(global::Vtk.NativeArrays.Copies($csinput, TYPE.getCPtr, nameof($csinput))),
                        %arg(global::Vtk.NativeArrays.Proxies(ptr, netvtkItem => new TYPE(netvtkItem, true))),
                        NetVtk_CopiesIn, NetVtk_CopiesOut)
%enddef
