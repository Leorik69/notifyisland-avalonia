#!/usr/bin/env python3
"""check_utf8.py — fail the build if a publish output contains non-UTF-8 text.

NotifyIsland serialises settings.json, weather-cache.json, and SVG icon packs
via ``File.WriteAllText`` (UTF-8, no BOM) and ``File.ReadAllText`` (UTF-8).
If a 1.3.x-era stale artifact, a PowerShell ``Get-Content`` without
``-Encoding UTF8``, or a manual Notepad save on a non-UTF8 Windows machine
re-encodes a file as CP1251 / Latin-1, the app silently loses the user's
Cyrillic city name and falls back to defaults.

This script walks a directory, decodes every text-like file as strict UTF-8,
and exits non-zero on any decode error, UTF-16 BOM, or (with ``--strict-bom``)
UTF-8 BOM.

Usage:
    python3 tools/check_utf8.py publish/
    python3 tools/check_utf8.py dist/ --strict-bom
    python3 tools/check_utf8.py . --exclude build,bin,obj,publish,.git
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

TEXT_EXTS = {
    ".cs", ".csproj", ".sln", ".props", ".targets",
    ".json", ".axaml", ".xaml", ".resx", ".xml", ".config",
    ".md", ".txt", ".yml", ".yaml", ".toml", ".ini",
    ".html", ".htm", ".css", ".js", ".ts", ".py", ".ps1", ".sh",
    ".manifest", ".iss", ".gitignore", ".editorconfig",
}

BINARY_EXTS = {
    ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff",
    ".wav", ".mp3", ".ogg", ".flac", ".m4a",
    ".ttf", ".otf", ".woff", ".woff2",
    ".pdb", ".dll", ".exe", ".so", ".dylib", ".lib", ".a",
    ".zip", ".tar", ".gz", ".7z", ".rar",
    ".pdf", ".docx", ".xlsx", ".pptx",
}


def is_text_file(p: Path) -> bool:
    """Heuristic: known-binary exts are skipped, known-text exts are scanned.
    Files with unknown / no extension get a small byte-probe."""
    suffix = p.suffix.lower()
    if suffix in BINARY_EXTS:
        return False
    if suffix in TEXT_EXTS:
        return True
    try:
        with open(p, "rb") as f:
            sample = f.read(8192)
        sample.decode("utf-8")
        return True
    except UnicodeDecodeError:
        return False
    except OSError:
        return False


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", help="Directory to scan recursively.")
    ap.add_argument("--strict-bom", action="store_true",
                    help="Fail on UTF-8 BOM. Default: warn only (NotifyIsland writes no BOM).")
    ap.add_argument("--exclude", default="",
                    help="Comma-separated directory basenames to skip (e.g. 'build,bin,obj').")
    args = ap.parse_args()

    root = Path(args.path).resolve()
    if not root.is_dir():
        print(f"not a directory: {root}", file=sys.stderr)
        return 2

    excludes = {e.strip() for e in args.exclude.split(",") if e.strip()}

    failures: list[str] = []
    warnings: list[str] = []
    scanned = 0
    skipped = 0

    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in excludes]
        for name in filenames:
            p = Path(dirpath) / name
            if not is_text_file(p):
                skipped += 1
                continue
            scanned += 1
            try:
                with open(p, "rb") as f:
                    data = f.read()
            except OSError as e:
                failures.append(f"{p}: read error: {e}")
                continue

            rel = p.relative_to(root)

            # BOM checks
            if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
                failures.append(f"{rel}: UTF-16 BOM (file looks re-encoded as UTF-16).")
                continue
            if data.startswith(b"\xef\xbb\xbf"):
                msg = f"{rel}: UTF-8 BOM present (File.WriteAllText default is no BOM)."
                if args.strict_bom:
                    failures.append(msg)
                else:
                    warnings.append(msg)
                data = data[3:]

            # Strict UTF-8 decode
            try:
                data.decode("utf-8", errors="strict")
            except UnicodeDecodeError as e:
                snippet = data[max(0, e.start - 6):e.end + 6]
                hex_ctx = " ".join(f"{b:02x}" for b in snippet)
                failures.append(
                    f"{rel}: not valid UTF-8 at byte {e.start} (context: {hex_ctx!r}). "
                    f"File is likely CP1251 / Latin-1 saved without re-encoding to UTF-8."
                )

    print(f"Scanned {scanned} text files, skipped {skipped} binary.")
    if warnings:
        print(f"\n{len(warnings)} warning(s):")
        for w in warnings:
            print(f"  WARN  {w}")
    if failures:
        print(f"\n{len(failures)} failure(s):", file=sys.stderr)
        for f in failures:
            print(f"  FAIL  {f}", file=sys.stderr)
        print(
            "\nFix by re-saving the file as UTF-8 (no BOM) — e.g. "
            "`Get-Content -Encoding UTF8 file.json | Set-Content -Encoding UTF8 out.json` "
            "or open in VSCode and 'Save with Encoding' → UTF-8.",
            file=sys.stderr,
        )
        return 1
    print("OK: all text files are valid UTF-8.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
