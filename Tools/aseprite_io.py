"""Minimalne citanie a zapis .aseprite suborov (RGBA, jedna vrstva, snimky). Pouziva import aj generator mapy."""

import struct
import zlib
from pathlib import Path

from PIL import Image

PALETTE = ["1a1c2c", "ffffff"]  # Aseprite chce paletu aj v RGBA subore; obsah nie je dolezity


def _chunk(ctype, data):
    return struct.pack("<IH", len(data) + 6, ctype) + data


def _string(s):
    b = s.encode("utf-8")
    return struct.pack("<H", len(b)) + b


def write(path, frames, tag=None, duration=100, grid=16):
    """frames: zoznam RGBA obrazkov rovnakej velkosti. Kazdy snimok = jeden sprite v Unity (Frame_0, Frame_1...)."""
    w, h = frames[0].size
    out = []
    for i, img in enumerate(frames):
        bbox = img.getbbox() or (0, 0, 1, 1)
        cel_img = img.crop(bbox)
        cel = _chunk(0x2005, struct.pack("<HhhBHh5x", 0, bbox[0], bbox[1], 255, 2, 0)
                     + struct.pack("<HH", cel_img.width, cel_img.height) + zlib.compress(cel_img.tobytes()))
        chunks = [cel]
        if i == 0:
            pal = struct.pack("<III8x", len(PALETTE), 0, len(PALETTE) - 1)
            for hexcol in PALETTE:
                r, g, b = bytes.fromhex(hexcol)
                pal += struct.pack("<HBBBB", 0, r, g, b, 255)
            layer = _chunk(0x2004, struct.pack("<HHHHHHB3x", 3, 0, 0, 0, 0, 0, 255) + _string("art"))
            tags = _chunk(0x2018, struct.pack("<H8x", 1) + struct.pack("<HHBH6x3xx", 0, len(frames) - 1, 0, 0)
                          + _string(tag or Path(path).stem))
            chunks = [_chunk(0x2019, pal), layer, tags, cel]
        body = b"".join(chunks)
        out.append(struct.pack("<IHHH2xI", len(body) + 16, 0xF1FA, len(chunks), duration, len(chunks)) + body)
    body = b"".join(out)
    header = struct.pack("<IHHHHHIHIIB3xHBBhhHH84x", 128 + len(body), 0xA5E0, len(frames), w, h, 32, 1, 100,
                         0, 0, 0, len(PALETTE), 1, 1, 0, 0, grid, grid)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(header + body)


def read(path):
    """Vrati zoznam snimkov (RGBA obrazky cez cele platno). Viac vrstiev sa zluci."""
    b = Path(path).read_bytes()
    w, h, depth = struct.unpack_from("<HHH", b, 8)
    if depth != 32:
        raise ValueError(f"{path}: podporovany je len RGBA (32 bit)")
    frames, p = [], 128
    while p < len(b):
        fs, _, old, _, _, n = struct.unpack_from("<IHHH2sI", b, p)
        q = p + 16
        img = Image.new("RGBA", (w, h))
        for _ in range(n or old):
            sz, t = struct.unpack_from("<IH", b, q)
            if t == 0x2005:
                _, x, y, _, ct = struct.unpack_from("<HhhBH", b, q + 6)
                if ct == 2:
                    cw, ch = struct.unpack_from("<HH", b, q + 22)
                    cel = Image.frombytes("RGBA", (cw, ch), zlib.decompress(b[q + 26:q + sz]))
                    img.alpha_composite(cel, (max(0, x), max(0, y)))
            q += sz
        frames.append(img)
        p += fs
    return frames


def has_art(path):
    return Path(path).exists() and any(f.getbbox() for f in read(path))


def bottom_center(images, pad=0):
    """Ulozi objekty roznej velkosti na spolocne platno: spodok na spodnom okraji, v strede (pivot v Unity)."""
    w = max(i.width for i in images) + pad * 2
    h = max(i.height for i in images) + pad
    w += w % 2
    out = []
    for i in images:
        c = Image.new("RGBA", (w, h))
        c.paste(i, ((w - i.width) // 2, h - i.height))
        out.append(c)
    return out
