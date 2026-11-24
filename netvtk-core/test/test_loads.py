# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""Tests of loads.py, what the natives load: python test/test_loads.py (stdlib unittest). The files are written here, the
smallest PE, ELF and Mach-O files with the records loads.py reads."""
from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

CORE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CORE))
import loads  # noqa: E402

SECTION_RVA, SECTION_OFFSET = 0x1000, 0x200


def pe(imports: list[str], delayed: list[str], wide: bool = True, delay_rvas: bool = True) -> bytes:
    """A PE file with one section: the import descriptors, the delay-load descriptors (RVAs, or addresses as the oldest
    linkers wrote them) and the names."""
    image_base = 0x180000000 if wide else 0x10000000
    names = b"".join(n.encode() + b"\0" for n in imports + delayed)
    imports_at = len(names)
    delayed_at = imports_at + 20 * (len(imports) + 1)
    section = bytearray(names + bytes(delayed_at - imports_at + 32 * (len(delayed) + 1)))
    position = 0
    for i, name in enumerate(imports + delayed):
        rva = SECTION_RVA + position
        if i < len(imports):
            struct.pack_into("<I", section, imports_at + 20 * i + 12, rva)
        else:
            j = i - len(imports)
            struct.pack_into("<II", section, delayed_at + 32 * j, 1 if delay_rvas else 0, rva if delay_rvas else rva + image_base)
        position += len(name) + 1

    optional = bytearray(240 if wide else 224)
    struct.pack_into("<H", optional, 0, 0x20B if wide else 0x10B)
    if wide:
        struct.pack_into("<Q", optional, 24, image_base)
    else:
        struct.pack_into("<I", optional, 28, image_base)
    directories = 112 if wide else 96
    struct.pack_into("<I", optional, directories - 4, 16)
    struct.pack_into("<II", optional, directories + 8, SECTION_RVA + imports_at, 20 * (len(imports) + 1))
    struct.pack_into("<II", optional, directories + 13 * 8, SECTION_RVA + delayed_at, 32 * (len(delayed) + 1))
    header = bytearray(0x40)
    header[:2] = b"MZ"
    struct.pack_into("<I", header, 0x3C, 0x40)
    coff = b"PE\0\0" + struct.pack("<HHIIIHH", 0x8664 if wide else 0x14C, 1, 0, 0, 0, len(optional), 0)
    section_header = b".rdata\0\0" + struct.pack("<IIIIIIHHI", len(section), SECTION_RVA, len(section), SECTION_OFFSET, 0, 0,
                                                 0, 0, 0)
    head = bytes(header) + coff + bytes(optional) + section_header
    return head + bytes(SECTION_OFFSET - len(head)) + bytes(section)


def elf(needed: list[str]) -> bytes:
    """A 64-bit little-endian ELF file: one PT_LOAD mapping it all at 0x1000, a PT_DYNAMIC with DT_NEEDED and DT_STRTAB."""
    address = 0x1000
    strings = b"\0" + b"".join(n.encode() + b"\0" for n in needed)
    strings_at = 64 + 2 * 56
    dynamic_at = strings_at + len(strings) + (-len(strings) % 8)
    offsets = [1 + sum(len(n) + 1 for n in needed[:i]) for i in range(len(needed))]
    dynamic = b"".join(struct.pack("<qQ", 1, o) for o in offsets) + struct.pack("<qQ", 5, address + strings_at) + \
        struct.pack("<qQ", 0, 0)
    size = dynamic_at + len(dynamic)
    header = b"\x7fELF" + bytes([2, 1, 1]) + bytes(9) + struct.pack("<HHIQQQIHHHHHH", 3, 62, 1, 0, 64, 0, 0, 64, 56, 2, 64, 0, 0)
    load = struct.pack("<IIQQQQQQ", 1, 5, 0, address, address, size, size, 0x1000)
    dynamic_segment = struct.pack("<IIQQQQQQ", 2, 6, dynamic_at, address + dynamic_at, address + dynamic_at, len(dynamic),
                                  len(dynamic), 8)
    body = header + load + dynamic_segment + strings
    return body + bytes(dynamic_at - len(body)) + dynamic


def macho(dylibs: list[tuple[int, str]]) -> bytes:
    """A 64-bit little-endian Mach-O dylib: its own LC_ID_DYLIB, then a dylib command per (command, install name)."""
    commands = b""
    for command, name in [(0xD, "@rpath/libSelf.dylib"), *dylibs]:
        text = name.encode() + b"\0"
        text += bytes(-(24 + len(text)) % 8)
        commands += struct.pack("<IIIIII", command, 24 + len(text), 24, 0, 0, 0) + text
    return struct.pack("<IiiIIIII", 0xFEEDFACF, 0x0100000C, 0, 6, len(dylibs) + 1, len(commands), 0, 0) + commands


def fat(thin: bytes) -> bytes:
    """A universal file holding one architecture's file at 4096."""
    return struct.pack(">II", 0xCAFEBABE, 1) + struct.pack(">iiIII", 0x0100000C, 0, 4096, len(thin), 12) + \
        bytes(4096 - 28) + thin


