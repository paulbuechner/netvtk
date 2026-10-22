# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Tests of NetVtkInterop.i's macros: SWIG expands each in a binding's module, as another binding's build does, and the
C++ and C# it writes hold what they declare: python test/test_interop.py (stdlib unittest; build.py tools' SWIG)."""
from __future__ import annotations

import importlib.util
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

CORE = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("build", CORE / "build.py")
build = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(build)

INTEROP = CORE / "src" / "SWIG_files" / "interop"

# a binding's module: a NetVtk class in its signatures, a class of its own deriving from one, the build it's compiled
# against
MODULE = """%module Binding
%include "NetVtkInterop.i"

%{
#include <vtkPolyDataAlgorithm.h>
%}

%netvtk_object(vtkPolyData)
%netvtk_derived(BindingSource, global::Vtk.vtkPolyDataAlgorithm)
%netvtk_new(BindingSource)
%netvtk_build(Binding_VtkBuild)

class BindingSource {
public:
  static BindingSource* New();
  vtkPolyData* GetShape();
};
"""


class InteropTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.output = Path(tempfile.mkdtemp())
        (cls.output / "Binding.i").write_text(MODULE, encoding="utf-8")
        cls.swig = subprocess.run(
            [build.swig_executable(), "-c++", "-csharp", "-I" + str(INTEROP), "-outdir", cls.output,
             "-o", cls.output / "Binding_wrap.cxx", cls.output / "Binding.i"],
            capture_output=True, text=True, env=build.swig_env())

    def cxx(self) -> str:
        return (self.output / "Binding_wrap.cxx").read_text(encoding="utf-8")

    def cs(self, name: str) -> str:
        return (self.output / f"{name}.cs").read_text(encoding="utf-8")

    def test_swig_expands_every_macro(self):
        self.assertEqual(self.swig.returncode, 0, self.swig.stderr)

    def test_cxx_holds_no_swig_directive(self):
        self.assertEqual(re.findall(r"^\s*%.*$", self.cxx(), re.MULTILINE), [])

    def test_build_includes_the_ports_header(self):
        self.assertRegex(self.cxx(), r"(?m)^#include <vtkNetVtkBuild\.h>$")
        self.assertIn("inline const char* Binding_VtkBuild() { return NETVTK_VTK_BUILD; }", self.cxx())
        self.assertIn("public static string Binding_VtkBuild()", self.cs("Binding"))

    def test_derived_class_derives_from_its_netvtk_base(self):
        self.assertIn("public class BindingSource : global::Vtk.vtkPolyDataAlgorithm", self.cs("BindingSource"))
        self.assertIn("public BindingSource()", self.cs("BindingSource"))

    def test_object_returns_netvtks_proxy(self):
        self.assertIn("global::Vtk.vtkObjectBase.FromNativePointer<global::Vtk.vtkPolyData>(cPtr, true)",
                      self.cs("BindingSource"))


if __name__ == "__main__":
    unittest.main()
