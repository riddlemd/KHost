#!/usr/bin/env python3
"""Regenerates every KHost icon asset from the SVGs beside this script: three for the console, three
for the screen app (the same tile with a monitor badge, so the two can be told apart in the Dock).

Needs rsvg-convert, and iconutil for the .icns (macOS only; skipped with a warning elsewhere).
Standard library only otherwise. The outputs are committed, so a build never needs these tools.
"""
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
WWWROOT = os.path.join(REPO, "src", "KHost.UserInterface", "wwwroot")

MASTER = os.path.join(HERE, "khost-icon.svg")
FULLBLEED = os.path.join(HERE, "khost-icon-fullbleed.svg")
SMALL = os.path.join(HERE, "khost-icon-small.svg")

SCREEN_MASTER = os.path.join(HERE, "khost-screen-icon.svg")
SCREEN_FULLBLEED = os.path.join(HERE, "khost-screen-icon-fullbleed.svg")
SCREEN_SMALL = os.path.join(HERE, "khost-screen-icon-small.svg")

# Below this the grille and the thin strokes turn to mush, so the simplified glyph is drawn instead.
SMALL_MAX = 32


def render(svg, size, out):
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), svg, "-o", out], check=True)
    with open(out, "rb") as f:
        return f.read()


def render_for(size, tmp, small=SMALL, fullbleed=FULLBLEED):
    svg = small if size <= SMALL_MAX else fullbleed
    return render(svg, size, os.path.join(tmp, f"layer-{size}.png"))


def decode_png_rgba(data):
    """Decodes the 8-bit RGBA, non-interlaced PNG rsvg-convert writes; nothing more general."""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    pos, idat, width = 8, b"", None
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, colour, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if (depth, colour, interlace) != (8, 6, 0):
                raise ValueError(f"unsupported PNG layout {depth}/{colour}/{interlace}")
        elif kind == b"IDAT":
            idat += body
        pos += 12 + length
    raw, stride, bpp = zlib.decompress(idat), width * 4, 4
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(height):
        f, line = raw[i], bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1:
                line[x] = (line[x] + a) & 0xFF
            elif f == 2:
                line[x] = (line[x] + b) & 0xFF
            elif f == 3:
                line[x] = (line[x] + ((a + b) >> 1)) & 0xFF
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
        rows.append(bytes(line))
        prev = line
    return width, height, rows


def dib_from_png(png):
    """A 32bpp BMP layer: the form pre-Vista shells and some toolkits still expect at small sizes."""
    width, height, rows = decode_png_rgba(png)
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(
        bytes(v for px in range(width) for v in (row[px * 4 + 2], row[px * 4 + 1], row[px * 4], row[px * 4 + 3]))
        for row in reversed(rows))
    # The AND mask is ignored for 32bpp but must be present; each row pads to 32 bits.
    mask = b"\x00" * (((width + 31) // 32) * 4 * height)
    return header + pixels + mask


def write_ico(path, layers):
    """layers: (size, png bytes, as_png). A 256 entry is written as 0 in the one-byte size fields."""
    blobs = [(size, png if as_png else dib_from_png(png)) for size, png, as_png in layers]
    out = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    for size, blob in blobs:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(out + b"".join(blob for _, blob in blobs))


def build_ico(path, sizes, png_from, tmp, small=SMALL, fullbleed=FULLBLEED):
    write_ico(path, [(s, render_for(s, tmp, small, fullbleed), s >= png_from) for s in sizes])


def build_icns(tmp, name="khost", master=MASTER):
    if shutil.which("iconutil") is None:
        print(f"iconutil not found; {name}.icns left as it was", file=sys.stderr)
        return
    iconset = os.path.join(tmp, f"{name}.iconset")
    os.makedirs(iconset)
    for size in (16, 32, 128, 256, 512):
        render(master, size, os.path.join(iconset, f"icon_{size}x{size}.png"))
        render(master, size * 2, os.path.join(iconset, f"icon_{size}x{size}@2x.png"))
    subprocess.run(["iconutil", "-c", "icns", iconset, "-o", os.path.join(HERE, f"{name}.icns")], check=True)


def main():
    with tempfile.TemporaryDirectory() as tmp:
        build_ico(os.path.join(HERE, "khost.ico"), (16, 24, 32, 48, 64, 128, 256), 64, tmp)
        build_icns(tmp)

        # The screen app ships only what KHost.LocalScreen links: its exe icon, Dock icon and window icon.
        build_ico(os.path.join(HERE, "khost-screen.ico"), (16, 24, 32, 48, 64, 128, 256), 64, tmp,
                  SCREEN_SMALL, SCREEN_FULLBLEED)
        build_icns(tmp, "khost-screen", SCREEN_MASTER)

        linux = os.path.join(HERE, "linux")
        os.makedirs(linux, exist_ok=True)
        for size in (16, 32, 48, 64, 128, 256, 512):
            svg = SMALL if size <= SMALL_MAX else FULLBLEED
            render(svg, size, os.path.join(linux, f"khost-{size}.png"))
        render(SCREEN_FULLBLEED, 256, os.path.join(linux, "khost-screen-256.png"))

        build_ico(os.path.join(WWWROOT, "favicon.ico"), (16, 32, 48), 64, tmp)
        shutil.copyfile(SMALL, os.path.join(WWWROOT, "favicon.svg"))
        render(FULLBLEED, 192, os.path.join(WWWROOT, "icon-192.png"))
        render(FULLBLEED, 512, os.path.join(WWWROOT, "icon-512.png"))

        # iOS masks the corners itself and shows anything transparent as black, so the plate goes square.
        with open(FULLBLEED) as f:
            square = re.sub(r'(<rect id="plate"[^>]*?) rx="\d+"', r"\1", f.read())
        square_svg = os.path.join(tmp, "square.svg")
        with open(square_svg, "w") as f:
            f.write(square)
        render(square_svg, 180, os.path.join(WWWROOT, "apple-touch-icon.png"))


if __name__ == "__main__":
    main()
