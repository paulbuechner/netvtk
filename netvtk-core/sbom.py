# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""The runtime packages' CycloneDX SBOM, with the data fields of BSI TR-03183-2.

Every file of a runtime package goes under the library it belongs to: NetVtk's, VTK's, or one VTK builds in, which no
package manager lists. Each library comes with its creator, version, license (the texts as evidence), copyright and
source. build.py native writes the SBOM beside THIRD-PARTY-NOTICES.txt (write), pack stamps the package's version, the
files' SHA-512 and the SBOM's serial number and time in (stamp). The package carries it as sbom/sbom.cdx.json, where a
consumer's own SBOM takes it in under the package. What VTK's sources declare of its license folders comes from
pack/vtk-components.json, which build.py third-party writes (write_vtk_components).
"""
from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
import uuid
from collections.abc import Callable, Iterator
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
FILE = "sbom.cdx.json"
SPEC = "1.6"
# What VTK installs a license folder for (share/vtk/licenses/VTK/<folder>), as VTK's sources declare it: its modules, the
# libraries it builds in. build.py third-party writes it from those sources, which CI's VTK, from vcpkg's binary cache,
# comes without.
VTK_COMPONENTS = ROOT / "pack" / "vtk-components.json"
VTK_HOMEPAGE = "https://vtk.org"
VTK_REPOSITORY = "https://github.com/Kitware/VTK"
SPDX_LICENSES = "https://raw.githubusercontent.com/spdx/license-list-data/main/json/licenses.json"
# VTK's declarations SPDX can't read or the installed license text contradicts, by license folder: glad's code is MIT,
# the Khronos specifications Apache-2.0, the EGL headers Khronos' old MIT (VTK declares "GLAD"); liblzma is 0BSD since
# xz 5.6 (VTK declares it public domain); loguru's public domain dedication by ScanCode's identifier, which BSI
# TR-03183-2 takes before a reference of one's own
VTK_LICENSE_CORRECTIONS = {"vtkglad": "MIT AND Apache-2.0 AND MIT-Khronos-old", "vtklzma": "0BSD",
                           "vtkloguru": "LicenseRef-scancode-public-domain"}


# --------------------------------------------------------------------------- VTK's declarations
CMAKE_TOKEN = re.compile(r'#[^\n]*|"((?:[^"\\]|\\.)*)"|([()])|([^\s()"#]+)')


def cmake_calls(text: str, command: str) -> list[list[tuple[str, bool]]]:
    """The arguments of each call of a CMake command, each (value, quoted), comments dropped and escapes resolved."""
    tokens = []
    for m in CMAKE_TOKEN.finditer(text):
        if m.group(1) is not None:
            tokens.append(("str", re.sub(r"\\(.)", lambda e: {"n": "\n", "t": "\t"}.get(e.group(1), e.group(1)),
                                         m.group(1))))
        elif m.group(2):
            tokens.append((m.group(2), m.group(2)))
        elif m.group(3):
            tokens.append(("word", m.group(3)))
    calls, i = [], 0
    while i < len(tokens):
        if tokens[i] == ("word", command) and i + 1 < len(tokens) and tokens[i + 1][0] == "(":
            depth, args, i = 1, [], i + 2
            while i < len(tokens) and depth:
                kind, value = tokens[i]
                depth += {"(": 1, ")": -1}.get(kind, 0)
                if kind in ("str", "word"):
                    args.append((value, kind == "str"))
                i += 1
            calls.append(args)
        else:
            i += 1
    return calls


def keyword_values(args: list[tuple[str, bool]]) -> dict[str, list[str]]:
    """A call's keyword arguments: an upper-case bare word starts one, the values after it are its."""
    values: dict[str, list[str]] = {}
    key = None
    for value, quoted in args:
        if not quoted and re.fullmatch(r"[A-Z][A-Z0-9_]*", value):
            key = value
            values.setdefault(key, [])
        elif key:
            values[key].append(value)
    return values


