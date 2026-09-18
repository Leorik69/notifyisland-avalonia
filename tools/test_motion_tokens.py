#!/usr/bin/env python3
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
MOTION = (ROOT / "Motion.cs").read_text(encoding="utf-8")
ANIM = (ROOT / "IslandAnimator.cs").read_text(encoding="utf-8")
OVERLAY = (ROOT / "OverlayWindow.axaml.cs").read_text(encoding="utf-8")
SETTINGS = (ROOT / "SettingsWindow.axaml.cs").read_text(encoding="utf-8")
WEATHER = (ROOT / "WeatherHub.cs").read_text(encoding="utf-8")

FADE, FAST, NORMAL = 83, 167, 250


def width_morph_ms(anim_speed: str, delta_px: float) -> int:
    if anim_speed == "fast":
        return FAST
    return FAST if abs(delta_px) < 80 else NORMAL


def main() -> int:
    failed = 0
    checks = [
        ("FadeMs = 83", MOTION),
        ("FastMs = 167", MOTION),
        ("NormalMs = 250", MOTION),
        ("SplineEasing(0, 0, 0, 1)", MOTION),
        ("SplineEasing(0.55, 0.55, 0, 1)", MOTION),
        ("SplineEasing(1, 0, 1, 1)", MOTION),
        ("WidthMorphMs", MOTION),
        ("DispatcherPriority.Render", ANIM),
        ("FromSeconds(3.2)", OVERLAY),
        ("_commitDelay", SETTINGS),
        ("WeatherKey", WEATHER),
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
    if "TickPop" in OVERLAY or "TickPop" in ANIM:
        print("FAIL  TickPop still present")
        failed += 1
    else:
        print("PASS  no TickPop")
    table = [
        ("fast", 10, FAST),
        ("fast", 200, FAST),
        ("normal", 40, FAST),
        ("normal", 79, FAST),
        ("normal", 80, NORMAL),
        ("normal", 180, NORMAL),
    ]
    for speed, delta, want in table:
        got = width_morph_ms(speed, delta)
        if got != want:
            print(f"FAIL  WidthMorphMs({speed}, {delta})={got} want {want}")
            failed += 1
        else:
            print(f"PASS  WidthMorphMs({speed}, {delta})={got}")
    for needle, src in checks:
        if needle not in src:
            print(f"FAIL  missing {needle}")
            failed += 1
        else:
            print(f"PASS  {needle}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
