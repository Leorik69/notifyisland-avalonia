#!/usr/bin/env python3
"""NumericUpDown settings rows stay wide enough for value + unit."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
AXAML = (ROOT / "SettingsWindow.axaml").read_text(encoding="utf-8")
CS = (ROOT / "SettingsWindow.axaml.cs").read_text(encoding="utf-8")


def main() -> int:
    failed = 0
    checks = [
        ("nud minwidth", AXAML, 'Property="MinWidth" Value="172"'),
        ("textbox minwidth", AXAML, 'TextBox#PART_TextBox'),
        ("repeat button cap", AXAML, "RepeatButton"),
        ("text align right", AXAML, 'TextAlignment" Value="Right"'),
        ("unit class", AXAML, 'Classes="unit"'),
        ("ms unit", AXAML, "мс"),
        ("px unit", AXAML, "px"),
        ("pct unit", AXAML, 'Text="%"'),
        ("wide columns", AXAML, "*,172,48"),
        ("no 88 col", "*,88" not in AXAML, "legacy 88px column must be gone"),
        ("chrome", "ApplyNumericChrome" in CS, "ApplyNumericChrome"),
        ("shot", "CaptureSettingsShot" in CS, "CaptureSettingsShot"),
        ("shot arg", "--settings-shot=" in (ROOT / "Program.cs").read_text(encoding="utf-8"), "cli"),
    ]
    for name, ok, detail in [
        (c[0], (c[2] in c[1]) if isinstance(c[1], str) and c[0] not in ("no 88 col", "chrome", "shot", "shot arg") else c[1], c[2] if len(c) > 2 else "")
        for c in checks
    ]:
        pass

    failed = 0
    if 'Property="MinWidth" Value="172"' not in AXAML:
        print("FAIL minwidth"); failed += 1
    else:
        print("PASS minwidth")
    if "TextBox#PART_TextBox" not in AXAML:
        print("FAIL textbox"); failed += 1
    else:
        print("PASS textbox")
    if "RepeatButton" not in AXAML:
        print("FAIL spinner cap"); failed += 1
    else:
        print("PASS spinner cap")
    if 'TextAlignment" Value="Right"' not in AXAML:
        print("FAIL align"); failed += 1
    else:
        print("PASS align")
    if AXAML.count('Classes="unit"') < 18:
        print("FAIL units", AXAML.count('Classes="unit"')); failed += 1
    else:
        print("PASS units", AXAML.count('Classes="unit"'))
    if "*,88" in AXAML or 'Width="88"' in AXAML:
        print("FAIL legacy 88"); failed += 1
    else:
        print("PASS no 88 col")
    if "ApplyNumericChrome" not in CS:
        print("FAIL chrome"); failed += 1
    else:
        print("PASS chrome")
    if "--settings-shot=" not in (ROOT / "Program.cs").read_text(encoding="utf-8"):
        print("FAIL cli"); failed += 1
    else:
        print("PASS cli")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