def vtk_module_values(text: str) -> dict[str, list[str]]:
    """A vtk.module file: a key at the start of a line, its values on the indented lines after it."""
    values: dict[str, list[str]] = {}
    key = None
    for line in text.splitlines():
        if not line.strip():
            continue
        if not line[0].isspace():
            key = line.strip()
            values.setdefault(key, [])
        elif key:
            values[key].append(line.strip())
    return values


def third_party_variables(text: str, folder: Path) -> dict[str, str]:
    """The variables vtk_third_party_extract sets from a third-party folder's update.sh: its repo and tag, and versions
    taken from the tag by a regex."""
    variables: dict[str, str] = {}
    for args in cmake_calls(text, "vtk_third_party_extract"):
        words = [value for value, _ in args]
        script = folder / Path(words[words.index("FILE") + 1].replace("${CMAKE_CURRENT_SOURCE_DIR}/", "")).name
        shell = dict(re.findall(r'^readonly\s+(\w+)="([^"]*)"', script.read_text(encoding="utf-8"), re.M))
        mode, rest = None, words[words.index("FILE") + 2:]
        while rest:
            if rest[0] in ("TAGS", "VERSION_FROM_TAG"):
                mode, rest = rest[0], rest[1:]
            elif mode == "TAGS":
                variables[rest[1]] = shell[rest[0]]
                rest = rest[2:]
            elif mode == "VERSION_FROM_TAG":
                match = re.match(rest[2], shell[rest[0]])
                if match:
                    variables[rest[1]] = match.group(1)
                rest = rest[3:]
            else:
                rest = rest[1:]
    return variables


def third_party_declaration(folder: Path) -> dict[str, list[str]]:
    """What a third-party module's CMakeLists.txt declares for its internal copy, variables resolved."""
    text = (folder / "CMakeLists.txt").read_text(encoding="utf-8", errors="replace")
    calls = cmake_calls(text, "vtk_module_third_party") + cmake_calls(text, "vtk_module_third_party_internal")
    if not calls:
        return {}
    args = calls[0]
    words = [value for value, quoted in args if not quoted]
    if "INTERNAL" in words:
        start = next(i for i, (value, quoted) in enumerate(args) if value == "INTERNAL" and not quoted)
        end = next((i for i, (value, quoted) in enumerate(args) if value == "EXTERNAL" and not quoted), len(args))
        args = args[start + 1:end]
    variables = third_party_variables(text, folder)
    resolve = lambda value: re.sub(r"\$\{(\w+)\}", lambda m: variables.get(m.group(1), m.group(0)), value)
    return {key: [resolve(v) for v in values] for key, values in keyword_values(args).items()}


def spdx_license_ids() -> dict[str, str]:
    """SPDX's license and exception identifiers, by their lower case: VTK's sources write some in other cases (ZLIB)."""
    import urllib.request
    ids = {}
    for url in (SPDX_LICENSES, SPDX_LICENSES.replace("licenses.json", "exceptions.json")):
        with urllib.request.urlopen(url, timeout=60) as response:
            data = json.load(response)
        for entry in data.get("licenses", []) + data.get("exceptions", []):
            identifier = entry.get("licenseId") or entry.get("licenseExceptionId")
            ids[identifier.lower()] = identifier
    return ids


def spdx_expression(expression: str, ids: dict[str, str]) -> str:
    """An SPDX expression with SPDX's spelling of each identifier; exits on one SPDX doesn't know."""
    def canonical(m: re.Match) -> str:
        word = m.group(0)
        if word in ("AND", "OR", "WITH") or word.startswith("LicenseRef-"):
            return word
        if word.lower() not in ids:
            sys.exit(f"license {word!r} (in {expression!r}) is no SPDX identifier: correct it in VTK_LICENSE_CORRECTIONS")
        return ids[word.lower()]
    return re.sub(r"[A-Za-z0-9.+-]+", canonical, expression)


