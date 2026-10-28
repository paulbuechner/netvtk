#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""netvtk-core build driver.

    python build.py tools                           # project-local SWIG from conda-forge (micromamba env in .tools/)
    python build.py vtk --triplet x64-windows       # VTK via the vcpkg manifest, repo-local
                                                    # --prune-archives: then drop the cached archives no install uses
    python build.py generate                        # SWIG: src/SWIG_files/wrapper/*.i -> build/generated
    python build.py native --triplet x64-windows    # CMake: NetVtk<Group> libraries + VTK's -> artifacts/runtimes/<rid>/native
    python build.py third-party                     # pack/vtk-components.json from VTK's sources, after a VTK update
    python build.py test --arch x64 [--framework net10.0|net8.0|net6.0|net48|net472|net462|net35] [--no-rendering]
                                                    # no --framework on Windows: every framework, net35 on CLR 2,
                                                    # plus the net45/net452 library builds
    python build.py pack [--version 9.7.1.1] [--rids win-x64,win-x86]  # NuGet: NetVtk + NetVtk.runtime.<rid>
    python build.py test-package [--arch x64]       # the packed NetVtk, consumed like an app would
    python build.py change fixed "what changed"     # a changelog entry for the next release: changes/<name>.<type>.md
    python build.py changelog [--version 9.7.1.1]   # a release's notes; default: what's pending
                                                    # --pull-request next: its release PR's description
    python build.py changelog --check origin/main   # fail if the packages changed since then without an entry
    python build.py version [--ci RUN | --latest]   # the version to pack, or the newest release
    python build.py release [--prerelease next]     # write the next release into CHANGELOG.md (next.1, next.2, ...)
    python build.py registry [--check]              # versions/ at the repository root: the vcpkg registry of the ports

Everything vcpkg touches stays under .vcpkg/ (install trees, build trees, downloads, binary cache, registry cache);
nothing is installed globally.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

import sbom

ROOT = Path(__file__).resolve().parent
TOOLS = ROOT / ".tools"
VCPKG_STATE = ROOT / ".vcpkg"
BUILD = ROOT / "build"
GENERATED = BUILD / "generated"
ARTIFACTS = ROOT / "artifacts"
SWIG_FILES = ROOT / "src" / "SWIG_files"

SWIG_VERSION = "4.5.1"
MICROMAMBA_VERSION = "2.9.0-0"
# micromamba's SHA-256 per conda-forge platform, for MICROMAMBA_VERSION: the release's own .sha256 files only catch a
# truncated download
MICROMAMBA_SHA256 = {
    "win-64": "a6d804394b2418991c4e29562853eaace2f2ce9d9da661a98e74e02e8dbb44b0",
    "linux-64": "366cd9cd8be14df1ab8ed50352a82111082a36686b2d389fdb79a92c3fafb3e3",
    "osx-arm64": "ec2a072f028e1a7cf20f3e2e74d5a8127cf5a5f27636375b5359811565f4e5be",
}

PACKAGES = ARTIFACTS / "packages"
# VTK's documentation comments as NetVtk's XML documentation (build.py docs): VTK's text, so artifacts only
DOCUMENTATION = ARTIFACTS / "docs" / "NetVtk.xml"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
CHANGELOG = ROOT / "CHANGELOG.md"
# pending changelog entries, one file each: <name>.<type>.md (python build.py change)
CHANGES = ROOT / "changes"
# Keep a Changelog's sections, in its order: the entry type -> the heading
CHANGE_TYPES = {"added": "Added", "changed": "Changed", "deprecated": "Deprecated", "removed": "Removed", "fixed": "Fixed",
                "security": "Security"}
REPOSITORY = "https://github.com/paulbuechner/netvtk"

# triplet -> (.NET RID, vcvars arch)
TRIPLETS = {
    "x64-windows": ("win-x64", "x64"),
    "x86-windows": ("win-x86", "x86"),
    "x64-linux-dynamic": ("linux-x64", None),
    "arm64-osx-dynamic": ("osx-arm64", None),
}


