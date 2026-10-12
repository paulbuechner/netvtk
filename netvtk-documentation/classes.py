# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT
"""The class index: every type NetVtk gives C#, per VTK module, linked to VTK's Doxygen reference.

Reads netvtk-core's src/SWIG_files/modules.json and classes.json (netvtk-gen writes both) and writes classes/*.md with
their toc.yml. Names and links only: no VTK doc text.

    python classes.py            # before docfx
    python classes.py --check    # also fetch every link: fails on one the reference doesn't have (then see NO_PAGE)
"""

import argparse
import json
import re
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SWIG_FILES = ROOT.parent / "netvtk-core" / "src" / "SWIG_files"
OUT = ROOT / "classes"
# nightly, which is 9.7: vtk.org/doc/release/<version>/html has 7.1, but no 8.2 or 9.x
REFERENCE = "https://vtk.org/doc/nightly/html/"

# how VTK's Doxygen spells names in file names (case-sensitive: capitals stay): "_" doubled, "::" as _1_1, "." as _8
ESCAPES = {"_": "__", ":": "_1", ".": "_8"}

# Classes the reference has no page for (--check, 2026-10-12): the typed arrays VTK's CMake declares from its array
# templates (vtkAffineDoubleArray: vtkAffineArray<double>) link their template's page; vtkType*Array (a typedef-like
# subclass per width) and the classes nightly no longer documents, none.
_C_TYPES = r"(Char|Double|Float|IdType|Int|Long|LongLong|Short|SignedChar|UnsignedChar|UnsignedInt|UnsignedLong" \
           r"|UnsignedLongLong|UnsignedShort)"
_WIDTHS = r"Type(U?Int(8|16|32|64)|Float(32|64))"
NO_PAGE: list[tuple[str, str | None]] = [
    *[(rf"vtk{t}({_C_TYPES}|{_WIDTHS})Array", f"vtk{t}Array") for t in ("Affine", "Composite", "Constant", "Indexed")],
    *[(rf"vtk{t}{_WIDTHS}Array", f"vtk{t}Array") for t in ("StdFunction", "Strided", "StructuredPoint")],
    (rf"vtkSOA{_WIDTHS}Array", "vtkSOADataArrayTemplate"),
    (rf"vtkScaledSOA{_WIDTHS}Array", "vtkScaledSOADataArrayTemplate"),
    (rf"vtk{_WIDTHS}Array", None),
    (r"vtkAMRDataInternals(_Block)?|vtkAMRInformation|vtkInteractorStyleTrackball|vtkMarchingCubesTriangleCases"
     r"|vtkMarchingSquaresLineCases|vtkOldStyleCallbackCommand|vtkScaledTextActor|vtkUniformGridAMRDataIterator", None),
]


def doxygen(name: str) -> str:
    return "".join(ESCAPES.get(c, c) for c in name)


# vtkSOADataArrayTemplate<double>::StorageTypeEnum as vtkSOADataArrayTemplate::StorageTypeEnum: Doxygen documents
# templates, not their instances
def without_arguments(cpp: str) -> str:
    text, depth = [], 0
    for c in cpp:
        depth += {"<": 1, ">": -1}.get(c, 0)
        if depth == 0 and c != ">":
            text.append(c)
    return "".join(text)


def no_page(name: str) -> tuple[bool, str | None]:
    """Whether NO_PAGE covers the type, and the template whose page it links then."""
    for pattern, template in NO_PAGE:
        if re.fullmatch(pattern, name):
            return True, template
    return False, None


def link(entry: dict, kinds: dict[str, str]) -> str | None:
    """The reference's page of a type: a template instance's is its template's, a nested enum's its scope's, a
    file-scope enum's its header's; None where there's none."""
    kind, cpp = entry["kind"], without_arguments(entry["cpp"])
    if kind == "enum":
        if "::" not in cpp:
            return REFERENCE + f"{doxygen(entry['header'])}.html"
        scope = cpp.rsplit("::", 1)[0]
        return REFERENCE + f"{kinds.get(scope, 'class')}{doxygen(scope)}.html"
    covered, template = no_page(entry["name"])
    if covered:
        return REFERENCE + f"class{doxygen(template)}.html" if template else None
    return REFERENCE + f"{kind}{doxygen(cpp)}.html"