def vtk_source(buildtrees: Path, vtk: str) -> Path:
    """VTK's sources as vcpkg extracted them into its build trees (build.py vtk built VTK here, not from the binary
    cache)."""
    sources = sorted((p for p in (buildtrees / "vtk" / "src").glob(f"v{vtk}-*") if (p / "CMakeLists.txt").is_file()),
                     key=lambda p: p.stat().st_mtime)
    if not sources:
        sys.exit(f"no VTK {vtk} sources under {buildtrees / 'vtk' / 'src'}: build VTK here (build.py vtk), not from a "
                 "binary cache, or pass --source")
    return sources[-1]


def vtk_components(source: Path, ids: dict[str, str]) -> dict[str, dict]:
    """By license folder, every module of VTK's sources that installs one: VTK's own modules as "vtk", the libraries it
    builds in (third-party modules, ThirdParty/ and Utilities/) under their own names, with what their declarations say."""
    components = {}
    for module_file in sorted(source.rglob("vtk.module")):
        module = vtk_module_values(module_file.read_text(encoding="utf-8", errors="replace"))
        library = (module.get("LIBRARY_NAME") or [module["NAME"][0].replace("VTK::", "vtk")])[0]
        place = module_file.parent.relative_to(source).parts[0]
        declared = third_party_declaration(module_file.parent) if "THIRD_PARTY" in module else module
        license_files = declared.get("LICENSE_FILES", [])
        if not license_files:
            continue
        # a third party installs its license files under their own paths (vtkexpat/COPYING), a module under its library's
        folder = Path(license_files[0]).parts[0] if "THIRD_PARTY" in module else library
        download = (declared.get("SPDX_DOWNLOAD_LOCATION") or [""])[0]
        if place in ("ThirdParty", "Utilities"):
            repository = re.match(r"(?:git\+)?(.+?)(?:@[^@/]*)?$", download).group(1) if download else ""
            name = Path(repository).stem if repository.endswith(".git") else library.removeprefix("vtk")
        else:
            name = "vtk"
        entry = {"name": name}
        version = (declared.get("VERSION") or [""])[0]
        if name != "vtk" and version and "${" not in version:
            entry["version"] = version
        license_id = VTK_LICENSE_CORRECTIONS.get(folder, " ".join(declared.get("SPDX_LICENSE_IDENTIFIER", [])))
        if license_id:
            entry["license"] = spdx_expression(license_id, ids)
        copyright_text = [c for c in declared.get("SPDX_COPYRIGHT_TEXT", []) if c]
        if copyright_text:
            entry["copyright"] = copyright_text
        if name != "vtk" and download and "${" not in download:
            entry["download"] = download
        components[folder] = entry
    return components


def write_vtk_components(source: Path, vtk: str) -> int:
    """pack/vtk-components.json from VTK's sources; the number of license folders."""
    components = vtk_components(source, spdx_license_ids())
    VTK_COMPONENTS.write_text(json.dumps({"vtk": vtk, "folders": components}, indent=2, ensure_ascii=False) + "\n",
                              encoding="utf-8")
    return len(components)


# --------------------------------------------------------------------------- CycloneDX and BSI TR-03183-2
# The same in netvtk-core/sbom.py and netocc-core/sbom.py: a change here goes into both.
def licenses(terms: list[str]) -> list[dict]:
    """The license a component ships under (BSI TR-03183-2's distribution license) as one concluded SPDX expression.
    CycloneDX takes nothing beside an expression: the texts go into the evidence."""
    return [{"expression": " AND ".join(terms), "acknowledgement": "concluded"}]


def evidence(texts: list[tuple[str, str]]) -> dict:
    """License texts, (term, text) each, as the evidence of a component's licenses; a reference or a term with an
    exception goes by name, which CycloneDX's ids can't spell."""
    found = []
    for term, text in texts:
        key = "name" if " WITH " in term or term.startswith("LicenseRef-") else "id"
        found.append({"license": {key: term, "text": {"contentType": "text/plain", "content": text}}})
    return {"licenses": found}


