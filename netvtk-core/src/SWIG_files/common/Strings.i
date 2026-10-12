// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Strings, in both directions: UTF-8 (const char*, std::string) and UTF-16 (const char16_t*). VTK reads file paths as
 * UTF-8, while default P/Invoke char* marshaling is ANSI on Windows (breaks C:\Daten\Übersicht\...). netstandard2.0 has
 * no UnmanagedType.LPUTF8Str, so encode/decode by hand.
 */

// a ref string parameter: the C# string, copied natively (ALLOC), goes in through the slot, and what the slot points to
// after the call comes back (DECODE); a slot the callee didn't change (after a C++ exception) gives the input back.
// cshin: constructors call a SwigConstruct helper that holds pre/post; its call needs the ref too.
%define %netvtk_ref_string_csin(TYPE, ALLOC, DECODE)
%typemap(csin,
         pre="    global::System.IntPtr buffer$csinput = ALLOC($csinput); global::System.IntPtr slot$csinput = buffer$csinput;",
         post="      $csinput = DECODE(slot$csinput); global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer$csinput);",
         cshin="ref $csinput")
         TYPE "ref slot$csinput"
%enddef

// const char* parameters: null-terminated UTF-8 byte[] (pinned, no copy on the native side)
%typemap(ctype, out="void *") const char*, const char* const "char *"
%typemap(imtype, out="global::System.IntPtr") const char*, const char* const "byte[]"
%typemap(cstype) const char*, const char* const "string"
%typemap(csin) const char*, const char* const "global::Vtk.Utf8.Encode($csinput)"
%typemap(in) const char*, const char* const %{ $1 = ($1_ltype)$input; %}

// const char* returns: decoded immediately (the pointer is owned by VTK)
%typemap(out) const char*, const char* const %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) const char*, const char* const {
    string ret = global::Vtk.Utf8.Decode($imcall);$excode
    return ret;
  }

/*
 * const char*& -> ref string: in, a UTF-8 copy the callee may read and move along; out, whatever the pointer then points
 * to, decoded right after the call. After a C++ exception the variable keeps its value.
 */
%typemap(ctype, out="void *")  const char*& "const char **"
%typemap(imtype, out="global::System.IntPtr") const char*& "ref global::System.IntPtr"
%typemap(cstype, out="ref global::System.IntPtr") const char*& "ref string"
%netvtk_ref_string_csin(const char*&, global::Vtk.Utf8.Alloc, global::Vtk.Utf8.Decode)
%typemap(in) const char*& (const char* temp) %{ temp = *$input; $1 = ($1_ltype)&temp; %}
%typemap(argout) const char*& %{ *$input = temp$argnum; %}
// SWIG falls back from const T& to T& for typemaps missing on const T&
%typemap(argout) const char* const& ""
// a member's const char*& return: a ref IntPtr into the object, as for other pointers (References.i)
%netvtk_ref_out(const char*&, global::System.IntPtr)

// const char16_t*: UTF-16 in both directions
%typecheck(SWIG_TYPECHECK_STRING) const char16_t*, const char16_t* const ""
%typemap(ctype, out="void *") const char16_t*, const char16_t* const "const char16_t *"
%typemap(imtype, out="global::System.IntPtr",
         inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)]")
         const char16_t*, const char16_t* const "string"
%typemap(cstype) const char16_t*, const char16_t* const "string"
%typemap(csin) const char16_t*, const char16_t* const "$csinput"
%typemap(in) const char16_t*, const char16_t* const %{ $1 = ($1_ltype)$input; %}
%typemap(out) const char16_t*, const char16_t* const %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) const char16_t*, const char16_t* const {
    global::System.IntPtr ptr = $imcall;$excode
    return global::System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
  }

/*
 * std::string and std::string_view: UTF-8, like const char*. A const std::stringstream& is its text, a UTF-8 string too.
 * Returns are copied into a thread-local buffer first.
 */
%{
#include <sstream>
#include <string>
#include <string_view>
%}

%typemap(ctype, out="void *") std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "char *"
%typemap(imtype, out="global::System.IntPtr") std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "byte[]"
%typemap(cstype) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "string"
%typemap(csin) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& "global::Vtk.Utf8.Encode($csinput)"
%typemap(in) std::string %{ if ($input) $1 = std::string((const char*)$input); %}
%typemap(in) const std::string& (std::string temp) %{
  if ($input) temp = (const char*)$input;
  $1 = &temp;
%}
// the pinned C# bytes, for the call
%typemap(in) std::string_view %{ if ($input) $1 = std::string_view((const char*)$input); %}
%typemap(in) const std::string_view& (std::string_view temp) %{
  if ($input) temp = std::string_view((const char*)$input);
  $1 = &temp;
%}
%typemap(in) const std::stringstream& (std::stringstream temp) %{
  if ($input) temp << (const char*)$input;
  $1 = &temp;
%}
%typemap(out, null="0") std::string, std::string_view %{
  { std::string& netvtk_ret = NetVtk_Slot< std::string >(); netvtk_ret = $1; $result = (void*)netvtk_ret.c_str(); }
%}
%typemap(out, null="0") const std::string&, const std::string_view& %{
  { std::string& netvtk_ret = NetVtk_Slot< std::string >(); netvtk_ret = *$1; $result = (void*)netvtk_ret.c_str(); }
%}
%typemap(out, null="0") const std::stringstream& %{
  { std::string& netvtk_ret = NetVtk_Slot< std::string >(); netvtk_ret = $1->str(); $result = (void*)netvtk_ret.c_str(); }
%}
%typemap(csout, excode=SWIGEXCODE) std::string, const std::string&, std::string_view, const std::string_view&, const std::stringstream& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::Vtk.Utf8.Decode(ptr);
  }

// director callbacks (Directors.i): a string parameter reaches the C# override decoded
%typemap(directorin) const char*, const char* const "$input = (void*)$1;"
%typemap(csdirectorin) const char*, const char* const "global::Vtk.Utf8.Decode($iminput)"
