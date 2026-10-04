#!/usr/bin/env python3
"""Fast, dependency-free VPM package structure and boundary check."""
from __future__ import annotations

import json
import re
import sys
import zlib
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
        if (
            path.is_file()
            and path.suffix.lower() in (".dll", ".rsp")
            and "_work" not in path.relative_to(REPOSITORY_ROOT).parts
        ):
            fail(f"build artifact must not remain in output: {path.relative_to(REPOSITORY_ROOT)}")

    runtime = PACKAGE_ROOT / "Runtime" / "KazamachiWind.cs"
    if not runtime.is_file() or "public sealed class KazamachiWind" not in text(runtime):
        fail("KazamachiWind runtime component is missing")
    runtime_meta = PACKAGE_ROOT / "Runtime" / "KazamachiWind.cs.meta"
    if not runtime_meta.is_file() or guid(runtime_meta) != "265eabde39285484a84891f62d22b0fc":
        fail("KazamachiWind must retain the component GUID")



ICON_GUIDS = {
    "Kazamachi": ("efaaa1d7f0314fd6af710a974d678a66", "f68dfb1b7fa44dc2b1c06e1818eb00df"),
    "Wind": ("c2002acd2ac14bd28246f5030b4188c4", "f57cb7dd289e444f87951a94350ac1c0"),
    "Direction": ("387adb1b1bb445edbdb8eb6eb907cdbd", "21d2760a1d554f98981821932ad80673"),
    "Strength": ("5b6b5d46786e43b5a5f03a3d0a306cba", "605406c6c0c7412ba764f7019d3a2129"),
    "Turbulence": ("6d44e52dd3ca486398d10927c832cea6", "b542cf339d2b48ecabdc7cca00187490"),
    "Elevation": ("6357f96426374d3bab0507aaed990cda", "af88516d9e6d4dbaa7f26845ce3c8e55"),
}


def png_has_transparent_and_opaque(path: Path) -> tuple[bool, bool]:
    import struct

    raw = path.read_bytes()
    if raw[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("invalid PNG signature")
    offset = 8
    compressed = bytearray()
    width = height = bit_depth = color_type = None
    while offset < len(raw):
        length = struct.unpack(">I", raw[offset:offset + 4])[0]
        kind = raw[offset + 4:offset + 8]
        data = raw[offset + 8:offset + 8 + length]
        offset += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(">IIBBBBB", data)
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            break
    if (width, height, bit_depth, color_type) != (256, 256, 8, 6):
        raise ValueError("PNG must be 256x256, 8-bit RGBA")
    decoded = zlib.decompress(compressed)
    stride = width * 4
    previous = bytearray(stride)
    has_zero = has_full = False

    def paeth(a: int, b: int, c: int) -> int:
        estimate = a + b - c
        da, db, dc = abs(estimate - a), abs(estimate - b), abs(estimate - c)
        return a if da <= db and da <= dc else b if db <= dc else c

    for y in range(height):
        row_offset = y * (stride + 1)
        filter_type = decoded[row_offset]
        scan = bytearray(decoded[row_offset + 1:row_offset + 1 + stride])
        for i in range(stride):
            left = scan[i - 4] if i >= 4 else 0
            above = previous[i]
            upper_left = previous[i - 4] if i >= 4 else 0
            if filter_type == 1:
                scan[i] = (scan[i] + left) & 255
            elif filter_type == 2:
                scan[i] = (scan[i] + above) & 255
            elif filter_type == 3:
                scan[i] = (scan[i] + ((left + above) // 2)) & 255
            elif filter_type == 4:
                scan[i] = (scan[i] + paeth(left, above, upper_left)) & 255
            elif filter_type != 0:
                raise ValueError(f"unsupported PNG filter {filter_type}")
        for i in range(3, stride, 4):
            has_zero |= scan[i] == 0
            has_full |= scan[i] == 255
        previous = scan
    return has_zero, has_full


def check_icons() -> None:
    icons = PACKAGE_ROOT / "Icons"
    plugin = PACKAGE_ROOT / "Editor" / "KazamachiBuildPlugin.cs"
    plugin_source = text(plugin) if plugin.is_file() else ""
    for name, (expected_guid, expected_sprite) in ICON_GUIDS.items():
        image = icons / f"{name}.png"
        meta = Path(str(image) + ".meta")
        if not image.is_file():
            fail(f"missing menu icon: {package_relative(image)}")
        else:
            try:
                transparent, opaque = png_has_transparent_and_opaque(image)
                if not transparent or not opaque:
                    fail(f"icon must contain fully transparent and opaque pixels: {package_relative(image)}")
            except (OSError, ValueError, zlib.error) as exc:
                fail(f"invalid icon {package_relative(image)}: {exc}")
        if not meta.is_file():
            fail(f"missing icon .meta: {package_relative(meta)}")
            continue
        source = text(meta)
        if "TextureImporter:" not in source:
            fail(f"icon meta must use TextureImporter: {package_relative(meta)}")
        if guid(meta) != expected_guid:
            fail(f"unexpected icon GUID for {name}")
        if f"spriteID: {expected_sprite}" not in source:
            fail(f"unexpected spriteID for {name}")
        if "enableMipMap: 0" not in source or "alphaIsTransparency: 1" not in source:
            fail(f"icon importer transparency settings are invalid for {name}")
        if any(int(value) > 256 for value in re.findall(r"maxTextureSize:\s*(\d+)", source)):
            fail(f"icon maxTextureSize exceeds 256 for {name}")
        if expected_guid not in plugin_source:
            fail(f"build plugin is missing icon GUID for {name}")

    for source in PACKAGE_ROOT.rglob("*.cs"):
        contents = text(source)
        if re.search(r"\b(?:syncToOthers|startEnabled)\b|networkSynced\s*=(?!\s*true\b)", contents, re.IGNORECASE):
            fail(f"removed sync/start-enabled code remains: {package_relative(source)}")


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
            check_icons()
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
