#!/usr/bin/env python3
from __future__ import annotations
import math, re, sys
from pathlib import Path
ISLAND = Path(__file__).resolve().parents[1]
CS = (ISLAND / "NotifyIsland.Core" / "OverlayMachine.cs").read_text(encoding="utf-8")
TOKENS = (ISLAND / "NotifyIsland.Core" / "OverlayTokens.cs").read_text(encoding="utf-8")
KINDS = ("Idle","Collapsed","Expanded","Notification","Progress","Media","Timer","Error","Weather")

class Payload:
    def __init__(self, title="", subtitle="", body="", progress=0.0, playing=False, remaining=0.0,
                 temp=None, code=None, precip=None):
        self.title, self.subtitle, self.body = title, subtitle, body
        self.progress, self.playing, self.remaining = progress, playing, remaining
        self.temp, self.code, self.precip = temp, code, precip

def sanitize(p: Payload) -> Payload:
    t, s, b = (p.title or "").strip(), (p.subtitle or "").strip(), (p.body or "").strip()
    if len(t) > 80: t = t[:77] + "\u2026"
    if len(s) > 80: s = s[:77] + "\u2026"
    if len(b) > 160: b = b[:157] + "\u2026"
    prog = p.progress
    if isinstance(prog, float) and (math.isnan(prog) or math.isinf(prog)): prog = 0.0
    prog = min(1.0, max(0.0, float(prog)))
    try: rem = float(p.remaining)
    except (TypeError, ValueError): rem = 0.0
    if math.isnan(rem) or math.isinf(rem) or rem < 0: rem = 0.0
    return Payload(t, s, b, prog, p.playing, rem, p.temp, p.code, p.precip)

class Machine:
    def __init__(self):
        self.kind = "Idle"; self.return_to = "Idle"; self.notify_ms = 0; self.notify_dur = 4000
        self.payload = Payload(); self.unread = 0; self.weather_enabled = True
    def dispatch(self, cmd: str, incoming: Payload | None = None) -> None:
        data = sanitize(incoming or Payload())
        if cmd == "Collapse":
            self.kind = "Collapsed"; self.notify_ms = 0
        elif cmd == "Expand":
            self.kind = "Expanded"; self.payload = data; self.notify_ms = 0
        elif cmd == "Notify":
            if self.kind != "Notification":
                self.return_to = "Idle" if self.kind == "Collapsed" else self.kind
            self.kind = "Notification"; self.payload = data
            if not self.payload.title: self.payload.title = "Уведомление"
            self.notify_ms = self.notify_dur
            self.unread = min(self.unread + 1, 99)
        elif cmd == "SetProgress":
            self.kind = "Progress"; self.payload = data; self.notify_ms = 0
        elif cmd == "SetMedia":
            self.kind = "Media"; self.payload = data
            if not self.payload.title: self.payload.title = "Без названия"
            if not self.payload.subtitle: self.payload.subtitle = "Неизвестный исполнитель"
            self.notify_ms = 0
        elif cmd == "SetTimer":
            self.kind = "Timer"; self.payload = data; self.notify_ms = 0
        elif cmd == "SetError":
            self.kind = "Error"; self.payload = data
            if not self.payload.body and not self.payload.title: self.payload.title = "Ошибка"
            self.notify_ms = 0
        elif cmd == "SetWeather":
            self.kind = "Weather"; self.payload = data; self.notify_ms = 0
        elif cmd == "Clear":
            self.kind = "Idle"; self.return_to = "Idle"; self.notify_ms = 0
            self.unread = 0; self.payload = Payload()
    def tick(self, ms: int) -> None:
        dt = max(0, ms)
        if self.kind == "Notification":
            self.notify_ms -= dt
            if self.notify_ms <= 0:
                self.notify_ms = 0
                self.kind = "Idle" if self.return_to == "Notification" else self.return_to
        if self.kind == "Timer" and self.payload.remaining > 0:
            self.payload.remaining = max(0.0, self.payload.remaining - dt / 1000.0)

def _token_int(name: str) -> int:
    m = re.search(rf"{name}\s*=\s*(\d+)", TOKENS)
    assert m, name
    return int(m.group(1))

def _token_float(name: str) -> float:
    m = re.search(rf"{name}\s*=\s*([\d.]+)", TOKENS)
    assert m, name
    return float(m.group(1))

