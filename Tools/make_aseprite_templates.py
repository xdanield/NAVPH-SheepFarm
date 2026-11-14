"""
Vytvori prazdne .aseprite sablony so spravnou velkostou, snimkami a tagmi.

Spustenie (z korena projektu):
    python Tools/make_aseprite_templates.py

Existujuce subory NEPREPISUJE, takze sa da spustit kedykolvek znova
(napr. ked sem pridame novy objekt) bez rizika, ze prides o kresbu.
"""

import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "Assets" / "Art"
GRID = 16  # velkost tile v pixeloch (1 tile = 16 px = 1 Unity unit)

# Paleta pre zaciatok (da sa v Aseprite lubovolne menit).
PALETTE = [
    "1a1c2c",  # obrys
    "4a4a55", "7c7c88", "b8b8c0", "e4e4e4", "ffffff",  # sede, vlna
    "2b1b12", "3b2a2a", "5d3a24", "8b5a2b", "c08552",  # hnede, drevo
    "a0784a", "d9b25c",  # hlina, slama
    "7a4b35", "c68863", "e8c39e",  # vlasy, pokozka
    "e07b1f", "f2a65a",  # oranzova kosela
    "2c3e66", "3f5a99",  # rifle
    "1e3d23", "2f5d34", "4f8a3c", "7bb54a", "b5d96b",  # zelene, trava, les
    "3d4a2a", "5c6b3a", "8a9a5b",  # zombie
    "7a1f1f", "c0392b", "ff5a4a",  # krv, oci
    "2d5f8a", "4a90c2", "9fd3f0",  # voda
]

# Trvanie snimok v ms podla typu animacie
DUR = {"idle": 400, "walk": 120, "attack": 90}


def anim(*specs):
    """specs: ("walk", 4) -> tagy walk_down, walk_up, walk_side po 4 snimky."""
    tags = []
    for kind, count in specs:
        for d in ("down", "up", "side"):
            tags.append((f"{kind}_{d}", count, DUR[kind]))
    return tags


def still(*names, duration=100):
    return [(n, 1, duration) for n in names]


# (cesta, sirka, vyska, [(tag, pocet snimok, ms)])
FILES = [
    # Postavy: platno 32x32 (2x2 tiles), nohy na spodnom okraji v strede.
    # Bocne snimky kresli otocene DOPRAVA, dolava sa zrkadli v kode.
    ("Characters/player.aseprite", 32, 32, anim(("idle", 2), ("walk", 4), ("attack", 3))),
    ("Characters/zombie.aseprite", 32, 32, anim(("idle", 2), ("walk", 4), ("attack", 3))),
    ("Characters/sheep.aseprite", 32, 32, anim(("idle", 2), ("walk", 4))),
    ("Characters/zombie_sheep.aseprite", 32, 32, anim(("idle", 2), ("walk", 4), ("attack", 3))),

    # Objekty: 16x16 = 1 tile, pata objektu na spodnom okraji.
    ("Objects/tree.aseprite", 32, 48, still("tree", "stump")),
    ("Objects/rock.aseprite", 16, 16, still("rock")),
    ("Objects/grass_tuft.aseprite", 16, 16, still("grass")),
    ("Objects/berry_bush.aseprite", 16, 16, still("full", "empty")),
    ("Objects/fence.aseprite", 16, 16, still("horizontal", "vertical")),
    ("Objects/pen_fence.aseprite", 16, 16, still("horizontal", "vertical", "corner")),
    ("Objects/spikes.aseprite", 16, 16, still("spikes")),
    ("Objects/chest.aseprite", 16, 16, still("closed", "open")),
    ("Objects/house.aseprite", 128, 112, still("house")),            # 8x7 tiles, dvere dole v strede
    ("Objects/crafting_table.aseprite", 32, 32, still("table")),
    ("Objects/cooking_pot.aseprite", 16, 24, still("idle", "cooking")),

    # Tiles: presne 16x16, musia na seba nadvazovat (View > Tiled Mode).
    ("Tiles/grass.aseprite", 16, 16, still("grass")),
    ("Tiles/forest_floor.aseprite", 16, 16, still("forest_floor")),
    ("Tiles/dirt.aseprite", 16, 16, still("dirt")),
    ("Tiles/path.aseprite", 16, 16, still("path")),
    ("Tiles/water.aseprite", 16, 16, still("water")),
    ("Tiles/cliff.aseprite", 16, 16, still("cliff")),  # okraj kopca hore

    # Pozadie nad mapou (vyhlad z kopca). 1024 px = sirka mapy (64 tiles). Kresli od spodneho okraja,
    # hore nechaj priehladne. Vrstvy sa pri pohybe kamery posuvaju roznou rychlostou.
    ("Background/sky.aseprite", 1024, 176, still("sky")),             # obloha + zapadajuce slnko
    ("Background/mountains.aseprite", 1024, 112, still("mountains")), # hory, vacsie vlavo
    ("Background/city.aseprite", 1024, 80, still("city")),            # mesto vlavo + kopce
]


def string(s):
    b = s.encode("utf-8")
    return struct.pack("<H", len(b)) + b


def chunk(ctype, data):
    return struct.pack("<IH", len(data) + 6, ctype) + data


def layer_chunk(name):
    # flags: 1 viditelna, 2 editovatelna; typ 0 = normalna vrstva
    return chunk(0x2004, struct.pack("<HHHHHHB3x", 3, 0, 0, 0, 0, 0, 255) + string(name))


def tags_chunk(tags):
    data = struct.pack("<H8x", len(tags))
    frame = 0
    for name, count, _ in tags:
        data += struct.pack("<HHBH6x3xx", frame, frame + count - 1, 0, 0) + string(name)
        frame += count
    return chunk(0x2018, data)


def palette_chunk():
    data = struct.pack("<III8x", len(PALETTE), 0, len(PALETTE) - 1)
    for hexcol in PALETTE:
        r, g, b = bytes.fromhex(hexcol)
        data += struct.pack("<HBBBB", 0, r, g, b, 255)
    return chunk(0x2019, data)


def frame(chunks, duration):
    body = b"".join(chunks)
    n = len(chunks)
    return struct.pack("<IHHH2xI", len(body) + 16, 0xF1FA, min(n, 0xFFFF), duration, n) + body


def build(width, height, tags):
    durations = [ms for _, count, ms in tags for _ in range(count)]
    frames = []
    for i, ms in enumerate(durations):
        chunks = []
        if i == 0:
            chunks = [palette_chunk(), layer_chunk("art"), tags_chunk(tags)]
        frames.append(frame(chunks, ms))
    body = b"".join(frames)

    header = struct.pack(
        "<IHHHHHIHIIB3xHBBhhHH84x",
        128 + len(body),  # velkost suboru
        0xA5E0,           # magic
        len(frames),
        width, height,
        32,               # RGBA
        1,                # flags: opacity vrstiev je platna
        100,              # stara rychlost (nepouziva sa)
        0, 0,
        0,                # transparentny index palety
        len(PALETTE),
        1, 1,             # pomer stran pixelu
        0, 0, GRID, GRID, # mriezka = 1 tile
    )
    assert len(header) == 128
    return header + body


def main():
    for rel, w, h, tags in FILES:
        path = ART / rel
        if path.exists():
            print(f"preskakujem (uz existuje): {rel}")
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(build(w, h, tags))
        n = sum(c for _, c, _ in tags)
        print(f"vytvorene: {rel}  {w}x{h}, {n} snimok, tagy: {', '.join(t for t, _, _ in tags)}")


if __name__ == "__main__":
    main()