def run(cmd: list[str], env: dict[str, str] | None = None, cwd: Path | None = None) -> None:
    print("+", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run([str(c) for c in cmd], check=True, env=env, cwd=cwd)


def host_is_windows() -> bool:
    return os.name == "nt"


def host_is_macos() -> bool:
    return sys.platform == "darwin"


def utc_stamp() -> str:
    return f"{datetime.now(timezone.utc):%Y%m%d%H%M%S}"


def vcpkg_installed(triplet: str) -> Path:
    return VCPKG_STATE / f"installed-{triplet}"


def vcpkg_prefix(triplet: str) -> Path:
    """The install prefix of a triplet: include/, lib/, bin/, share/."""
    return vcpkg_installed(triplet) / triplet


def natives(rid: str) -> Path:
    """The staged native libraries of a RID, which the runtime package holds."""
    return ARTIFACTS / "runtimes" / rid / "native"


# --------------------------------------------------------------------------- tools
# SWIG from conda-forge, in a project-local environment: micromamba (one static binary, pinned) creates it
SWIG_PREFIX = TOOLS / "swig"
MICROMAMBA = TOOLS / ("micromamba.exe" if host_is_windows() else "micromamba")
MICROMAMBA_RELEASES = f"https://github.com/mamba-org/micromamba-releases/releases/download/{MICROMAMBA_VERSION}"


def conda_platform() -> str:
    """The conda-forge platform of this host; the build runs on these three."""
    import platform
    platforms = {("Windows", "AMD64"): "win-64", ("Linux", "x86_64"): "linux-64", ("Darwin", "arm64"): "osx-arm64"}
    key = (platform.system(), platform.machine())
    if key not in platforms:
        sys.exit(f"no conda-forge SWIG for {key[0]} on {key[1]}")
    return platforms[key]


def swig_executable() -> Path:
    exe = SWIG_PREFIX / ("Library/bin/swig.exe" if host_is_windows() else "bin/swig")
    if not exe.exists():
        sys.exit("project-local SWIG missing; run: python build.py tools")
    return exe


def swig_env() -> dict[str, str]:
    """SWIG's library by path, where the platform's package puts it (Library/bin/Lib on Windows, share/swig/<version>
    elsewhere), whatever path the build compiled in."""
    library = next(SWIG_PREFIX.rglob("swig.swg"), None)
    if library is None:
        sys.exit(f"SWIG's library (swig.swg) missing in {SWIG_PREFIX}; run: python build.py tools")
    return dict(os.environ, SWIG_LIB=str(library.parent))


def download_micromamba() -> None:
    import hashlib
    import urllib.request
    platform = conda_platform()
    asset = f"micromamba-{platform}"
    with urllib.request.urlopen(f"{MICROMAMBA_RELEASES}/{asset}", timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != MICROMAMBA_SHA256[platform]:
        sys.exit(f"{asset} {MICROMAMBA_VERSION}: checksum mismatch")
    TOOLS.mkdir(exist_ok=True)
    MICROMAMBA.write_bytes(data)
    MICROMAMBA.chmod(0o755)


def cmd_tools(_: argparse.Namespace) -> None:
    stamp = TOOLS / "micromamba.version"
    if not MICROMAMBA.exists() or not stamp.exists() or stamp.read_text().strip() != MICROMAMBA_VERSION:
        download_micromamba()
        stamp.write_text(MICROMAMBA_VERSION)
    if not list((SWIG_PREFIX / "conda-meta").glob(f"swig-{SWIG_VERSION}-*.json")):
        shutil.rmtree(SWIG_PREFIX, ignore_errors=True)
        # --no-rc: no user .condarc/.mambarc; the package cache stays in .tools/mamba
        run([MICROMAMBA, "create", "--yes", "--no-rc", "--root-prefix", TOOLS / "mamba", "--prefix", SWIG_PREFIX,
             "--override-channels", "--channel", "conda-forge", f"swig={SWIG_VERSION}"])
    run([swig_executable(), "-version"], env=swig_env())


# --------------------------------------------------------------------------- vtk
def vcpkg_exe() -> str:
    root = os.environ.get("VCPKG_ROOT")
    if root:
        cand = Path(root) / ("vcpkg.exe" if host_is_windows() else "vcpkg")
        if cand.exists():
            return str(cand)
    found = shutil.which("vcpkg")
    if not found:
        sys.exit("vcpkg not found (set VCPKG_ROOT or put vcpkg on PATH)")
    return found


def vcpkg_env() -> dict[str, str]:
    env = dict(os.environ)
    archives = VCPKG_STATE / "archives"
    archives.mkdir(parents=True, exist_ok=True)
    env["VCPKG_BINARY_SOURCES"] = f"clear;files,{archives},readwrite"
    env["X_VCPKG_REGISTRIES_CACHE"] = str(VCPKG_STATE / "registries")
    (VCPKG_STATE / "registries").mkdir(parents=True, exist_ok=True)
    env["VCPKG_DOWNLOADS"] = str(VCPKG_STATE / "downloads")
    (VCPKG_STATE / "downloads").mkdir(parents=True, exist_ok=True)
    env["VCPKG_DISABLE_METRICS"] = "1"
    return env


def archive_names() -> set[str]:
    return {p.name for p in (VCPKG_STATE / "archives").glob("*/*.zip")}


def installed_abis(install_root: Path) -> set[str]:
    """ABIs of the packages installed under a vcpkg install root, from its status database (status + updates/)."""
    database = install_root / "vcpkg"
    packages: dict[tuple[str, str, str], dict[str, str]] = {}
    for file in [database / "status", *sorted((database / "updates").glob("*"))]:
        if not file.is_file():
            continue
        for paragraph in re.split(r"\n\s*\n", file.read_text(encoding="utf-8")):
            fields = dict(line.split(": ", 1) for line in paragraph.splitlines() if ": " in line and not line[0].isspace())
            if "Package" in fields:
                # a later paragraph for the same package and feature replaces the earlier one
                packages[(fields["Package"], fields.get("Architecture", ""), fields.get("Feature", ""))] = fields
    return {f["Abi"] for f in packages.values() if f.get("Status") == "install ok installed" and "Abi" in f}


def prune_archives() -> None:
    """Deletes the binary cache archives (<abi[:2]>/<abi>.zip) that no install root uses."""
    keep = set().union(*(installed_abis(root) for root in VCPKG_STATE.glob("installed-*")))
    if not keep:
        return
    archives = VCPKG_STATE / "archives"
    for archive in archives.glob("*/*.zip"):
        if archive.stem not in keep:
            print(f"pruned {archive.relative_to(archives)}", flush=True)
            archive.unlink()
    for folder in archives.iterdir():
        if folder.is_dir() and not any(folder.iterdir()):
            folder.rmdir()


def cmd_vtk(args: argparse.Namespace) -> None:
    before = archive_names()
    host = "x64-windows" if host_is_windows() else args.triplet
    # the overlay triplets come from vcpkg-configuration.json
    run([vcpkg_exe(), "install",
         f"--triplet={args.triplet}", f"--host-triplet={host}",
         f"--x-install-root={vcpkg_installed(args.triplet)}",
         f"--x-buildtrees-root={VCPKG_STATE / 'buildtrees'}",
         f"--x-packages-root={VCPKG_STATE / 'packages'}"],
        env=vcpkg_env(), cwd=ROOT)
    if args.prune_archives:
        prune_archives()
        changed = archive_names() != before
        print(f"binary cache {'changed' if changed else 'unchanged'}", flush=True)
        if output := os.environ.get("GITHUB_OUTPUT"):
            with open(output, "a", encoding="utf-8") as f:
                f.write(f"archives-changed={'true' if changed else 'false'}\n")


# --------------------------------------------------------------------------- generate
# a SWIG message: file(line) : Warning n: text on Windows, file:line: Warning n: text elsewhere
SWIG_WARNING = re.compile(r"(?P<file>[^\\/]+?)\.i(?:\(\d+\) ?|:\d+): Warning (?P<number>\d+): (?P<text>.*)")


def native_libraries() -> dict[str, list[str]]:
    """The SWIG modules (one per VTK module) per native library, NetVtk<Group>, from modules.json: a Windows DLL exports
    at most 65535 functions."""
    path = SWIG_FILES / "modules.json"
    if not path.exists():
        sys.exit(f"{path.relative_to(ROOT)} missing")
    return {f"NetVtk{group}": modules for group, modules in json.loads(path.read_text(encoding="utf-8")).items()}


def platform_libraries() -> dict[str, str]:
    """The native libraries of one platform's classes and that platform's OS as .NET's RIDs name it (win, linux, osx),
    from platforms.json: built and packed there only."""
    path = SWIG_FILES / "platforms.json"
    if not path.exists():
        sys.exit(f"{path.relative_to(ROOT)} missing")
    return {f"NetVtk{group}": os_name for group, os_name in json.loads(path.read_text(encoding="utf-8")).items()}


def library_sources(cxx_dir: Path, library: str, platforms: dict[str, str]) -> Path:
    """Where generate puts a native library's C++, which CMake builds: cxx/<library>, a platform's library in
    cxx/<os>/<library>, which CMake builds on that OS only."""
    return cxx_dir / platforms[library] / library if library in platforms else cxx_dir / library


def sift_swig_messages(module: str, messages: str) -> tuple[list[str], int, list[str]]:
    """A module's SWIG messages without the harmless Warning 401 pairs: the lines to print, the pairs left out, and the
    Warning 401 lines left in, each a class SWIG gave C# without its base.

    Warning 401 comes in pairs, "Base class 'X' undefined." at a class and "'X' must be defined before it is used as a base
    class." at X: a class of an imported module deriving from a class of this module, which comes after the imports. No
    code comes of an imported class: harmless."""
    lines = messages.splitlines()
    kept, lost, harmless = [], [], 0
    i = 0
    while i < len(lines):
        first = SWIG_WARNING.search(lines[i])
        second = SWIG_WARNING.search(lines[i + 1]) if i + 1 < len(lines) else None
        base = re.match(r"Base class '(\w+)' undefined", first["text"]) if first and first["number"] == "401" else None
        if base and second and second["file"] == module \
                and second["text"] == f"'{base.group(1)}' must be defined before it is used as a base class.":
            harmless += 1
            i += 2
            continue
        if first and first["number"] == "401":
            lost.append(lines[i])
        kept.append(lines[i])
        i += 1
    return kept, harmless, lost


def cmd_generate(_: argparse.Namespace) -> None:
    swig = swig_executable()
    env = swig_env()
    libraries = native_libraries()
    platforms = platform_libraries()
    library_of = {module: library for library, modules in libraries.items() for module in modules}
    cxx_dir = GENERATED / "cxx"
    cs_root = GENERATED / "cs"
    if GENERATED.exists():
        shutil.rmtree(GENERATED)
    cxx_dir.mkdir(parents=True)

    # C++ per library (CMake builds one from each directory, a platform's on that platform only), C# per module: every
    # platform's in NetVtk.dll
    for library in libraries:
        library_sources(cxx_dir, library, platforms).mkdir(parents=True)

    def swig_module(module: str) -> tuple[str, subprocess.CompletedProcess[str]]:
        cs_dir = cs_root / module
        cs_dir.mkdir(parents=True)
        cmd = [swig, "-csharp", "-c++",
               "-namespace", "Vtk",
               "-dllimport", library_of[module],
               "-I" + str(SWIG_FILES / "common"),
               "-I" + str(SWIG_FILES / "wrapper"),
               "-I" + str(SWIG_FILES),  # %include "extras/<Module>.i"
               "-outdir", cs_dir,
               "-o", library_sources(cxx_dir, library_of[module], platforms) / f"{module}_wrap.cxx",
               SWIG_FILES / "wrapper" / f"{module}.i"]
        return module, subprocess.run([str(c) for c in cmd], env=env, capture_output=True, text=True,
                                      encoding="utf-8", errors="replace")

    # one SWIG process per module, in parallel; each module's messages print as one block
    failed, dropped, unbased = [], [], []
    harmless = 0
    with ThreadPoolExecutor(os.cpu_count() or 4) as pool:
        for module, result in pool.map(swig_module, library_of):
            messages, pairs, lost = sift_swig_messages(module, (result.stdout + result.stderr).strip())
            harmless += pairs
            if messages:
                print(f"{module}:\n" + "\n".join(messages), flush=True)
            if result.returncode != 0:
                failed.append(module)
            # Warning 516: SWIG dropped an overload it couldn't tell from another
            if any("Warning 516" in line for line in messages):
                dropped.append(module)
            if lost:
                unbased.append(module)
    if failed:
        sys.exit(f"SWIG failed for {', '.join(failed)}")
    in_types = fix_director_in_types(cs_root)
    lookups = cache_director_lookups(cs_root)
    if dropped:
        sys.exit(f"SWIG dropped overloads (Warning 516) in {', '.join(dropped)}")
    if unbased:
        sys.exit(f"SWIG dropped base classes (Warning 401) in {', '.join(unbased)}")
    # a SWIGTYPE_* wrapper (SWIGTYPE_p_T for a pointer, SWIGTYPE_T by value) is a type no typemap covered
    opaque = sorted(str(f.relative_to(cs_root)) for f in cs_root.rglob("*.cs") if "SWIGTYPE_" in f.read_text(encoding="utf-8"))
    if opaque:
        sys.exit(f"SWIG left unmapped types (SWIGTYPE_*) in {len(opaque)} file(s), e.g. {', '.join(opaque[:5])}")
    print(f"SWIG: {len(library_of)} modules in {len(libraries)} libraries -> {GENERATED}; {harmless} imported classes seen "
          f"before their base (Warning 401, left out); {lookups} director classes, {in_types} parameter types by in",
          flush=True)


# a director's method types (SwigDerivedClassHasMethod), as SWIG writes them from the cstype: it spells ref and out
# parameters' types as T.MakeByRefType(), but an in one as typeof(in T), which C# doesn't take
DIRECTOR_IN_TYPE = re.compile(r"typeof\(in ([\w.]+)\)")


def fix_director_in_types(cs_root: Path) -> int:
    """Rewrites SWIG's typeof(in T) in director classes to typeof(T).MakeByRefType(); the number rewritten."""
    count = 0
    for path in cs_root.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "typeof(in " not in text:
            continue
        fixed, n = DIRECTOR_IN_TYPE.subn(r"typeof(\1).MakeByRefType()", text)
        path.write_text(fixed, encoding="utf-8", newline="")
        count += n
    return count


# SWIG's lookup of a C# override in a director class, which reflects over the object's type on every call of a virtual
# member: replaced by Directors.Overrides, which caches it per type and member
DIRECTOR_LOOKUP = re.compile(
    r"  private bool SwigDerivedClassHasMethod\(string methodName, global::System\.Type\[\] methodTypes\) \{\n"
    r".*?methodInfo\.DeclaringType\.IsSubclassOf\(typeof\((\w+)\)\).*?\n    return false;\n  \}\n", re.S)


def cache_director_lookups(cs_root: Path) -> int:
    """Replaces SwigDerivedClassHasMethod in the director classes with Directors.Overrides; the classes changed."""
    count = 0
    for path in cs_root.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "private bool SwigDerivedClassHasMethod(" not in text:
            continue
        fixed, n = DIRECTOR_LOOKUP.subn(
            lambda m: "  // SWIG's lookup of an override, cached per type and member (Directors.Overrides)\n"
                      "  private bool SwigDerivedClassHasMethod(string methodName, global::System.Type[] methodTypes) {\n"
                      f"    return global::Vtk.Directors.Overrides(this.GetType(), typeof({m.group(1)}), methodName, methodTypes);\n"
                      "  }\n", text)
        if n != 1:
            sys.exit(f"{path.relative_to(cs_root)}: SWIG's SwigDerivedClassHasMethod not found as expected")
        path.write_text(fixed, encoding="utf-8", newline="")
        count += 1
    return count


# --------------------------------------------------------------------------- native
def msvc_env(arch: str) -> dict[str, str]:
    """The environment of vcvarsall.bat for the newest Visual Studio with the C++ tools."""
    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / \
        "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    if not vswhere.exists():
        sys.exit(f"{vswhere} missing: install Visual Studio with the C++ tools")
    install = subprocess.run(
        [str(vswhere), "-latest", "-products", "*",
         "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
         "-property", "installationPath"],
        check=True, capture_output=True, text=True).stdout.strip()
    if not install:
        sys.exit("no Visual Studio with the C++ tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64)")
    vcvars = Path(install) / "VC" / "Auxiliary" / "Build" / "vcvarsall.bat"
    # the environment as JSON (ASCII): cmd's own output is in the OEM code page, which garbles non-ASCII paths
    dump = f'"{sys.executable}" -c "import json, os; print(json.dumps(dict(os.environ)))"'
    out = subprocess.run(f'"{vcvars}" {arch} >nul && {dump}', shell=True, check=True, capture_output=True, text=True).stdout
    return json.loads(out)


def macos_deployment_target() -> str:
    """The oldest macOS the libraries load on (their minos): the arm64-osx triplet's, which VTK is built for."""
    triplet = ROOT / "vcpkg" / "triplets" / "arm64-osx-dynamic.cmake"
    match = re.search(r"^set\(VCPKG_OSX_DEPLOYMENT_TARGET\s+([\d.]+)\)", triplet.read_text(encoding="utf-8"), re.M)
    if not match:
        sys.exit(f"{triplet.relative_to(ROOT)} sets no VCPKG_OSX_DEPLOYMENT_TARGET")
    return match.group(1)


def cmd_native(args: argparse.Namespace) -> None:
    rid, vc_arch = TRIPLETS[args.triplet]
    prefix = vcpkg_prefix(args.triplet)
    if not (prefix / "share" / "vtk").is_dir():
        sys.exit(f"VTK missing for {args.triplet}: run build.py vtk --triplet {args.triplet}")
    if not (GENERATED / "cxx").is_dir():
        sys.exit("SWIG output missing: run build.py generate")
    build_dir = BUILD / f"native-{args.triplet}"
    stage = natives(rid)
    env = msvc_env(vc_arch) if vc_arch else dict(os.environ)
    configure = ["cmake", "-S", ROOT, "-B", build_dir, "-G", "Ninja",
                 "-DCMAKE_BUILD_TYPE=Release",
                 f"-DCMAKE_PREFIX_PATH={prefix}",
                 f"-DNETVTK_GENERATED_DIR={GENERATED / 'cxx'}",
                 f"-DNETVTK_VTK_VERSION={vtk_version()}",
                 f"-DNETVTK_VTK_RUNTIME_DIR={prefix / ('bin' if host_is_windows() else 'lib')}",
                 f"-DCMAKE_INSTALL_PREFIX={stage}"]
    if host_is_macos():
        configure.append(f"-DCMAKE_OSX_DEPLOYMENT_TARGET={macos_deployment_target()}")
    run(configure, env=env)
    run(["cmake", "--build", build_dir, "--parallel"], env=env)
    if stage.exists():
        shutil.rmtree(stage)
    run(["cmake", "--install", build_dir], env=env)
    if not host_is_windows():
        flatten_stage(stage)
    write_notices(args.triplet, stage, stage.parent / "THIRD-PARTY-NOTICES.txt")
    sbom.write(stage, stage.parent / sbom.FILE, f"NetVtk.runtime.{rid}", prefix=prefix, vtk=vtk_version(),
               repository=REPOSITORY)


def loader_name(library: Path) -> str | None:
    """The file name the dynamic loader asks for: the SONAME (Linux) or the install name (macOS)."""
    if host_is_macos():
        lines = subprocess.run(["otool", "-D", str(library)], check=True, capture_output=True, text=True).stdout.splitlines()
        return Path(lines[1].strip()).name if len(lines) > 1 else None
    out = subprocess.run(["readelf", "-d", str(library)], check=True, capture_output=True, text=True).stdout
    match = re.search(r"\(SONAME\)\s+Library soname: \[(.+?)\]", out)
    return match.group(1) if match else None


def flatten_stage(stage: Path) -> None:
    """One file per library, under the name the loader asks for.

    The install keeps the symlink chains (libvtkCommonCore-9.7.so -> .so.1 -> .so.9.7.1); a copy or a NuGet package
    stores every link as a full file, about three times the size. Also drops pkgconfig/ and VTK's
    other directories (cmake/, vtk-9.7/)."""
    for entry in sorted(stage.iterdir()):
        if entry.is_symlink():
            entry.unlink()
        elif entry.is_dir():
            shutil.rmtree(entry)
    for library in sorted(stage.iterdir()):
        name = loader_name(library)
        if name and name != library.name:
            library.rename(stage / name)


def write_notices(triplet: str, stage: Path, target: Path) -> None:
    """NetVtk's license and NOTICE, then VTK's: its Copyright.txt and the licenses it installs for its modules and the
    third-party libraries it builds in (share/vtk/licenses: every library the stage holds is VTK's), for the runtime
    package."""
    parts = [("NetVtk", (ROOT / "LICENSE").read_text(encoding="utf-8")),
             ("NetVtk NOTICE", (ROOT / "NOTICE").read_text(encoding="utf-8"))]
    share = vcpkg_prefix(triplet) / "share" / "vtk"
    parts.append(("VTK", (share / "copyright").read_text(encoding="utf-8", errors="replace")))
    licenses = share / "licenses" / "VTK"
    for path in sorted(licenses.rglob("*"), key=lambda p: str(p.relative_to(licenses)).lower()):
        if path.is_file() and path.relative_to(licenses).parts != ("Copyright.txt",):
            parts.append((f"VTK: {path.relative_to(licenses).as_posix()}", path.read_text(encoding="utf-8", errors="replace")))
    rule = "=" * 78
    target.write_text("".join(f"{rule}\n{name}\n{rule}\n\n{text.strip()}\n\n" for name, text in parts), encoding="utf-8")


def cmd_third_party(args: argparse.Namespace) -> None:
    vtk = vtk_version()
    source = Path(args.source) if args.source else sbom.vtk_source(VCPKG_STATE / "buildtrees", vtk)
    count = sbom.write_vtk_components(source, vtk)
    print(f"{sbom.VTK_COMPONENTS.relative_to(ROOT)}: {count} license folders from {source}")


# --------------------------------------------------------------------------- test
# test project frameworks; the .NET Framework ones build everywhere but run on Windows only
FRAMEWORKS = ["net10.0", "net8.0", "net6.0", "net48", "net472", "net462", "net35"]
MODERN_FRAMEWORKS = [f for f in FRAMEWORKS if "." in f]
# library builds no test framework picks by itself (NUnit 4 needs 4.6.2): the net48 tests run against each
LEGACY_LIBRARY_BUILDS = ["net45", "net452"]
TESTS = ROOT / "test" / "NetVtk.Tests"


def clr2_installed() -> bool:
    """.NET Framework 3.5 (CLR 2), an optional Windows feature."""
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5") as key:
            return winreg.QueryValueEx(key, "Install")[0] == 1
    except OSError:
        return False


# the tests that render, which need OpenGL 3.2: Mesa's llvmpipe under Xvfb on Linux, a GPU elsewhere
RENDERING_CATEGORY = "Rendering"


def run_net35(arch: str, rendering: bool = True) -> None:
    """The net35 target: an NUnitLite exe on CLR 2, which VSTest can't host. Its exit code counts the failures."""
    if not clr2_installed():
        message = "net35 tests skipped: .NET Framework 3.5 (CLR 2) is not installed"
        print(f"::warning::{message}" if os.environ.get("GITHUB_ACTIONS") else f"warning: {message}", flush=True)
        return
    run(["dotnet", "build", TESTS / "NetVtk.Tests.csproj", "-c", "Release", "--framework", "net35", "--arch", arch])
    where = [] if rendering else ["--where", f"cat != {RENDERING_CATEGORY}"]
    run([TESTS / "bin" / "Release" / "net35" / f"win-{arch}" / "NetVtk.Tests.exe", "--noresult", *where])


def cmd_test(args: argparse.Namespace) -> None:
    if args.framework and args.framework not in MODERN_FRAMEWORKS and not host_is_windows():
        sys.exit(f"{args.framework}: .NET Framework tests run on Windows only")
    cmd = ["dotnet", "test", TESTS / "NetVtk.Tests.csproj", "-c", "Release", "--arch", args.arch]
    if args.no_rendering:
        cmd += ["--filter", f"TestCategory!={RENDERING_CATEGORY}"]
    rendering = not args.no_rendering
    if args.framework == "net35":
        run_net35(args.arch, rendering)
    elif args.framework:
        run(cmd + ["--framework", args.framework])
    elif not host_is_windows():
        for framework in MODERN_FRAMEWORKS:
            run(cmd + ["--framework", framework])
    else:
        for build in LEGACY_LIBRARY_BUILDS:
            run(cmd + ["--framework", "net48", f"-p:NetVtkTarget={build}"])
        run(cmd)  # last, so bin/ ends up with every framework's own library build; net35 builds but isn't a VSTest target
        if args.arch in ("x64", "x86"):
            run_net35(args.arch, rendering)


# --------------------------------------------------------------------------- versions and the changelog
# a NetVtk version, prerelease label included, and where a CHANGELOG.md section ends: the next one, the links, the end
VERSION = r"\d+\.\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?"
SECTION_END = r"(?=^## \[|^\[[^\]\n]+\]: |\Z)"


def vtk_version() -> str:
    """The VTK version the bindings are for: what vcpkg.json builds, which netvtk-gen stamps into every module it
    writes."""
    overrides = json.loads((ROOT / "vcpkg.json").read_text(encoding="utf-8")).get("overrides", [])
    built = next((o.get("version") for o in overrides if o.get("name") == "vtk"), None)
    if not built:
        sys.exit("vcpkg.json overrides no vtk version")
    stamped = {m.group(1) for path in (SWIG_FILES / "wrapper").glob("*.i")
               if (m := re.search(r"generated by netvtk-gen from VTK (\d+\.\d+\.\d+)", path.read_text(encoding="utf-8")))}
    if stamped and stamped != {built}:
        sys.exit(f"the modules are generated from VTK {', '.join(sorted(stamped))}, vcpkg.json builds {built}: "
                 "regenerate them with netvtk-gen")
    return built


def read_changelog() -> str:
    return CHANGELOG.read_text(encoding="utf-8")


def released_versions(changelog: str) -> list[str]:
    """The versions the changelog has sections for, newest first."""
    return re.findall(rf"^## \[({VERSION})\]", changelog, re.M)


def next_revision(vtk: str, released: list[str]) -> int:
    """NetVtk's next revision for a VTK version: one after the last release for it, 1 for a new VTK version. A
    prerelease doesn't count: the release after it has its revision. (NuGet shows 9.7.1.0 as 9.7.1, so revisions start
    at 1.)"""
    revisions = [int(v.split(".")[3]) for v in released if v.startswith(f"{vtk}.") and "-" not in v]
    return max(revisions, default=0) + 1


def local_version(vtk: str) -> str:
    return f"{vtk}.{next_revision(vtk, released_versions(read_changelog()))}-local.{utc_stamp()}"


def prerelease_label(label: str, vtk: str, revision: int, released: list[str]) -> str:
    """A prerelease's label: a numbered one as given (preview.1), a bare one numbered after its revision's last with it
    (next: next.1, then next.2), as changesets numbers a pre mode's releases."""
    if re.search(r"\.\d+$", label):
        return label
    prefix = f"{vtk}.{revision}-{label}."
    numbers = [int(v[len(prefix):]) for v in released if v.startswith(prefix) and v[len(prefix):].isdigit()]
    return f"{label}.{max(numbers, default=0) + 1}"


def version_problem(version: str, vtk: str) -> str | None:
    """Why a version isn't one of NetVtk's, or None: the VTK version it wraps and NetVtk's revision (from 1),
    maybe with a prerelease."""
    if re.fullmatch(rf"{re.escape(vtk)}\.[1-9]\d*(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?", version):
        return None
    return f"version {version}: NetVtk's versions are VTK's and a revision from 1, e.g. {vtk}.1 or {vtk}.2-preview.1"


def check_version(version: str) -> None:
    if problem := version_problem(version, vtk_version()):
        sys.exit(problem)


def changelog_section(changelog: str, version: str | None) -> str | None:
    """The body of a version's section (None: [Unreleased]); None if the changelog has no such section."""
    heading = re.escape(version) if version else "Unreleased"
    match = re.search(rf"^## \[{heading}\][^\n]*\n(.*?){SECTION_END}", changelog, re.S | re.M)
    return match.group(1).strip() if match else None


def fragments() -> list[tuple[str, Path]]:
    """The pending entries: (type, file), in the order of the file names."""
    if not CHANGES.is_dir():
        return []
    found = []
    for path in sorted(CHANGES.glob("*.md")):
        kind = path.name[:-3].rsplit(".", 1)[-1]
        if kind not in CHANGE_TYPES:
            sys.exit(f"changes/{path.name}: name it <name>.<type>.md, type one of {', '.join(CHANGE_TYPES)}")
        if not path.read_text(encoding="utf-8").strip():
            sys.exit(f"changes/{path.name} is empty")
        found.append((kind, path))
    return found


def pending_entries() -> list[tuple[str, str]]:
    """The pending entries: (type, text)."""
    return [(kind, path.read_text(encoding="utf-8")) for kind, path in fragments()]


def as_bullet(text: str) -> list[str]:
    """An entry as one list item: its first line after "- ", the rest indented under it."""
    lines = text.strip().splitlines()
    first = lines[0] if lines[0].startswith("- ") else f"- {lines[0]}"
    return [first] + [f"  {line}" if line.strip() and not line.startswith("  ") else line for line in lines[1:]]


def parse_notes(body: str | None) -> dict[str, list[str]]:
    """A section's body by heading (### Added): each heading's lines."""
    notes: dict[str, list[str]] = {}
    heading = None
    for line in (body or "").splitlines():
        if line.startswith("### "):
            heading = line[4:].strip()
            notes.setdefault(heading, [])
        elif heading is not None:
            notes[heading].append(line)
    return notes


def merge_notes(into: dict[str, list[str]], notes: dict[str, list[str]]) -> None:
    """Adds each heading's lines after the ones under the same heading, without the blank lines between."""
    for heading, lines in notes.items():
        entries = into.setdefault(heading, [])
        while entries and not entries[-1].strip():
            entries.pop()
        start = next((i for i, line in enumerate(lines) if line.strip()), len(lines))
        entries += lines[start:]


def render_notes(notes: dict[str, list[str]]) -> str | None:
    """Notes as a section's body: Keep a Changelog's headings in its order, others after; None without any."""
    if not notes:
        return None
    order = list(CHANGE_TYPES.values()) + [h for h in notes if h not in CHANGE_TYPES.values()]
    return "\n\n".join(f"### {heading}\n\n" + "\n".join(notes[heading]).strip("\n") for heading in order if heading in notes)


def pending_notes(changelog: str, entries: list[tuple[str, str]]) -> str | None:
    """What the next release says: the [Unreleased] section, if the changelog still has one, with the entries (type, text)
    added under their headings; None if nothing is pending."""
    notes = parse_notes(changelog_section(changelog, None))
    for kind, text in entries:
        merge_notes(notes, {CHANGE_TYPES[kind]: as_bullet(text)})
    return render_notes(notes)


def release_notes(changelog: str, version: str, pending: str | None) -> str | None:
    """A release's notes: what's pending, and for a release its prereleases' notes before (a prerelease writes and deletes
    the entries it has, and the release lists them too); None if there's nothing to say."""
    if "-" in version:
        return pending
    notes: dict[str, list[str]] = {}
    for prerelease in reversed([v for v in released_versions(changelog) if v.startswith(f"{version}-")]):
        merge_notes(notes, parse_notes(changelog_section(changelog, prerelease)))
    merge_notes(notes, parse_notes(pending))
    return render_notes(notes)


def with_release(changelog: str, version: str, notes: str, date: str) -> str:
    """The changelog with a release written in: its section where [Unreleased] was, else before the first release, and its
    link first among the links; [Unreleased]'s link goes with its section."""
    section = f"## [{version}] - {date}\n\n{notes}\n\n"
    unreleased = re.search(rf"^## \[Unreleased\][^\n]*\n.*?{SECTION_END}", changelog, re.S | re.M)
    if unreleased:
        text = changelog[:unreleased.start()] + section + changelog[unreleased.end():]
    else:
        first = re.search(r"^## \[|^\[[^\]\n]+\]: |\Z", changelog, re.M)
        text = changelog[:first.start()] + section + changelog[first.start():]
    text = re.sub(r"^\[Unreleased\]: [^\n]*\n?", "", text, flags=re.M).rstrip("\n") + "\n"
    link = f"[{version}]: {REPOSITORY}/releases/tag/v{version}\n"
    links = re.search(r"^\[[^\]\n]+\]: ", text, re.M)
    return text[:links.start()] + link + text[links.start():] if links else f"{text}\n{link}"


# the warning signs around changesets' pre mode note
PRE_MODE_SIGNS = "⚠️" * 6


def release_pr_body(version: str, notes: str, branch: str) -> str:
    """The release pull request's description: changesets' "Version Packages" text word for word, where NetVtk differs
    only in its facts (nuget.org, changelog entries, next's pre mode ends in a release from main), then the release."""
    parts = [f"This PR was opened by the [NetVtk release]({REPOSITORY}/blob/{branch}/.github/workflows/release.yml) GitHub "
             "action. When you're ready to do a release, you can merge this and the packages will be published to nuget.org "
             "automatically. If you're not ready to do a release yet, that's fine, whenever you add more changelog entries "
             f"to {branch}, this PR will be updated."]
    if branch == "next":
        parts += [PRE_MODE_SIGNS,
                  "`next` is currently in **pre mode** so this branch has prereleases rather than normal releases. If you "
                  "want to exit prereleases, merge `next` into `main` and run `gh workflow run release.yml` on `main`.",
                  PRE_MODE_SIGNS]
    parts += ["# Releases", f"## NetVtk@{version}", notes]
    return "\n\n".join(parts)


def cmd_changelog(args: argparse.Namespace) -> None:
    if args.check:
        check_changes(args.check)
        return
    if args.pull_request and not args.version:
        sys.exit("--pull-request needs --version: the release it describes")
    changelog = read_changelog()
    section = changelog_section(changelog, args.version) if args.version else pending_notes(changelog, pending_entries())
    if section is None:
        sys.exit(f"CHANGELOG.md has no section [{args.version}]" if args.version else "nothing pending: no changes/ and no [Unreleased]")
    if args.pull_request:
        section = release_pr_body(args.version, section, args.pull_request)
    if args.output:
        Path(args.output).write_bytes((section + "\n").encode("utf-8"))
    else:
        sys.stdout.buffer.write((section + "\n").encode("utf-8"))  # UTF-8 on any console code page


def check_changes(base: str) -> None:
    """A pull request that changes what the packages hold says so: an entry in changes/ (or an edit of CHANGELOG.md)."""
    changed = subprocess.run(["git", "diff", "--name-only", f"{base}...HEAD", "--", "."], cwd=ROOT, check=True,
                             capture_output=True, text=True).stdout.split()
    shipped = [p for p in changed if p.startswith(("netvtk-core/src/", "netvtk-core/pack/"))]
    noted = [p for p in changed if p.startswith("netvtk-core/changes/") or p == "netvtk-core/CHANGELOG.md"]
    if shipped and not noted:
        sys.exit("this changes the packages but adds no changelog entry: python build.py change <type> \"what changed\" "
                 "(or the label 'no changelog' for changes users don't see)")
    fragments()  # names and contents
    print(f"changelog: {len(noted)} entr{'y' if len(noted) == 1 else 'ies'} for {len(shipped)} shipped file(s)")


def cmd_change(args: argparse.Namespace) -> None:
    """A changelog entry for the next release, like a changeset: changes/<name>.<type>.md."""
    text = " ".join(args.text).strip()
    if not text:
        sys.exit("say what changed")
    name = args.name or "-".join(re.findall(r"[a-z0-9]+", text.lower())[:6]) or utc_stamp()
    CHANGES.mkdir(exist_ok=True)
    path = CHANGES / f"{name}.{args.type}.md"
    if path.exists():
        sys.exit(f"changes/{path.name} exists: pick another --name")
    path.write_bytes((text + "\n").encode("utf-8"))
    print(f"changes/{path.name}")


def cmd_version(args: argparse.Namespace) -> None:
    """The version to pack: a CI build's, a local one's, or the newest release CHANGELOG.md has (empty if none)."""
    vtk = vtk_version()
    released = released_versions(read_changelog())
    if args.latest:
        print(next(iter(released), ""))
    elif args.ci:
        print(f"{vtk}.{next_revision(vtk, released)}-ci.{args.ci}")
    else:
        print(local_version(vtk))


def cmd_release(args: argparse.Namespace) -> None:
    """Writes the next release into CHANGELOG.md: the pending notes under ## [version] - date, the entries deleted. Prints
    the version (nothing with --if-pending when no entry is pending)."""
    changelog = read_changelog()
    pending = pending_notes(changelog, pending_entries())
    if pending is None and args.if_pending:
        return
    vtk = vtk_version()
    released = released_versions(changelog)
    revision = args.revision or next_revision(vtk, released)
    version = f"{vtk}.{revision}" + (f"-{prerelease_label(args.prerelease, vtk, revision, released)}" if args.prerelease else "")
    check_version(version)
    if version in released:
        sys.exit(f"CHANGELOG.md already has [{version}]")
    notes = release_notes(changelog, version, pending)
    if notes is None:
        sys.exit("nothing to release: no changes/, no [Unreleased] and no prerelease of it")
    CHANGELOG.write_bytes(with_release(changelog, version, notes, f"{datetime.now(timezone.utc):%Y-%m-%d}").encode("utf-8"))
    for _, path in fragments():
        path.unlink()
    print(version)


# --------------------------------------------------------------------------- packages
PACKAGE_TESTS = ROOT / "test" / "NetVtk.PackageTests"


def pack_notes(version: str) -> str:
    """A package's release notes: a release's section, or for a CI or local build what's pending."""
    changelog = read_changelog()
    if "-local." in version or "-ci." in version:
        return pending_notes(changelog, pending_entries()) or ""
    notes = changelog_section(changelog, version)
    if notes is None:
        sys.exit(f"CHANGELOG.md has no section [{version}]: python build.py release writes it")
    return notes


def write_restore_config() -> Path:
    """The package sources for the NetVtk restore, as a nuget.config: right after the runtime packs, two --source options
    took nuget.org for a local path."""
    config = BUILD / "pack-nuget.config"
    config.write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="{PACKAGES}" />
    <add key="nuget.org" value="{NUGET_ORG}" />
  </packageSources>
</configuration>
""", encoding="utf-8")
    return config


def cmd_pack(args: argparse.Namespace) -> None:
    version = args.version or local_version(vtk_version())
    check_version(version)
    rids = args.rids.split(",") if args.rids else [rid for rid, _ in TRIPLETS.values()]
    missing = [rid for rid in rids if not natives(rid).is_dir()]
    if missing:
        sys.exit(f"natives missing for {', '.join(missing)}: run build.py native there (or pass --rids)")
    if not (GENERATED / "cs").is_dir():
        sys.exit("SWIG output missing: run build.py generate")
    notes_file = BUILD / "release-notes.txt"
    notes_file.parent.mkdir(parents=True, exist_ok=True)
    notes_file.write_text(pack_notes(version) + "\n", encoding="utf-8")
    if PACKAGES.exists():
        shutil.rmtree(PACKAGES)
    PACKAGES.mkdir(parents=True)

    ci = ["-p:ContinuousIntegrationBuild=true"] if os.environ.get("GITHUB_ACTIONS") else []
    for rid in rids:
        sbom.stamp(natives(rid).parent / sbom.FILE, version)
        run(["dotnet", "pack", ROOT / "pack" / "NetVtk.runtime.csproj", "-c", "Release", "-o", PACKAGES,
             f"-p:NetVtkRid={rid}", f"-p:Version={version}", *ci])

    documentation = [f"-p:NetVtkDocumentation={DOCUMENTATION}"] if DOCUMENTATION.exists() else []
    if not documentation:
        print("warning: no VTK documentation (build.py docs): IntelliSense gets the hand-written members' only", flush=True)
    managed = [f"-p:Version={version}", f"-p:NetVtkReleaseNotesFile={notes_file}", *documentation, *ci]
    # the RID list as an environment variable: -p: splits values at ';' and ','
    env = dict(os.environ, NetVtkRuntimeRids=";".join(rids))
    project = ROOT / "src" / "NetVtk" / "NetVtk.csproj"
    run(["dotnet", "restore", project, "--configfile", write_restore_config(), *managed], env=env)
    run(["dotnet", "pack", project, "-c", "Release", "--no-restore", "-o", PACKAGES, *managed], env=env)

    # test/NetVtk.PackageTests picks this version up
    (PACKAGES / "NetVtk.version.props").write_text(
        f"<Project>\n  <PropertyGroup>\n    <NetVtkPackageVersion>{version}</NetVtkPackageVersion>\n  </PropertyGroup>\n</Project>\n",
        encoding="utf-8")
    print(f"NetVtk {version}: {', '.join(sorted(p.name for p in PACKAGES.glob('*.*nupkg')))}")


def cmd_docs(args: argparse.Namespace) -> None:
    """VTK's documentation comments as NetVtk's XML documentation file, which pack ships: netvtk-gen docs over VTK's
    installed headers and the net10.0 assembly. Needs the monorepo's netvtk-generator."""
    generator = ROOT.parent / "netvtk-generator"
    if not (generator / "src" / "NetVtk.Generator").is_dir():
        sys.exit(f"netvtk-gen missing at {generator}: docs needs the monorepo's netvtk-generator")
    prefix = vcpkg_prefix(args.triplet)
    if not (prefix / "share" / "vtk").is_dir():
        sys.exit(f"VTK missing at {prefix}: run build.py vtk --triplet {args.triplet}")
    output = ROOT / "src" / "NetVtk" / "bin" / "Release" / "net10.0"
    run(["dotnet", "build", ROOT / "src" / "NetVtk" / "NetVtk.csproj", "-c", "Release", "-f", "net10.0"])
    DOCUMENTATION.parent.mkdir(parents=True, exist_ok=True)
    run(["dotnet", "run", "--project", generator / "src" / "NetVtk.Generator", "-c", "Release", "--", "docs",
         "--vtk", prefix, "--core", ROOT, "--config", generator / "config" / "modules.yaml",
         "--assembly", output / "NetVtk.dll", "--xml", output / "NetVtk.xml", "--out", DOCUMENTATION])
    # the text is VTK's: its license goes into the package with it
    shutil.copyfile(prefix / "share" / "vtk" / "licenses" / "VTK" / "Copyright.txt",
                    DOCUMENTATION.parent / "VTK-LICENSE.txt")


def cmd_test_package(args: argparse.Namespace) -> None:
    if not (PACKAGES / "NetVtk.version.props").exists():
        sys.exit("no packages: run build.py pack first")
    frameworks = ["net8.0", "net48"] if host_is_windows() else ["net8.0"]
    for framework in frameworks:
        run(["dotnet", "test", PACKAGE_TESTS / "NetVtk.PackageTests.csproj", "-c", "Release", "--arch", args.arch,
             "--framework", framework])


# --------------------------------------------------------------------------- the vcpkg registry
# The repository is a vcpkg git registry for VTK: versions/ at its root names, per version of a port in vcpkg/ports, the
# git tree vcpkg checks out. Another project builds the same VTK from it: pinned at a NetVtk release's commit, with
# vcpkg/triplets copied and the port's features asked for as vcpkg.json asks for them.
REGISTRY_VERSIONS = ROOT.parent / "versions"
PORTS = ROOT / "vcpkg" / "ports"
VERSION_KEYS = ("version", "version-semver", "version-date", "version-string")


def git_output(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()


def port_entry(port: Path) -> tuple[str, dict]:
    """A port's name and its version database entry: its version, port-version and committed git tree."""
    manifest = json.loads((port / "vcpkg.json").read_text(encoding="utf-8"))
    key = next((k for k in VERSION_KEYS if k in manifest), None)
    if key is None:
        sys.exit(f"vcpkg/ports/{port.name}/vcpkg.json has no version")
    relative = port.relative_to(ROOT.parent).as_posix()
    # vcpkg reads the port from git: what isn't committed isn't the port
    if git_output("status", "--porcelain", "--", relative):
        sys.exit(f"{relative} has changes git doesn't have: commit them, then run build.py registry")
    entry = {"git-tree": git_output("rev-parse", f"HEAD:{relative}"), key: manifest[key],
             "port-version": manifest.get("port-version", 0)}
    return manifest["name"], entry


def registry_files() -> dict[Path, str]:
    """versions/ as the committed ports make it: each port's database with its current entry first, and the baseline."""
    files, baseline = {}, {}
    for port in sorted(p for p in PORTS.iterdir() if (p / "vcpkg.json").is_file()):
        name, entry = port_entry(port)
        path = REGISTRY_VERSIONS / f"{name[0]}-" / f"{name}.json"
        versions = json.loads(path.read_text(encoding="utf-8"))["versions"] if path.is_file() else []
        same = [v for v in versions if v.get("port-version", 0) == entry["port-version"]
                and all(v.get(k) == entry.get(k) for k in VERSION_KEYS)]
        if same and same[0]["git-tree"] != entry["git-tree"]:
            sys.exit(f"vcpkg/ports/{port.name} changed since its version was registered: raise port-version in its "
                     "vcpkg.json")
        if not same:
            versions.insert(0, entry)
        files[path] = json.dumps({"versions": versions}, indent=2) + "\n"
        version = next(entry[k] for k in VERSION_KEYS if k in entry)
        baseline[name] = {"baseline": version, "port-version": entry["port-version"]}
    files[REGISTRY_VERSIONS / "baseline.json"] = json.dumps({"default": baseline}, indent=2) + "\n"
    return files


def cmd_registry(args: argparse.Namespace) -> None:
    files = registry_files()
    stale = [path for path, text in files.items()
             if not path.is_file() or path.read_text(encoding="utf-8") != text]
    if args.check:
        if stale:
            sys.exit("versions/ doesn't register the committed ports: run python build.py registry and commit it ("
                     + ", ".join(p.relative_to(ROOT.parent).as_posix() for p in stale) + ")")
        print("versions/ registers the committed ports")
        return
    for path in stale:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(files[path], encoding="utf-8", newline="\n")
        print(f"wrote {path.relative_to(ROOT.parent).as_posix()}")


# --------------------------------------------------------------------------- command line
def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("tools", help="project-local SWIG from conda-forge").set_defaults(fn=cmd_tools)
    for name, fn, text in (("vtk", cmd_vtk, "VTK via vcpkg, repo-local"),
                           ("native", cmd_native, "the native libraries -> artifacts/runtimes/<rid>/native"),
                           ("docs", cmd_docs, "VTK's doc comments as NetVtk's XML documentation")):
        sp = sub.add_parser(name, help=text)
        sp.add_argument("--triplet", required=True, choices=sorted(TRIPLETS))
        if name == "vtk":
            sp.add_argument("--prune-archives", action="store_true",
                            help="then delete the binary cache archives no install root uses (CI)")
        sp.set_defaults(fn=fn)
    sub.add_parser("generate", help="SWIG over the modules -> build/generated").set_defaults(fn=cmd_generate)
    tpy = sub.add_parser("third-party", help="pack/vtk-components.json: VTK's modules and built-in libraries, from its sources")
    tpy.add_argument("--source", help="VTK's source tree; default: the one vcpkg extracted under .vcpkg/buildtrees")
    tpy.set_defaults(fn=cmd_third_party)
    t = sub.add_parser("test", help="the NetVtk tests")
    t.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    t.add_argument("--framework", choices=FRAMEWORKS, help="default: all (Windows), the .NET ones elsewhere")
    t.add_argument("--no-rendering", action="store_true",
                   help=f"leave out the tests that render (category {RENDERING_CATEGORY}): no OpenGL 3.2 on the machine")
    t.set_defaults(fn=cmd_test)
    pk = sub.add_parser("pack", help="NetVtk and NetVtk.runtime.<rid> -> artifacts/packages")
    pk.add_argument("--version", help="package version; default <VTK version>.<next revision>-local.<utc time>")
    pk.add_argument("--rids", help="comma-separated; default all four (natives from artifacts/runtimes/<rid>)")
    pk.set_defaults(fn=cmd_pack)
    tp = sub.add_parser("test-package", help="the packed NetVtk, consumed like an app")
    tp.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    tp.set_defaults(fn=cmd_test_package)
    c = sub.add_parser("changelog", help="a release's notes; default: what's pending (changes/, [Unreleased])")
    c.add_argument("--version", help="the section of this release")
    c.add_argument("--output", help="write to this file instead of stdout")
    c.add_argument("--pull-request", metavar="BRANCH", help="with --version: the description of its release PR on BRANCH")
    c.add_argument("--check", metavar="BASE", help="fail if the commits since BASE change the packages without an entry")
    c.set_defaults(fn=cmd_changelog)
    ch = sub.add_parser("change", help="a changelog entry for the next release: changes/<name>.<type>.md")
    ch.add_argument("type", choices=list(CHANGE_TYPES))
    ch.add_argument("text", nargs="+", help="what changed, for package users")
    ch.add_argument("--name", help="the file's name; default: from the text")
    ch.set_defaults(fn=cmd_change)
    v = sub.add_parser("version", help="the version to pack: <VTK version>.<revision>-ci.<run> or -local.<time>")
    v.add_argument("--ci", metavar="RUN", help="a CI build's version")
    v.add_argument("--latest", action="store_true", help="the newest release in CHANGELOG.md, empty if none")
    v.set_defaults(fn=cmd_version)
    r = sub.add_parser("release", help="write the next release into CHANGELOG.md from changes/ (and [Unreleased])")
    r.add_argument("--revision", type=int, help="default: one after the last release for this VTK version")
    r.add_argument("--prerelease", help="a prerelease label: numbered (preview.1), or bare and numbered after the last (next: next.1)")
    r.add_argument("--if-pending", action="store_true", help="do nothing, successfully, when no entry is pending")
    r.set_defaults(fn=cmd_release)
    g = sub.add_parser("registry", help="versions/ at the repository root: the vcpkg registry of the committed ports")
    g.add_argument("--check", action="store_true", help="fail if versions/ doesn't register them")
    g.set_defaults(fn=cmd_registry)
    return p


def main() -> None:
    args = parser().parse_args()
    try:
        args.fn(args)
    except subprocess.CalledProcessError as e:
        # run() printed the command
        command = e.cmd if isinstance(e.cmd, str) else Path(str(e.cmd[0])).name
        sys.exit(f"build.py {args.cmd}: {command} failed (exit code {e.returncode})")
    except FileNotFoundError as e:
        sys.exit(f"build.py {args.cmd}: {e.filename or e} not found")


if __name__ == "__main__":
    main()