def test_source_has_kinds() -> None:
    for k in KINDS:
        assert f"    {k}" in CS or f"{k}," in CS, k
    assert "Sanitize" in CS and "DemoNext" in CS
    assert "UnreadCount" in CS
    assert "SetWeather" in CS and "CycleNext" in CS
    assert 'FillHex = "#080808"' in TOKENS and 'AccentHex = "#3D9CF0"' in TOKENS
    morph = _token_int("MorphMs")
    assert 260 <= morph <= 320, morph
    assert _token_int("SwipeFirePx") == 48
    assert _token_int("SwipeClickMaxPx") == 12
    assert _token_int("SwipeRubberMs") == 180
    assert "new WUC.Compositor()" not in CS
    assert "HeightFor(OverlayKind kind) => OverlayTokens.CollapsedH" in CS or "=> OverlayTokens.CollapsedH" in CS
    # No Open-Meteo
    assert "open-meteo" not in CS.lower()
    assert "OpenMeteo" not in CS

def test_transitions() -> None:
    m = Machine(); m.dispatch("Expand", Payload(title="Hi")); assert m.kind == "Expanded"
    m.dispatch("Notify", Payload(title="Ping")); assert m.kind == "Notification"
    m.tick(4000); assert m.kind == "Expanded"
    m.dispatch("Clear"); assert m.kind == "Idle"
    m.dispatch("SetProgress", Payload(progress=0.5)); assert m.kind == "Progress"
    m.dispatch("SetMedia", Payload()); assert m.payload.title == "Без названия"
    m.dispatch("SetTimer", Payload(remaining=10)); m.tick(2500); assert abs(m.payload.remaining - 7.5) < 0.05
    m.dispatch("SetError", Payload()); assert m.payload.title == "Ошибка"
    m.dispatch("SetWeather", Payload(title="Ясно", temp=18, code=0)); assert m.kind == "Weather"
    m.dispatch("Collapse"); assert m.kind == "Collapsed"

def test_invalid_data() -> None:
    bad = sanitize(Payload(title="x"*200, progress=float("nan"), remaining=-9))
    assert len(bad.title) <= 80 and bad.progress == 0.0 and bad.remaining == 0.0
    hi = sanitize(Payload(progress=4.2, remaining=0)); assert hi.progress == 1.0
    inf = sanitize(Payload(remaining=float("inf"))); assert inf.remaining == 0.0

def test_notify_timer_auto_return() -> None:
    m = Machine(); m.notify_dur = 1000
    m.dispatch("SetMedia", Payload(title="A", subtitle="B"))
    m.dispatch("Notify", Payload(title="N")); m.tick(500); assert m.kind == "Notification"
    m.tick(500); assert m.kind == "Media"

def test_unread_count() -> None:
    m = Machine()
    assert m.unread == 0
    m.dispatch("Notify", Payload(title="A")); assert m.unread == 1
    m.tick(4000); assert m.unread == 1
    m.dispatch("Notify", Payload(title="B")); assert m.unread == 2
    m.dispatch("Collapse"); assert m.kind == "Collapsed" and m.unread == 2
    m.dispatch("SetWeather", Payload(title="Ясно")); assert m.unread == 2
    m.dispatch("Clear"); assert m.unread == 0 and m.kind == "Idle"

def test_fixed_height_tokens() -> None:
    h = _token_float("CollapsedH")
    w = _token_float("CollapsedW")
    assert h <= 30, h
    assert w <= 160, w
    assert "ExpandedMinH" not in TOKENS
    assert "ExpandedMaxH" not in TOKENS
    assert _token_float("CollapsedWeatherW") >= w

def test_no_new_compositor_in_overlay() -> None:
    assert not re.search(r"new\s+(WUC\.)?Compositor\(\)", CS)

def test_no_open_meteo_in_repo() -> None:
    """Docs may mention Open-Meteo as forbidden; code must not call it."""
    root = ISLAND
    for p in root.rglob("*.cs"):
        if "bin" in p.parts or "obj" in p.parts: continue
        text = p.read_text(encoding="utf-8", errors="ignore")
        low = text.lower()
        assert "api.open-meteo.com" not in low, p
        assert "open-meteo.com" not in low, p
        # Ban HttpClient weather fetch patterns tied to third-party weather hosts
        if "httprequest" in low or "httpclient" in low:
            assert "meteo" not in low, p

def main() -> int:
    failed = 0
    for fn in (test_source_has_kinds, test_transitions, test_invalid_data,
               test_notify_timer_auto_return, test_unread_count, test_fixed_height_tokens,
               test_no_new_compositor_in_overlay, test_no_open_meteo_in_repo):
        try:
            fn(); print(f"PASS  {fn.__name__}")
        except AssertionError as exc:
            failed += 1; print(f"FAIL  {fn.__name__}: {exc}")
    return 1 if failed else 0

if __name__ == "__main__":
    sys.exit(main())
