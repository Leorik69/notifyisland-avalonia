#!/usr/bin/env python3
import json
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
AXAML = (ROOT / "SettingsWindow.axaml").read_text(encoding="utf-8")
CS = (ROOT / "SettingsWindow.axaml.cs").read_text(encoding="utf-8")
RU = json.loads((ROOT / "loc/ru.json").read_text(encoding="utf-8"))
EN = json.loads((ROOT / "loc/en.json").read_text(encoding="utf-8"))


def main() -> int:
    failed = 0
    leftovers = [
        'Text="Palette"',
        'Text="Opacity"',
        'Text="Glass',
        'Header="Capsule"',
        'Header="Weather"',
        'Header="Sounds"',
        'Header="Notifications"',
        'Content="Sounds on"',
        'Content="Show weather',
        'Text="Idle width"',
        'Text="Chat ms"',
    ]
    for s in leftovers:
        if s in AXAML:
            print("FAIL leftover", s)
            failed += 1
        else:
            print("PASS no", s)
    for key in ("render_mode", "whats_new_body", "opacity", "cue_vol", "display", "sys_vol_st"):
        if key not in RU or key not in EN:
            print("FAIL missing key", key)
            failed += 1
        else:
            print("PASS key", key)
    if "Режим отрисовки" not in RU["whats_new_body"] and "переключите" not in RU["whats_new_body"]:
        print("FAIL whats_new missing render flip")
        failed += 1
    else:
        print("PASS whats_new mentions render flip")
    if "RenderModeLabel.Text = Ui.T(\"render_mode\")" not in CS:
        print("FAIL apply render")
        failed += 1
    else:
        print("PASS apply render")
    if "OpacityLabel.Text" not in CS:
        print("FAIL opacity label")
        failed += 1
    else:
        print("PASS opacity label")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
