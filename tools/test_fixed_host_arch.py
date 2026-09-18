#!/usr/bin/env python3
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
PLACE = (ROOT / "OverlayPlacement.cs").read_text(encoding="utf-8")
OVER = (ROOT / "OverlayWindow.axaml.cs").read_text(encoding="utf-8")
ANIM = (ROOT / "IslandAnimator.cs").read_text(encoding="utf-8")
PREFS = (ROOT / "PrefsStore.cs").read_text(encoding="utf-8")


def main() -> int:
    failed = 0
    checks = [
        ("strip helper", "ComputeStrip" in PLACE),
        ("pill left", "PillLeftDip" in PLACE),
        ("default fixed", 'RenderMode { get; set; } = "fixedHost"' in PREFS),
        ("place inside", "PlacePillInsideStrip" in OVER),
        ("settle region", "ApplyPillHitRegion" in OVER),
        ("no per-frame host", "hwndResize=False" in ANIM),
        ("easing", "Motion.SoftOut.Ease" in ANIM and "Motion.PointToPoint.Ease" in ANIM),
        ("no pill scale", "StartAnimation(\"Scale\"" not in ANIM.split("class FixedHostMorph")[-1]),
        ("width clip geom", "geom=widthClip" in ANIM),
        ("clock no ss", 'ClockFormat = "HH:mm"' in PREFS or 'fmt = "HH:mm"' in OVER),
        ("resize fallback kept", "class HwndMorph" in ANIM),
    ]
    for name, ok in checks:
        print(("PASS" if ok else "FAIL") + "  " + name)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