def copyright_lines(text: str) -> str:
    """The copyright lines of a license text, each once."""
    lines = []
    for line in text.splitlines():
        line = line.strip()
        if re.match(r"(Copyright\b|\(c\)|©)", line, re.I) and line not in lines:
            lines.append(line)
    return "\n".join(lines)


def source(location: str) -> tuple[str, str | None] | None:
    """The repository (without .git) and the ref of a git+<repository>@<ref>#<path> source; None for an archive."""
    match = re.match(r"git\+(.+?)(?:\.git)?(?:@([^#]+))?(?:#.*)?$", location)
    return (match.group(1), match.group(2)) if match else None


def source_references(location: str) -> list[dict]:
    """Where a library's source comes from: a repository, the ref as a comment, and the location itself, a
    git+<repository>@<ref> or an archive, which names the exact source (BSI TR-03183-2's source code URI)."""
    references = []
    if repository := source(location):
        vcs = {"type": "vcs", "url": repository[0]}
        if repository[1]:
            vcs["comment"] = repository[1]
        references.append(vcs)
    return references + [{"type": "source-distribution", "url": location}]


def bsi_properties(executable: bool, archive: bool, structured: bool, filename: str | None = None) -> list[dict]:
    """BSI TR-03183-2's properties of a component that is a file, by its CycloneDX taxonomy."""
    properties = [{"name": "bsi:component:filename", "value": filename}] if filename else []
    return properties + [
        {"name": "bsi:component:executable", "value": "executable" if executable else "non-executable"},
        {"name": "bsi:component:archive", "value": "archive" if archive else "no archive"},
        {"name": "bsi:component:structured", "value": "structured" if structured else "unstructured"}]


def own_name(package: str) -> str:
    """The project a runtime package belongs to (NetOcc.Vtk.runtime.win-x64: NetOcc.Vtk)."""
    return package.split(".runtime.")[0]


def package_rid(package: str) -> str:
    """The RID a runtime package's name ends in (NetOcc.Vtk.runtime.win-x64: win-x64)."""
    return package.split(".runtime.")[1]


def creator(package: str, repository: str) -> dict:
    """The creator of the SBOM, the package and the project's own libraries: the project (NetOcc of NetOcc.Vtk)."""
    return {"name": own_name(package).split(".")[0], "url": [repository]}


def root_reference(package: str) -> str:
    """The reference of the package itself, the SBOM's root; pack adds the version."""
    return f"pkg:nuget/{package}"


def own_reference(package: str) -> str:
    """The reference of the project's own libraries in a runtime package's SBOM, the package's version theirs."""
    return f"{package}/{own_name(package).lower()}"


def own_component(package: str, repository: str, description: str) -> dict:
    """The project's own libraries, under its LICENSE (MIT); the version is the package's, which pack stamps in."""
    text = (ROOT / "LICENSE").read_text(encoding="utf-8").strip()
    return {"type": "library", "bom-ref": own_reference(package),
            "manufacturer": creator(package, repository), "name": own_name(package), "version": "",
            "description": description, "licenses": licenses(["MIT"]), "copyright": copyright_lines(text),
            "externalReferences": [{"type": "vcs", "url": repository}], "evidence": evidence([("MIT", text)])}


def file_component(package: str, name: str, owner: dict) -> dict:
    """A file of the package as a component of the library it belongs to, with that library's creator, version and
    license: a shared library, executable, its parts no longer told apart (unstructured). Pack adds its hash."""
    where = f"runtimes/{package_rid(package)}/native/{name}"
    return {"type": "file", "bom-ref": f"{package}/{where}", "manufacturer": owner["manufacturer"], "name": name,
            "version": owner["version"], "licenses": owner["licenses"],
            "externalReferences": [{"type": "distribution", "url": where}],
            "properties": bsi_properties(True, False, False, name)}


