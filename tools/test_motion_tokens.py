#!/usr/bin/env python3
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
MOTION = (ROOT / "Motion.cs").read_text(encoding="utf-8")
ANIM = (ROOT / "IslandAnimator.cs").read_text(encoding="utf-8")
OVERLAY = (ROOT / "OverlayWindow.axaml.cs").read_text(encoding="utf-8")
SETTINGS = (ROOT / "SettingsWindow.axaml.cs").read_text(encoding="utf-8")
WEATHER = (ROOT / "WeatherHub.cs").read_text(encoding="utf-8")
PREFS = (ROOT / "PrefsStore.cs").read_text(encoding="utf-8")


def main() -> int:
    failed = 0
    checks = [
        ("FadeMs = 83", MOTION),
        ("FastMs = 167", MOTION),
        ("CollapseMs = 333", MOTION),
        ("ExpandMs", PREFS),
        ("CollapseMs", PREFS),
        ("SlideFromBadge", ANIM),
        ("PopChat", ANIM),
        ("towardBadge", ANIM),
        ("FromSeconds(3.2)", OVERLAY),
        ("_commitDelay", SETTINGS),
        ("WeatherKey", WEATHER),
        ("UiLanguage", PREFS),
        ("RenderMode", PREFS),
        ("FixedHostMorph", ANIM),
    ]
    if ANIM.count("ApplyNoActivate") != 1:
        print("FAIL  ApplyNoActivate should run once at morph end")
        failed += 1
    else:
        print("PASS  ApplyNoActivate once at morph end")
    if "Layoutable.WidthProperty" in ANIM:
        print("FAIL  layout Width transition still present")
        failed += 1
    else:
        print("PASS  no layout Width morph")
    for needle, src in checks:
        if needle not in src:
            print(f"FAIL  missing {needle}")
            failed += 1
        else:
            print(f"PASS  {needle}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
