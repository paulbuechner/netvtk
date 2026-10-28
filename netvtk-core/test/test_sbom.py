# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Tests of sbom.py, the runtime packages' SBOM: python test/test_sbom.py (stdlib unittest)."""
from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import sbom  # noqa: E402

THIRD_PARTY_CMAKE = """vtk_third_party_extract(
  FILE "${CMAKE_CURRENT_SOURCE_DIR}/update.sh"
  TAGS
    repo expat_repo
    tag  expat_tag
  VERSION_FROM_TAG
    tag  expat_version "^for/vtk-[0-9]*-([0-9\\\\.]+)$")

vtk_module_third_party(
  INTERNAL
    LICENSE_FILES "vtkexpat/COPYING"  # the license
    SPDX_LICENSE_IDENTIFIER
      "MIT"
    SPDX_COPYRIGHT_TEXT
      "Copyright (c) 1998-2000 Thai Open Source Software Center Ltd and Clark Cooper"
      "Copyright (c) 2001-2019 Expat maintainers"
    SPDX_DOWNLOAD_LOCATION "git+${expat_repo}@${expat_tag}"
    VERSION "${expat_version}"
    STANDARD_INCLUDE_DIRS
  EXTERNAL
    PACKAGE EXPAT
    VERSION 2.2.1
    TARGETS EXPAT::EXPAT)
"""