def add_files(stage: Path, package: str, owner_of: Callable[[str], dict]) -> None:
    """Each file of the stage as a component of the library owner_of names by the file's name."""
    for path in sorted(stage.iterdir(), key=lambda p: p.name.lower()):
        owner = owner_of(path.name)
        owner.setdefault("components", []).append(file_component(package, path.name, owner))


def walk(components: list[dict]) -> Iterator[dict]:
    """Every component, each nested one after the one it belongs to."""
    for component in components:
        yield component
        yield from walk(component.get("components", []))


def timestamp() -> str:
    return f"{datetime.now(timezone.utc):%Y-%m-%dT%H:%M:%SZ}"


def document(package: str, repository: str, parts: list[dict], dependencies: list[dict]) -> dict:
    """A runtime package's SBOM: the package its root, under the licenses of all its parts; the parts its components;
    the dependencies, an empty entry of each file added. Every file of the package is listed (complete); not what the
    libraries load from the platform, the C and C++ runtime, the system's libraries, OpenGL (incomplete)."""
    made_by = creator(package, repository)
    terms = []
    for part in parts:
        terms += [t for t in re.split(r"\s+AND\s+", part["licenses"][0]["expression"]) if t not in terms]
    root = root_reference(package)
    dependencies = dependencies + [{"ref": f["bom-ref"], "dependsOn": []} for p in parts for f in p.get("components", [])]
    return {"bomFormat": "CycloneDX", "specVersion": SPEC, "serialNumber": f"urn:uuid:{uuid.uuid4()}", "version": 1,
            "metadata": {"timestamp": timestamp(), "manufacturer": made_by,
                         "component": {"type": "library", "bom-ref": root, "manufacturer": made_by, "name": package,
                                       "version": "", "licenses": licenses(terms), "purl": root,
                                       "externalReferences": [{"type": "vcs", "url": repository}],
                                       "properties": bsi_properties(False, True, True)}},
            "components": parts,
            "dependencies": dependencies,
            "compositions": [{"aggregate": "complete", "assemblies": [root, *(p["bom-ref"] for p in parts)]},
                             {"aggregate": "incomplete", "dependencies": [d["ref"] for d in dependencies]}]}