class NeededTests(unittest.TestCase):
    def needed(self, data: bytes) -> list[str]:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "library"
            path.write_bytes(data)
            return loads.needed(path)

    def test_needed_of_a_pe_file_are_its_imports_then_its_delay_loaded_ones(self):
        # Act
        names = self.needed(pe(["vtkCommonCore-9.7.dll", "KERNEL32.dll"], ["vtkRenderingOpenGL2-9.7.dll"]))

        # Assert
        self.assertEqual(names, ["vtkCommonCore-9.7.dll", "KERNEL32.dll", "vtkRenderingOpenGL2-9.7.dll"])

    def test_needed_of_a_32_bit_pe_file_take_delay_load_addresses_off_its_image_base(self):
        # Act
        names = self.needed(pe(["vtkCommonCore-9.7.dll"], ["vtksys-9.7.dll"], wide=False, delay_rvas=False))

        # Assert
        self.assertEqual(names, ["vtkCommonCore-9.7.dll", "vtksys-9.7.dll"])

    def test_needed_of_an_elf_file_are_its_dt_needed_entries(self):
        # Act
        names = self.needed(elf(["libvtkCommonCore-9.7.so.1", "libc.so.6"]))

        # Assert
        self.assertEqual(names, ["libvtkCommonCore-9.7.so.1", "libc.so.6"])

    def test_needed_of_a_macho_file_are_its_dylibs_last_parts_without_its_own_id(self):
        # Act
        names = self.needed(macho([(0xC, "@rpath/libvtkCommonCore-9.7.1.dylib"), (0x80000018, "/usr/lib/libc++.1.dylib")]))

        # Assert
        self.assertEqual(names, ["libvtkCommonCore-9.7.1.dylib", "libc++.1.dylib"])

    def test_needed_of_a_universal_file_are_its_architectures(self):
        # Act
        names = self.needed(fat(macho([(0xC, "@rpath/libvtksys-9.7.1.dylib")])))

        # Assert
        self.assertEqual(names, ["libvtksys-9.7.1.dylib"])

    def test_needed_refuses_another_file(self):
        # Act, Assert
        with self.assertRaises(ValueError):
            self.needed(b"<Project />")


class StageTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.stage = Path(self.directory.name)
        # NetVtkFilters imports vtkFiltersCore, which imports vtkcommoncore by another spelling; NetVtkRuntime imports
        # the system's only; vtkRenderingOpenGL2 nobody imports
        files = {"NetVtkFilters.dll": pe(["vtkFiltersCore-9.7.dll", "KERNEL32.dll"], []),
                 "vtkFiltersCore-9.7.dll": pe(["vtkcommoncore-9.7.dll"], []),
                 "vtkCommonCore-9.7.dll": pe(["KERNEL32.dll"], ["vtksys-9.7.dll"]),
                 "vtksys-9.7.dll": pe([], []),
                 "vtkRenderingOpenGL2-9.7.dll": pe(["vtkCommonCore-9.7.dll", "OPENGL32.dll"], []),
                 "NetVtkRuntime.dll": pe(["KERNEL32.dll"], [])}
        for name, data in files.items():
            (self.stage / name).write_bytes(data)

    def tearDown(self):
        self.directory.cleanup()

    def test_closures_hold_what_each_file_loads_through_others_in_the_stages_spelling(self):
        # Act
        closures = loads.closures(self.stage)

        # Assert
        self.assertEqual(closures["NetVtkFilters.dll"],
                         ["NetVtkFilters.dll", "vtkCommonCore-9.7.dll", "vtkFiltersCore-9.7.dll", "vtksys-9.7.dll"])

    def test_foreign_are_the_loaded_files_the_stage_lacks(self):
        # Act
        names = loads.foreign(self.stage)

        # Assert
        self.assertEqual(names, ["KERNEL32.dll", "OPENGL32.dll"])

    def test_module_of_a_library_is_its_name_after_the_prefix_on_every_system(self):
        # Act
        modules = [loads.module_of(n, "NetVtk") for n in
                   ("NetVtkFilters.dll", "libNetVtkFilters.so", "libNetVtkFilters.dylib", "NetVtk.dll",
                    "vtkFiltersCore-9.7.dll")]

        # Assert
        self.assertEqual(modules, ["Filters", "Filters", "Filters", None, None])

    def test_write_marks_the_module_libraries_items(self):
        # Arrange
        target = self.stage.parent / f"{self.stage.name}.targets"

        # Act
        try:
            loads.write(self.stage, target, "NetVtk.runtime.win-x64", "NetVtk")
            text = target.read_text(encoding="utf-8")
        finally:
            target.unlink(missing_ok=True)

        # Assert
        self.assertIn('<_NetVtkNative Include="NetVtkFilters.dll" Package="NetVtk.runtime.win-x64" Module="Filters" '
                      'Loads="NetVtkFilters.dll;vtkCommonCore-9.7.dll;vtkFiltersCore-9.7.dll;vtksys-9.7.dll" />', text)
        self.assertIn('<_NetVtkNative Include="vtkRenderingOpenGL2-9.7.dll" Package="NetVtk.runtime.win-x64" '
                      'Loads="vtkCommonCore-9.7.dll;vtkRenderingOpenGL2-9.7.dll;vtksys-9.7.dll" />', text)

    def test_write_refuses_a_stage_without_its_runtime_library(self):
        # Arrange
        (self.stage / "NetVtkRuntime.dll").unlink()

        # Act, Assert
        with self.assertRaises(SystemExit):
            loads.write(self.stage, self.stage.parent / "unused.targets", "NetVtk.runtime.win-x64", "NetVtk")


if __name__ == "__main__":
    unittest.main()