# what the entry is, next to its name: an instance's template, an enum, a struct, a namespace, a missing page
def note(entry: dict) -> str:
    if entry.get("instance"):
        return f" (instance of `{entry['cpp']}`)"
    covered, template = no_page(entry["name"])
    if covered:
        return f" (of `{template}`, whose page it links)" if template else " (no page in VTK's reference)"
    return f" ({entry['kind']})" if entry["kind"] in ("enum", "struct", "namespace") else ""


def page(module: str, library: str, entries: list[dict], kinds: dict[str, str]) -> str:
    lines = [f"# {module}", "",
             f"The types of VTK's {module} module in C# (native library `NetVtk{library}`), each linked to its page in "
             "VTK's reference. C# names are VTK's; a nested type's scopes are joined by `_`, a template instance is "
             "named by its arguments.", ""]
    for entry in entries:
        url = link(entry, kinds)
        name = f"[{entry['name']}]({url})" if url else entry["name"]
        lines.append(f"- {name}{note(entry)}")
    lines.append("")
    return "\n".join(lines)


def status(url: str) -> int:
    """The HTTP status of a page (HEAD; GET where HEAD isn't allowed), -1 when unreachable."""
    for method in ("HEAD", "GET"):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, method=method), timeout=30) as response:
                return response.status
        except urllib.error.HTTPError as error:
            if error.code != 405:
                return error.code
        except OSError:
            return -1
    return -1


def check(urls: set[str]) -> None:
    with ThreadPoolExecutor(8) as pool:
        broken = [(url, code) for url, code in zip(sorted(urls), pool.map(status, sorted(urls))) if code != 200]
    for url, code in broken:
        print(f"{code} {url}")
    print(f"links: {len(urls)} checked, {len(broken)} broken")
    if broken:
        sys.exit(1)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="fetch every link, fail on one that's broken")
    args = parser.parse_args()

    libraries: dict[str, list[str]] = json.loads((SWIG_FILES / "modules.json").read_text(encoding="utf-8"))
    types: dict[str, list[dict]] = json.loads((SWIG_FILES / "classes.json").read_text(encoding="utf-8"))
    # each C++ scope's kind (class, struct, namespace): a nested enum's page is its scope's
    kinds = {without_arguments(entry["cpp"]): entry["kind"] for entries in types.values() for entry in entries
             if entry["kind"] != "enum"}
    OUT.mkdir(exist_ok=True)
    for stale in OUT.glob("*.md"):
        stale.unlink()
    toc = ["- name: Overview", "  href: index.md"]
    overview = []
    for library, modules in libraries.items():
        overview += [f"## NetVtk{library}", ""]
        for module in modules:
            entries = types.get(module, [])
            (OUT / f"{module}.md").write_text(page(module, library, entries, kinds), encoding="utf-8", newline="\n")
            toc += [f"- name: {module}", f"  href: {module}.md"]
            overview.append(f"- [{module}]({module}.md): {len(entries)} types")
        overview.append("")
    count = sum(len(v) for v in types.values())
    modules = sum(len(v) for v in libraries.values())
    (OUT / "index.md").write_text("\n".join([
        "# Class index", "",
        f"All {count} types NetVtk gives C#, in namespace `Vtk`, by VTK module and the native library that wraps it. "
        f"Each links to its page in [VTK's reference]({REFERENCE}) (nightly: Kitware publishes no reference for 9.7 "
        "releases); C# keeps VTK's names, so the pages apply as they are. The typed arrays VTK declares from its "
        "array templates link their template's page.", "",
        *overview,
    ]), encoding="utf-8", newline="\n")
    (OUT / "toc.yml").write_text("\n".join(toc) + "\n", encoding="utf-8", newline="\n")
    print(f"class index: {count} types in {modules} modules -> {OUT}")
    if args.check:
        check({url for entries in types.values() for entry in entries if (url := link(entry, kinds))})


if __name__ == "__main__":
    main()