def save(sbom: dict, path: Path) -> None:
    path.write_text(json.dumps(sbom, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def stamp(path: Path, version: str) -> None:
    """What pack knows: the package's version (in its purl and reference, and of the project's own libraries), its
    file name, each file's SHA-512 (of the stage beside the SBOM), and the SBOM's serial number and time. Exits when the
    stage's files are not the SBOM's."""
    if not path.is_file():
        sys.exit(f"{path} missing: run build.py native for its RID again")
    stage = path.parent / "native"
    sbom = json.loads(path.read_text(encoding="utf-8"))
    package = sbom["metadata"]["component"]
    old, new = package["bom-ref"], f"{root_reference(package['name'])}@{version}"
    package.update({"version": version, "bom-ref": new, "purl": new})
    package["properties"] = [{"name": "bsi:component:filename", "value": f"{package['name']}.{version}.nupkg"},
                             *(p for p in package["properties"] if p["name"] != "bsi:component:filename")]
    sbom["serialNumber"] = f"urn:uuid:{uuid.uuid4()}"
    sbom["metadata"]["timestamp"] = timestamp()
    rename = lambda ref: new if ref == old else ref
    for dependency in sbom["dependencies"]:
        dependency.update({"ref": rename(dependency["ref"]), "dependsOn": [rename(r) for r in dependency["dependsOn"]]})
    for composition in sbom.get("compositions", []):
        for key in ("assemblies", "dependencies"):
            if key in composition:
                composition[key] = [rename(r) for r in composition[key]]

    for component in sbom["components"]:
        if component["bom-ref"] == own_reference(package["name"]):
            for part in walk([component]):
                part["version"] = version
    listed = set()
    for component in walk(sbom["components"]):
        if component["type"] != "file":
            continue
        listed.add(component["name"])
        file = stage / component["name"]
        if not file.is_file():
            sys.exit(f"{component['name']} of {path} is not in {stage}: run build.py native for its RID again")
        hashes = [{"alg": "SHA-512", "content": hashlib.sha512(file.read_bytes()).hexdigest()}]
        component["hashes"] = hashes
        for reference in component.get("externalReferences", []):
            if reference["type"] == "distribution":
                reference["hashes"] = hashes
    unlisted = sorted(p.name for p in stage.iterdir() if p.name not in listed)
    if unlisted:
        sys.exit(f"{', '.join(unlisted)} of {stage} not in {path}: run build.py native for its RID again")
    save(sbom, path)


# --------------------------------------------------------------------------- the runtime package's SBOM
def license_text(folder: Path) -> str:
    """The license files of a folder VTK installs, one after another."""
    files = sorted((p for p in folder.rglob("*") if p.is_file()), key=lambda p: str(p).lower())
    texts = [p.read_text(encoding="utf-8", errors="replace").strip() for p in files]
    if len(files) == 1:
        return texts[0]
    return "\n\n".join(f"{p.relative_to(folder).as_posix()}\n\n{text}" for p, text in zip(files, texts))


def license_terms(expression: str) -> list[str]:
    """The AND terms of an SPDX expression. An OR needs a decision first: none of VTK's builds in has one."""
    if re.search(r"\bOR\b", expression):
        sys.exit(f"the license {expression!r} offers a choice: decide which one NetVtk ships under")
    return [term.strip("() ") for term in re.split(r"\s+AND\s+", expression.strip("() "))]


def purl(name: str, version: str | None) -> str:
    from urllib.parse import quote
    return f"pkg:generic/{quote(name, safe='')}" + (f"@{quote(version, safe='.-_')}" if version else "")


def has_version(value: str) -> bool:
    """A version, not a branch (master, for/vtk) or nothing."""
    return bool(re.search(r"\d", value))


def port_patches(repository: str) -> list[dict]:
    """The port's patches of VTK, at the commit that builds them."""
    try:
        commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, check=True, capture_output=True,
                                text=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        commit = "main"
    port = ROOT / "vcpkg" / "ports" / "vtk"
    return [{"type": "unofficial",
             "diff": {"url": f"{repository}/blob/{commit}/{port.relative_to(ROOT.parent).as_posix()}/{patch.name}"}}
            for patch in sorted(port.glob("*.patch"))]


def library_name(file_name: str) -> str | None:
    """A shared library's name without its platform's prefix, version and extension (vtkzlib-9.7.dll,
    libvtkzlib-9.7.so.1, libvtkzlib-9.7.1.dylib: vtkzlib); None for another file."""
    match = re.fullmatch(r"(?:lib)?(.+?)(?:-\d+(?:\.\d+)*)?(?:\.so(?:\.\d+)*|\.dylib|\.dll)", file_name, re.I)
    return match.group(1) if match else None


def write(stage: Path, target: Path, package: str, *, prefix: Path, vtk: str, repository: str) -> None:
    """A runtime package's SBOM: NetVtk's libraries, VTK of the install prefix, patched by the port, under the
    licenses of its modules' folders, and each library VTK builds in with a license folder, as vtk-components.json
    declares them, each with its files of the stage. A reference names the package first: the win-x64 VTK is another
    than the win-x86 one. Exits on a folder vtk-components.json lacks and on a file that is no shared library."""
    data = json.loads(VTK_COMPONENTS.read_text(encoding="utf-8"))
    if data["vtk"] != vtk:
        sys.exit(f"{VTK_COMPONENTS.name} describes VTK {data['vtk']}, the build is VTK {vtk}: run build.py third-party")
    folder_root = prefix / "share" / "vtk" / "licenses" / "VTK"
    folders = sorted((p for p in folder_root.iterdir() if p.is_dir()), key=lambda p: p.name.lower())
    missing = [folder.name for folder in folders if folder.name not in data["folders"]]
    if missing:
        sys.exit(f"{VTK_COMPONENTS.name} lacks VTK's license folders {', '.join(missing)}: run build.py third-party")

    vtk_terms = ["BSD-3-Clause"]
    vtk_texts = [("BSD-3-Clause", (folder_root / "Copyright.txt").read_text(encoding="utf-8").strip())]
    vtk_copyright: list[str] = []
    built_in = {}
    for folder in folders:
        entry = data["folders"][folder.name]
        text = license_text(folder)
        if entry["name"] == "vtk":
            for term in license_terms(entry.get("license", "BSD-3-Clause")):
                if term not in vtk_terms:
                    vtk_terms.append(term)
                    vtk_texts.append((term, text))
            vtk_copyright += [c for c in entry.get("copyright", []) if c not in vtk_copyright]
            continue
        if "license" not in entry:
            sys.exit(f"VTK declares no license for {folder.name}: correct it in VTK_LICENSE_CORRECTIONS")
        # a library of VTK's own sources without a release of its own (DICOMParser, MetaIO's master) is VTK's version's
        version = entry["version"] if has_version(entry.get("version", "")) else vtk
        terms = license_terms(entry["license"])
        ref = purl(entry["name"], version)
        download = entry.get("download")
        repository_of = source(download) if download else None
        component = {"type": "library", "bom-ref": f"{package}/{ref}",
                     "manufacturer": {"url": [repository_of[0] if repository_of else VTK_REPOSITORY]},
                     "name": entry["name"], "version": version,
                     "description": f"built into VTK {vtk} by VTK (its own copy, symbols mangled)",
                     "licenses": licenses(terms)}
        if entry.get("copyright"):
            component["copyright"] = "\n".join(entry["copyright"])
        component["purl"] = ref
        if download:
            component["externalReferences"] = source_references(download)
        component["evidence"] = evidence([(term, text) for term in terms])
        built_in[folder.name] = component
    vtk_ref = purl("vtk", vtk)
    vtk_component = {"type": "library", "bom-ref": f"{package}/{vtk_ref}",
                     "manufacturer": {"name": "Kitware, Inc.", "url": [VTK_HOMEPAGE]}, "name": "vtk", "version": vtk,
                     "licenses": licenses(vtk_terms), "copyright": "\n".join(vtk_copyright), "purl": vtk_ref,
                     "externalReferences": [{"type": "website", "url": VTK_HOMEPAGE},
                                            *source_references(f"git+{VTK_REPOSITORY}@v{vtk}")],
                     "evidence": evidence(vtk_texts), "pedigree": {"patches": port_patches(repository)}}
    own = own_component(package, repository, "NetVtk's native libraries, the C side of its bindings of VTK")

    # a library VTK builds in by its license folder (vtkzlib), and a second library of it (vtkhdf5_hl) by that
    owners = sorted(built_in.items(), key=lambda item: -len(item[0]))

    def owner_of(name: str) -> dict:
        library = library_name(name)
        if library is None:
            sys.exit(f"{name} in {stage} is no shared library: no library of the SBOM it belongs to")
        if library.startswith("NetVtk"):
            return own
        return next((c for folder, c in owners if library == folder or library.startswith(f"{folder}_")), vtk_component)

    add_files(stage, package, owner_of)
    built_in_refs = [c["bom-ref"] for c in built_in.values()]
    dependencies = [{"ref": root_reference(package), "dependsOn": [own["bom-ref"], vtk_component["bom-ref"]]},
                    {"ref": own["bom-ref"], "dependsOn": [vtk_component["bom-ref"]]},
                    {"ref": vtk_component["bom-ref"], "dependsOn": built_in_refs},
                    *({"ref": ref, "dependsOn": []} for ref in built_in_refs)]
    save(document(package, repository, [own, vtk_component, *built_in.values()], dependencies), target)
