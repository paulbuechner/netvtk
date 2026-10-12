// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * std::ostream& / std::istream& -> System.IO.Stream, lent for the call; std::ostream* / std::istream* the same, null for
 * nullptr. VTK writes into a native memory stream whose bytes then go to the C# stream (PrintSelf, writers), or reads the
 * C# stream's remaining bytes in place; a seekable C# stream then stands where VTK stopped. The memory streams
 * (src/Native/NetVtkStreams.cxx) seek, which binary formats need.
 *
 * netvtk-gen leaves stream parameters out where VTK may keep the stream: constructors, and classes with a stream member.
 */

%{
#include <istream>
#include <ostream>
%}

namespace std {
class ostream;
class istream;
}

%typemap(ctype)  std::ostream&, std::istream& "void *"
%typemap(imtype) std::ostream&, std::istream& "global::System.IntPtr"
%typemap(cstype) std::ostream&, std::istream& "global::System.IO.Stream"
%typemap(csin,
         pre="    global::Vtk.NativeOutput output$csinput = global::Vtk.NativeOutput.For($csinput);",
         post="      output$csinput.CopyToAndFree($csinput);")
         std::ostream& "output$csinput.Handle"
%typemap(csin,
         pre="    global::Vtk.NativeInput input$csinput = global::Vtk.NativeInput.For($csinput);",
         post="      input$csinput.Free($csinput);")
         std::istream& "input$csinput.Handle"
%typemap(in) std::ostream& %{ $1 = static_cast<std::ostream*>($input); %}
%typemap(in) std::istream& %{ $1 = static_cast<std::istream*>($input); %}

%typemap(ctype)  std::ostream*, std::istream* "void *"
%typemap(imtype) std::ostream*, std::istream* "global::System.IntPtr"
%typemap(cstype) std::ostream*, std::istream* "global::System.IO.Stream"
%typemap(csin,
         pre="    global::Vtk.NativeOutput output$csinput = $csinput == null ? null : global::Vtk.NativeOutput.For($csinput);",
         post="      if (output$csinput != null) output$csinput.CopyToAndFree($csinput);")
         std::ostream* "(output$csinput == null ? global::System.IntPtr.Zero : output$csinput.Handle)"
%typemap(csin,
         pre="    global::Vtk.NativeInput input$csinput = $csinput == null ? null : global::Vtk.NativeInput.For($csinput);",
         post="      if (input$csinput != null) input$csinput.Free($csinput);")
         std::istream* "(input$csinput == null ? global::System.IntPtr.Zero : input$csinput.Handle)"
%typemap(in) std::ostream* %{ $1 = static_cast<std::ostream*>($input); %}
%typemap(in) std::istream* %{ $1 = static_cast<std::istream*>($input); %}
