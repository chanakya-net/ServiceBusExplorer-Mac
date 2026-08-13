#!/usr/bin/env python3
"""
Generates AppIcon.icns without any third-party imaging library.

Writes a set of PNGs with the stdlib (zlib + struct), then lets macOS's own
iconutil pack them into an .icns. The artwork is a rounded-square tile with three
stacked "message" bars, echoing a queue.
"""
import math, os, shutil, struct, subprocess, sys, zlib

def lerp(a, b, t): return a + (b - a) * t

TOP    = (0x1B, 0x6E, 0xC7)   # azure blue
BOTTOM = (0x0D, 0x3F, 0x7A)   # deeper blue
BAR    = (0xFF, 0xFF, 0xFF)

def rounded_alpha(x, y, size, radius, feather=1.0):
    """Coverage of a rounded square at a pixel centre, antialiased."""
    cx = min(max(x, radius), size - radius)
    cy = min(max(y, radius), size - radius)
    d = math.hypot(x - cx, y - cy)
    return max(0.0, min(1.0, (radius - d) / feather + 0.5))

def bar_alpha(x, y, rects, feather=1.0):
    """Coverage of the union of rounded bars."""
    best = 0.0
    for (x0, y0, x1, y1, r) in rects:
        cx = min(max(x, x0 + r), x1 - r)
        cy = min(max(y, y0 + r), y1 - r)
        d = math.hypot(x - cx, y - cy)
        best = max(best, max(0.0, min(1.0, (r - d) / feather + 0.5)))
    return best

def render(size):
    radius = size * 0.2237                      # matches the macOS squircle closely enough
    margin = size * 0.20
    width  = size - 2 * margin
    bar_h  = size * 0.086
    gap    = size * 0.072
    total  = 3 * bar_h + 2 * gap
    top    = (size - total) / 2
    br     = bar_h / 2

    bars = []
    for i in range(3):
        y0 = top + i * (bar_h + gap)
        # Each bar is a little shorter than the one above, so the tile reads as a list.
        w = width * (1.0 - 0.16 * i)
        bars.append((margin, y0, margin + w, y0 + bar_h, br))

    rows = bytearray()
    for py in range(size):
        rows.append(0)                          # PNG filter type 0 for this scanline
        y = py + 0.5
        t = py / max(1, size - 1)
        r = int(lerp(TOP[0], BOTTOM[0], t))
        g = int(lerp(TOP[1], BOTTOM[1], t))
        b = int(lerp(TOP[2], BOTTOM[2], t))
        for px in range(size):
            x = px + 0.5
            outer = rounded_alpha(x, y, size, radius)
            if outer <= 0.0:
                rows += bytes((0, 0, 0, 0))
                continue
            bar = bar_alpha(x, y, bars)
            cr = int(lerp(r, BAR[0], bar))
            cg = int(lerp(g, BAR[1], bar))
            cb = int(lerp(b, BAR[2], bar))
            rows += bytes((cr, cg, cb, int(outer * 255)))
    return bytes(rows)

def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

def write_png(path, size):
    raw = render(size)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(png)

def main():
    out = os.path.dirname(os.path.abspath(__file__))
    iconset = os.path.join(out, "AppIcon.iconset")
    shutil.rmtree(iconset, ignore_errors=True)
    os.makedirs(iconset)

    # The names are fixed by iconutil; each pair is (points, scale).
    for points in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            size = points * scale
            suffix = "" if scale == 1 else "@2x"
            write_png(os.path.join(iconset, f"icon_{points}x{points}{suffix}.png"), size)

    subprocess.run(["iconutil", "-c", "icns", iconset,
                    "-o", os.path.join(out, "AppIcon.icns")], check=True)
    shutil.rmtree(iconset, ignore_errors=True)
    print("Wrote", os.path.join(out, "AppIcon.icns"))

if __name__ == "__main__":
    main()
