#!/usr/bin/env python3
"""P1–P3 source contracts. Fail first, then keep green after implementation."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def main() -> int:
    failed = 0
    checks = [
        ("OverlayKind.TimerComplete", "OverlayMachine.cs", "TimerComplete"),
        ("SHQueryUserNotificationState", "QuietHours.cs", "SHQueryUserNotificationState"),
        ("ScreenIndex pref", "PrefsStore.cs", "ScreenIndex"),
        ("SettingsSchema", "PrefsStore.cs", "SettingsSchema"),
        ("ReduceMotion", "PrefsStore.cs", "ReduceMotion"),
        ("SuppressFocusAssist", "PrefsStore.cs", "SuppressFocusAssist"),
        ("GetLastWin32Error", "Win32Overlay.cs", "GetLastWin32Error"),
        ("IslandLog rotate", "Core/IslandLog.cs", "Rotate"),
        ("resx Strings", "loc/Strings.resx", "<data name=\"title\""),
        ("resx ru", "loc/Strings.ru.resx", "<data name=\"title\""),
        ("Ui ResourceManager", "Ui.cs", "ResourceManager"),
        ("Automation overlay", "OverlayWindow.axaml", "AutomationProperties.Name"),
        ("Cancel progress", "OverlayWindow.axaml", "CancelProgress"),
        ("InvokeAction", "Core/OverlayDispatcher.cs", "InvokeAction"),
        ("Urgency", "Core/NotificationModel.cs", "NotifyUrgency"),
        ("platform doc", "docs/platform-limitations.md", "Windows"),
        ("version 1.3.0", "NotifyIsland.Av.csproj", "<Version>1.3.0</Version>"),
        ("screen picker", "SettingsWindow.axaml", "ScreenBox"),
        ("diagnostics", "Program.cs", "--diagnostics"),
        ("CJK demo", "Demo/DemoScript.cs", "こんにちは"),
    ]
    for name, rel, needle in checks:
        path = ROOT / rel
        if not path.exists():
            print(f"FAIL  {name}: missing {rel}")
            failed += 1
            continue
        text = path.read_text(encoding="utf-8")
        if needle not in text:
            print(f"FAIL  {name}: {needle!r} not in {rel}")
            failed += 1
        else:
            print(f"PASS  {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
