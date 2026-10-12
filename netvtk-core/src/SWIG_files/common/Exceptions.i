// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Every wrapper runs its call ($action) in a try block: an exception the call throws is caught, and its type and message
 * go to a per-module C# callback, which queues a Vtk.VtkException that the generated code throws right after the call.
 * Typemap code outside the call isn't covered; it can only fail to allocate.
 *
 * VTK reports errors through vtkOutputWindow and error events, not exceptions: what arrives here is the C++ library's
 * (std::bad_alloc, std::out_of_range). What a C# override VTK called back threw comes after the call, as the number
 * NetVtkRuntime holds (Directors.i): a NetVtk_ManagedException, whose VtkException carries the C# one.
 */

%{
#include <exception>
#include <new>
#include <stdexcept>
#include <string>
#include <NetVtkRuntime.hxx>
%}

%insert(runtime) %{
typedef void (SWIGSTDCALL* NetVtkExceptionCallback_t)(const char* theType, const char* theMessage);
static NetVtkExceptionCallback_t NetVtk_exceptionCallback = 0;

#ifdef __cplusplus
extern "C"
#endif
SWIGEXPORT void SWIGSTDCALL NetVtkRegisterExceptionCallback_$module(NetVtkExceptionCallback_t theCallback) {
  NetVtk_exceptionCallback = theCallback;
}

static void NetVtk_SetPendingException(const char* theType, const char* theMessage) {
  if (NetVtk_exceptionCallback) {
    NetVtk_exceptionCallback(theType, theMessage ? theMessage : "");
  }
}
%}

%pragma(csharp) imclasscode=%{
  class NetVtkExceptionHelper {
    public delegate void ExceptionDelegate(global::System.IntPtr theType, global::System.IntPtr theMessage);
    static readonly ExceptionDelegate exceptionDelegate = new ExceptionDelegate(SetPending);

    [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="NetVtkRegisterExceptionCallback_$module")]
    public static extern void NetVtkRegisterExceptionCallback(ExceptionDelegate theCallback);

    static void SetPending(global::System.IntPtr theType, global::System.IntPtr theMessage) {
      SWIGPendingException.Set(global::Vtk.VtkException.FromNative(
        global::Vtk.Utf8.Decode(theType), global::Vtk.Utf8.Decode(theMessage)));
    }

    static NetVtkExceptionHelper() {
      NetVtkRegisterExceptionCallback(exceptionDelegate);
    }
  }
  static readonly NetVtkExceptionHelper netvtkExceptionHelper = new NetVtkExceptionHelper();
%}

// the standard exceptions by their names; then what a C# override threw during the call (Directors.i), which a C++
// exception makes moot
%exception {
  try {
    $action
  } catch (const std::bad_alloc& e) {
    NetVtk_TakeDirectorFailure();
    NetVtk_SetPendingException("std::bad_alloc", e.what());
    return $null;
  } catch (const std::out_of_range& e) {
    NetVtk_TakeDirectorFailure();
    NetVtk_SetPendingException("std::out_of_range", e.what());
    return $null;
  } catch (const std::invalid_argument& e) {
    NetVtk_TakeDirectorFailure();
    NetVtk_SetPendingException("std::invalid_argument", e.what());
    return $null;
  } catch (const std::exception& e) {
    NetVtk_TakeDirectorFailure();
    NetVtk_SetPendingException("std::exception", e.what());
    return $null;
  } catch (...) {
    NetVtk_TakeDirectorFailure();
    NetVtk_SetPendingException("unknown", "non-standard C++ exception");
    return $null;
  }
  if (const long long netvtkFailure = NetVtk_TakeDirectorFailure()) {
    NetVtk_SetPendingException("NetVtk_ManagedException", std::to_string(netvtkFailure).c_str());
  }
}
