#!/usr/bin/env python3
"""Fast, dependency-free VPM package structure and boundary check."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ROOT = REPOSITORY_ROOT / "Packages" / "com.tentee.vrc-kazamachi"
ERRORS: list[str] = []


def fail(message: str) -> None:
    ERRORS.append(message)


def text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def package_relative(path: Path) -> Path:
    return path.relative_to(PACKAGE_ROOT)


def guid(path: Path) -> str | None:
    match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", text(path), re.MULTILINE)
    return match.group(1) if match else None


def check_manifest() -> None:
    manifest_path = PACKAGE_ROOT / "package.json"
    try:
        manifest = json.loads(text(manifest_path))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"invalid package.json: {exc}")
        return

    if manifest.get("name") != "com.tentee.vrc-kazamachi":
        fail("package name must be com.tentee.vrc-kazamachi")
    version = manifest.get("version")
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
        fail("package version must be SemVer")
    if manifest.get("unity") != "2022.3":
        fail("package must target Unity 2022.3")
    deps = manifest.get("vpmDependencies", {})
    for dependency in ("com.vrchat.avatars", "nadena.dev.ndmf"):
        if not isinstance(deps.get(dependency), str) or not deps[dependency]:
            fail(f"package must declare {dependency} in vpmDependencies")
    if "nadena.dev.modular-avatar" in deps:
        fail("package must not depend on Modular Avatar")


def check_meta_pairs() -> dict[str, Path]:
    seen: dict[str, Path] = {}

    def ignored(path: Path) -> bool:
        return any(part.endswith("~") for part in package_relative(path).parts)

    for path in PACKAGE_ROOT.rglob("*"):
        if ignored(path) or path.is_dir() or path.suffix == ".meta" or path.name.startswith("."):
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            fail(f"missing .meta: {package_relative(path)}")

    for meta in PACKAGE_ROOT.rglob("*.meta"):
        if ignored(meta):
            continue
        target = Path(str(meta)[:-5])
        if not target.exists():
            fail(f"orphan .meta: {package_relative(meta)}")
        value = guid(meta)
        if value is None:
            fail(f"missing GUID: {package_relative(meta)}")
        elif value in seen:
            fail(f"duplicate GUID {value}: {package_relative(seen[value])} and {package_relative(meta)}")
        else:
            seen[value] = meta
    return seen


def check_asmdefs(meta_guids: dict[str, Path]) -> None:
    asmdefs: dict[str, Path] = {}
    for path in PACKAGE_ROOT.rglob("*.asmdef"):
        try:
            data = json.loads(text(path))
        except json.JSONDecodeError as exc:
            fail(f"invalid asmdef JSON {package_relative(path)}: {exc.msg}")
            continue
        asmdefs[data.get("name", "")] = path
    for name in ("TenteEEEE.Kazamachi.Runtime", "TenteEEEE.Kazamachi.Editor"):
        if name not in asmdefs:
            fail(f"required assembly missing: {name}")
    editor = asmdefs.get("TenteEEEE.Kazamachi.Editor")
    if editor and json.loads(text(editor)).get("includePlatforms") != ["Editor"]:
        fail("Editor asmdef includePlatforms must be [Editor]")
    for source in PACKAGE_ROOT.rglob("*.cs"):
        if "AvatarWind" in text(source):
            fail(f"source contains old AvatarWind name: {package_relative(source)}")
    plugin = PACKAGE_ROOT / "Editor" / "KazamachiBuildPlugin.cs"
    if not plugin.is_file() or not re.search(r"\[assembly:\s*ExportsPlugin\(typeof\([^)]*KazamachiBuildPlugin\)\)\]", text(plugin)):
        fail("KazamachiBuildPlugin ExportsPlugin attribute is missing")



def check_text_encoding() -> None:
    # A non-UTF-8 write turns Japanese into runs of '?', which still parses fine.
    for path in PACKAGE_ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in (".cs", ".md", ".json", ".asmdef"):
            continue
        try:
            source = path.read_bytes().decode("utf-8")
        except UnicodeDecodeError:
            fail(f"not UTF-8: {package_relative(path)}")
            continue
        if re.search(r"[^\s?]\?{3,}", source):
            fail(f"likely mojibake ('???'): {package_relative(path)}")


def check_folder_metas() -> None:
    for path in PACKAGE_ROOT.rglob("*"):
        if path.is_dir() and not Path(str(path) + ".meta").is_file():
            fail(f"missing folder .meta: {package_relative(path)}")


def check_release_files() -> None:
    required = (
        REPOSITORY_ROOT / ".github" / "workflows" / "release.yml",
        REPOSITORY_ROOT / ".gitignore", REPOSITORY_ROOT / ".gitattributes",
        REPOSITORY_ROOT / "README.md", REPOSITORY_ROOT / "README.ja.md",
        REPOSITORY_ROOT / "LICENSE", REPOSITORY_ROOT / "Tools" / "verify-package.py",
    )
    for path in required:
        if not path.is_file():
            fail(f"release project file missing: {path.relative_to(REPOSITORY_ROOT)}")
    for path in (REPOSITORY_ROOT / "Packages.meta", REPOSITORY_ROOT / "Packages" / "com.tentee.vrc-kazamachi.meta"):
        if path.exists():
            fail(f"unexpected Unity project metadata at repository root: {path.relative_to(REPOSITORY_ROOT)}")
    for path in REPOSITORY_ROOT.rglob("*"):
        if path.is_file() and path.suffix.lower() in (".dll", ".rsp"):
            fail(f"build artifact must not remain in output: {path.relative_to(REPOSITORY_ROOT)}")

    runtime = PACKAGE_ROOT / "Runtime" / "KazamachiWind.cs"
    if not runtime.is_file() or "public sealed class KazamachiWind" not in text(runtime):
        fail("KazamachiWind runtime component is missing")
    runtime_meta = PACKAGE_ROOT / "Runtime" / "KazamachiWind.cs.meta"
    if not runtime_meta.is_file() or guid(runtime_meta) != "265eabde39285484a84891f62d22b0fc":
        fail("KazamachiWind must retain the component GUID")


def main() -> int:
    try:
        if not PACKAGE_ROOT.is_dir():
            fail(f"package directory is missing: {PACKAGE_ROOT.relative_to(REPOSITORY_ROOT)}")
        else:
            check_manifest()
            metadata = check_meta_pairs()
            check_asmdefs(metadata)
            check_text_encoding()
            check_folder_metas()
        check_release_files()
    except OSError as exc:
        fail(f"file access error: {exc}")

    if ERRORS:
        print("PACKAGE CHECK FAILED")
        for error in ERRORS:
            print("- " + error)
        return 1
    print("PACKAGE CHECK PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
