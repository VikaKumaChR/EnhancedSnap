# Energy, rendering and materials

[README](../README.md) · [正體中文](ENERGY.zh-TW.md)

Enhanced Snap runs in the background all day, so it is built to cost next to nothing while you aren't snapping, and to
draw with the same compositor and materials as Windows 11.

## Energy

- **Idle = asleep.** The engine waits in `GetMessage` with no timers or polling. Window drags are observed with an
  out-of-context WinEvent hook (no DLL injection); per-drag hooks and a 33 ms modifier check exist only while a
  window is actually being dragged. For maximize-button hover, mouse moves only reset a timer; the button is
  checked once, after the pointer has rested for 300 ms.
- **Efficiency mode, small footprint.** The engine process runs with EcoQoS and opts its thread into HighQoS only
  during a drag. Popup text is drawn with Windows' software renderer, so the engine never loads the graphics driver.
  Measured on the development machine: 6.8 MB private memory at idle, about 12 MB with a snap group and its
  thumbnails.
- **Snap groups at rest cost nothing.** The base, dividers and placeholders are composition visuals; Mica comes from
  the system's blurred wallpaper, sampled once. A group's thumbnail (taskbar, Alt + Tab, Task View) is drawn once in
  the background when the group forms or changes — never on a timer — and handed to Windows instantly when asked. The
  full-screen picture a peek needs is captured only when a taskbar preview with the group opens (16 MB at
  2560 × 1600) and freed 20 seconds later.
- **The settings app is a separate process** (WinUI 3) that only runs while its window is open. Five seconds after it
  goes to the background it switches to efficiency mode and gives its memory back to Windows (measured: 93 MB in
  front → 1.5 MB private working set in the background).

## Rendering

Zones are `Windows.UI.Composition` visuals composed by DWM on the GPU; fades and the gliding drop target are
compositor animations, so they cost no CPU in Enhanced Snap. Overlay windows are created the first time they are
needed.

## Materials

Materials follow Fluent 2: transient zones use **Acrylic** (host-backdrop blur and tint), and the settings window uses
**Mica**. Acrylic falls back to a solid color in **Energy saver**, with transparency effects off, in high contrast and
in Remote Desktop; animations stop in Energy saver or when Windows animation effects are off. You can also choose
*Solid* and *No animations* yourself.

How this is built: [Design → Energy & performance](DESIGN.md#5-energy--performance).