class SbomTests(unittest.TestCase):
    def test_cmake_calls_unquote_values_and_drop_comments(self):
        # Arrange
        text = 'set(a "x \\"y\\"" b) # set(c)\nif (NOT (a)) set( d "e" )'

        # Act
        calls = sbom.cmake_calls(text, "set")

        # Assert (a comment's call is no call; parentheses nest)
        self.assertEqual(calls, [[("a", False), ('x "y"', True), ("b", False)], [("d", False), ("e", True)]])

    def test_third_party_declaration_resolves_the_internal_copys_tag_and_version(self):
        # Arrange
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            (folder / "CMakeLists.txt").write_text(THIRD_PARTY_CMAKE, encoding="utf-8")
            (folder / "update.sh").write_text('readonly repo="https://example.org/expat.git"\n'
                                              'readonly tag="for/vtk-20260214-2.7.0"\n', encoding="utf-8")

            # Act
            declared = sbom.third_party_declaration(folder)

        # Assert (the EXTERNAL block's minimum version is not the copy's)
        self.assertEqual(declared, {
            "LICENSE_FILES": ["vtkexpat/COPYING"], "SPDX_LICENSE_IDENTIFIER": ["MIT"],
            "SPDX_COPYRIGHT_TEXT": ["Copyright (c) 1998-2000 Thai Open Source Software Center Ltd and Clark Cooper",
                                    "Copyright (c) 2001-2019 Expat maintainers"],
            "SPDX_DOWNLOAD_LOCATION": ["git+https://example.org/expat.git@for/vtk-20260214-2.7.0"], "VERSION": ["2.7.0"],
            "STANDARD_INCLUDE_DIRS": []})

    def test_spdx_expression_takes_spdxs_spelling(self):
        # Arrange
        ids = {"ijg": "IJG", "bsd-3-clause": "BSD-3-Clause", "zlib": "Zlib"}

        # Act
        expression = sbom.spdx_expression("IJG AND BSD-3-Clause AND ZLIB", ids)

        # Assert
        self.assertEqual(expression, "IJG AND BSD-3-Clause AND Zlib")

    def test_license_terms_split_an_expression_and_refuse_a_choice_nobody_made(self):
        # Act
        terms = sbom.license_terms("IJG AND BSD-3-Clause AND Zlib")
        refuse = lambda: sbom.license_terms("FTL OR GPL-2.0-or-later")

        # Assert
        self.assertEqual(terms, ["IJG", "BSD-3-Clause", "Zlib"])
        self.assertRaises(SystemExit, refuse)

    def test_licenses_are_one_concluded_expression_their_texts_evidence(self):
        # Act
        licenses = sbom.licenses(["MIT", "LicenseRef-scancode-public-domain"])
        evidence = sbom.evidence([("MIT", "a"), ("LicenseRef-scancode-public-domain", "b")])

        # Assert (a LicenseRef is no SPDX id: CycloneDX takes it as a name)
        self.assertEqual(licenses, [{"expression": "MIT AND LicenseRef-scancode-public-domain",
                                     "acknowledgement": "concluded"}])
        self.assertEqual([(sorted(e["license"]), e["license"]["text"]["content"]) for e in evidence["licenses"]],
                         [(["id", "text"], "a"), (["name", "text"], "b")])

    def test_source_splits_the_repository_from_the_ref_and_the_path(self):
        # Act
        sources = [sbom.source(d) for d in (
            "git+https://gitlab.kitware.com/third-party/zlib.git@for/vtk-20260125-1.3.1",
            "git+https://gitlab.kitware.com/third-party/seacas.git@exodus/for/vtk-20250313#packages/seacas/libraries/exodus",
            "git+https://github.com/Dav1dde/glad/tree/glad2")]

        # Assert (a ref may hold a slash)
        self.assertEqual(sources, [("https://gitlab.kitware.com/third-party/zlib", "for/vtk-20260125-1.3.1"),
                                   ("https://gitlab.kitware.com/third-party/seacas", "exodus/for/vtk-20250313"),
                                   ("https://github.com/Dav1dde/glad/tree/glad2", None)])

    def test_library_name_drops_the_platforms_prefix_version_and_extension(self):
        # Act
        names = [sbom.library_name(n) for n in ("vtkzlib-9.7.dll", "libvtkzlib-9.7.so.1", "libvtkzlib-9.7.1.dylib",
                                                 "NetVtkCommon.dll", "libNetVtkCommon.so", "notes.txt")]

        # Assert
        self.assertEqual(names, ["vtkzlib", "vtkzlib", "vtkzlib", "NetVtkCommon", "NetVtkCommon", None])

    def test_write_sbom_puts_each_file_under_its_library(self):
        # Arrange (VTK's license folders, a built-in with a second library, NetVtk's own and VTK's)
        with tempfile.TemporaryDirectory() as tmp:
            prefix, stage = Path(tmp, "prefix"), Path(tmp, "win-x64", "native")
            licenses = prefix / "share" / "vtk" / "licenses" / "VTK"
            for folder, text in (("vtkCommonCore", "VTK module"), ("vtkhdf5", "HDF5"), ("vtkzlib", "zlib")):
                (licenses / folder).mkdir(parents=True)
                (licenses / folder / "COPYING").write_text(text, encoding="utf-8")
            (licenses / "Copyright.txt").write_text("VTK", encoding="utf-8")
            stage.mkdir(parents=True)
            for name in ("NetVtkCommon.dll", "vtkCommonCore-9.7.dll", "vtkhdf5-9.7.dll", "vtkhdf5_hl-9.7.dll",
                         "vtkzlib-9.7.dll"):
                (stage / name).write_bytes(b"")
            components = Path(tmp, "vtk-components.json")
            components.write_text(json.dumps({"vtk": "9.7.1", "folders": {
                "vtkCommonCore": {"name": "vtk", "license": "BSD-3-Clause"},
                "vtkhdf5": {"name": "hdf5", "version": "1.14.6", "license": "BSD-3-Clause",
                            "download": "git+https://gitlab.kitware.com/third-party/hdf5.git@for/vtk-1.14.6"},
                "vtkzlib": {"name": "zlib", "version": "master", "license": "Zlib"}}}), encoding="utf-8")
            target = stage.parent / sbom.FILE

            # Act
            with mock.patch.object(sbom, "VTK_COMPONENTS", components), \
                    mock.patch.object(sbom, "port_patches", return_value=[]):
                sbom.write(stage, target, "NetVtk.runtime.win-x64", prefix=prefix, vtk="9.7.1",
                           repository="https://github.com/paulbuechner/netvtk")
            written = json.loads(target.read_text(encoding="utf-8"))

        # Assert (hdf5_hl is hdf5's; a branch is no version, VTK's is; every reference names the package first)
        files = {c["name"]: [f["name"] for f in c.get("components", [])] for c in written["components"]}
        self.assertEqual(files, {"NetVtk": ["NetVtkCommon.dll"], "vtk": ["vtkCommonCore-9.7.dll"],
                                 "hdf5": ["vtkhdf5-9.7.dll", "vtkhdf5_hl-9.7.dll"], "zlib": ["vtkzlib-9.7.dll"]})
        self.assertEqual([c["version"] for c in written["components"]], ["", "9.7.1", "1.14.6", "9.7.1"])
        self.assertEqual(written["components"][2]["manufacturer"], {"url": ["https://gitlab.kitware.com/third-party/hdf5"]})
        self.assertTrue(all(d["ref"].startswith("NetVtk.runtime.win-x64/") or d["ref"] == "pkg:nuget/NetVtk.runtime.win-x64"
                            for d in written["dependencies"]))
        self.assertEqual(len(written["dependencies"]), 3 + 2 + 5)

    def test_stamp_sbom_gives_the_package_its_version_and_each_file_its_hash(self):
        # Arrange
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp) / "win-x64" / "native"
            stage.mkdir(parents=True)
            (stage / "NetVtkCommon.dll").write_bytes(b"code")
            stamp_file = stage.parent / sbom.FILE
            package, own = "pkg:nuget/NetVtk.runtime.win-x64", "NetVtk.runtime.win-x64/netvtk"
            stamp_file.write_text(json.dumps({
                "serialNumber": "urn:uuid:00000000-0000-0000-0000-000000000000", "metadata": {
                    "timestamp": "2026-01-01T00:00:00Z", "component": {
                        "bom-ref": package, "name": "NetVtk.runtime.win-x64", "purl": package,
                        "properties": sbom.bsi_properties(False, True, True)}},
                "components": [{"type": "library", "bom-ref": own, "name": "NetVtk", "version": "", "components": [
                    sbom.file_component("NetVtk.runtime.win-x64", "NetVtkCommon.dll", {
                        "manufacturer": {}, "version": "", "licenses": []})]}],
                "dependencies": [{"ref": package, "dependsOn": [own]}],
                "compositions": [{"aggregate": "complete", "assemblies": [package, own]}]}), encoding="utf-8")

            # Act (twice: pack stamps a stage again)
            sbom.stamp(stamp_file, "9.7.1.1")
            sbom.stamp(stamp_file, "9.7.1.2")
            stamped = json.loads(stamp_file.read_text(encoding="utf-8"))

        # Assert (the reference that names the package follows it; the package's own libraries take its version)
        expected = "pkg:nuget/NetVtk.runtime.win-x64@9.7.1.2"
        root, own_part = stamped["metadata"]["component"], stamped["components"][0]
        file = own_part["components"][0]
        self.assertEqual((root["version"], root["purl"], stamped["dependencies"][0]["ref"],
                          stamped["compositions"][0]["assemblies"][0]), ("9.7.1.2", expected, expected, expected))
        self.assertEqual(root["properties"][0], {"name": "bsi:component:filename",
                                                 "value": "NetVtk.runtime.win-x64.9.7.1.2.nupkg"})
        self.assertEqual((own_part["version"], file["version"]), ("9.7.1.2", "9.7.1.2"))
        self.assertEqual(file["hashes"], [{"alg": "SHA-512", "content": hashlib.sha512(b"code").hexdigest()}])
        self.assertEqual(file["externalReferences"][0]["hashes"], file["hashes"])
        self.assertNotEqual(stamped["serialNumber"], "urn:uuid:00000000-0000-0000-0000-000000000000")

    def test_stamp_sbom_refuses_a_stage_with_a_file_the_sbom_lacks(self):
        # Arrange
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp) / "win-x64" / "native"
            stage.mkdir(parents=True)
            (stage / "vtkzlib-9.7.dll").write_bytes(b"")
            package = "pkg:nuget/NetVtk.runtime.win-x64"
            (stage.parent / sbom.FILE).write_text(json.dumps({
                "metadata": {"component": {"bom-ref": package, "name": "NetVtk.runtime.win-x64", "purl": package,
                                           "properties": []}},
                "components": [], "dependencies": []}), encoding="utf-8")

            # Act
            refuse = lambda: sbom.stamp(stage.parent / sbom.FILE, "9.7.1.1")

            # Assert
            self.assertRaises(SystemExit, refuse)


if __name__ == "__main__":
    unittest.main()
