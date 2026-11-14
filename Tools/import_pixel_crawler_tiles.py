"""
Nacita grafiku z free balika Pixel Crawler (Anokolisa) do Assets/Art.

Spustenie (z korena projektu):
    python Tools/import_pixel_crawler_tiles.py ["<cesta k Pixel Crawler - Free Pack>"] [--force]

Co vyrobi:
    Tiles/*.aseprite          dlazdice (kazdy snimok = varianta)
    Tiles/Decor/*.aseprite    ozdoby na zem (kvety, travicky...)
    Props/*.aseprite          stromy, kriky, kamene, tiene... (dekoracie, rozmiestni ich generator mapy)
    Objects/tree|rock|berry_bush|grass_tuft.aseprite   zbieratelne veci (len ak este nie su nakreslene)

Rucne kreslene subory neprepisuje (okrem --force). Props su generovane, tie sa prepisu vzdy.
Okraje travy, hliny a brehy potoka sa nemaju dlazdice: generator mapy ich maluje (Tools/mapgen/paint.py).
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import aseprite_io as A  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "Assets" / "Art"
DEFAULT_PACK = Path.home() / "Downloads" / "Pixel Crawler - Free Pack (Exterior 0.1)" / "Pixel Crawler - Free Pack"

TS = "Environment/Tilesets/"
PROPS = "Environment/Props/Static/"
VEG = PROPS + "Vegetation.png"

# subor v Assets/Art/Tiles -> (obrazok v baliku, [(stlpec, riadok)]) ; 1 dlazdica = 16x16 px
TILES = {
    "grass": (TS + "Floors_Tiles.png", [(1, 10), (2, 10), (3, 10)]),        # svetla trava
    "grass_dark": (TS + "Floors_Tiles.png", [(1, 11), (2, 11), (3, 11)]),   # tmava trava (podklad, les)
    "dirt": (TS + "Floors_Tiles.png", [(11, 10), (12, 10), (13, 10)]),      # hlina (cesta, dvor)
    "water": (TS + "Water_tiles.png", [(5, 0), (5, 1), (5, 2), (5, 3), (5, 4)]),
    "water_deep": (TS + "Water_tiles.png", [(2, 7), (7, 7), (2, 12), (7, 12)]),
    "cliff": (TS + "Wall_Tiles.png", [(x, 16) for x in range(6)]),          # ide za sebou, nie nahodne
    "Decor/flowers": (VEG, [(x, y) for y in (23, 24, 25, 26) for x in (3, 4, 6, 7)]),  # oranz., biele, modre, zlte
    "Decor/sprouts": (VEG, [(1, 9), (2, 9), (3, 9), (2, 10), (3, 10), (4, 11), (5, 11), (2, 11)]),
    "Decor/forest_decor": (VEG, [(1, 21), (2, 21), (0, 22), (1, 22), (5, 18), (6, 18), (7, 19), (4, 10)]),
    "Decor/cattails": (VEG, [(10, 11), (11, 11), (5, 11), (6, 11)]),
    "Decor/pebbles": (VEG, [(14, 25), (14, 26), (15, 26)]),
}

# Props: subor -> [(obrazok v baliku, (x0, y0, x1, y1) v pixeloch)]
PROP_SETS = {
    "trees_round": [(PROPS + "Trees/Model_01/Size_03.png", (0, 0, 48, 96)),
                    (PROPS + "Trees/Model_01/Size_03.png", (48, 0, 96, 96)),
                    (PROPS + "Trees/Model_01/Size_04.png", (3, 2, 76, 128)),
                    (PROPS + "Trees/Model_01/Size_04.png", (83, 2, 156, 128))],
    "trees_pine": [(PROPS + "Trees/Model_02/Size_03.png", (4, 84, 41, 160)),
                   (PROPS + "Trees/Model_02/Size_03.png", (52, 84, 89, 160)),
                   (PROPS + "Trees/Model_02/Size_03.png", (4, 4, 41, 80)),
                   (PROPS + "Trees/Model_02/Size_04.png", (4, 121, 58, 224)),
                   (PROPS + "Trees/Model_02/Size_04.png", (68, 121, 122, 224)),
                   (PROPS + "Trees/Model_02/Size_04.png", (4, 9, 58, 112))],
    "trees_tall": [(PROPS + "Trees/Model_03/Size_03.png", (0, 1, 63, 144)),
                   (PROPS + "Trees/Model_03/Size_03.png", (64, 1, 127, 144))],
    "bushes": [(VEG, (198, 6, 233, 39)), (VEG, (246, 6, 281, 39)), (VEG, (194, 51, 222, 77)),
               (VEG, (2, 99, 45, 142)), (VEG, (50, 99, 93, 142)), (VEG, (194, 86, 233, 119)),
               (VEG, (242, 86, 281, 119))],
    "rocks": [(PROPS + "Rocks.png", (2, 19, 30, 62)), (PROPS + "Rocks.png", (35, 19, 61, 46)),
              (PROPS + "Rocks.png", (98, 19, 126, 62)), (PROPS + "Rocks.png", (131, 19, 157, 46))],
    "rocks_small": [(PROPS + "Rocks.png", b) for b in
                    [(65, 19, 79, 30), (80, 17, 96, 31), (161, 19, 175, 30), (176, 17, 192, 31),
                     (48, 51, 63, 61), (144, 51, 159, 61)]],
    "foxgloves": [(VEG, (192, 160, 208, 192)), (VEG, (208, 160, 224, 192)), (VEG, (224, 160, 240, 192))],
    "stumps": [(PROPS + "Trees/Model_01/Size_04.png", b) for b in
               [(328, 9, 356, 32), (328, 41, 356, 64), (328, 73, 356, 96)]],
}

# ---------------------------------------------------------------- zbieratelne veci
def gameplay_sprites(pack):
    m01_02 = Image.open(pack / PROPS / "Trees/Model_01/Size_02.png").convert("RGBA")
    m01_03 = Image.open(pack / PROPS / "Trees/Model_01/Size_03.png").convert("RGBA")
    rocks = Image.open(pack / PROPS / "Rocks.png").convert("RGBA")
    veg = Image.open(pack / VEG).convert("RGBA")
    tree, stump = m01_02.crop((16, 2, 64, 64)), m01_03.crop((193, 74, 207, 96))
    bush = veg.crop((194, 51, 222, 77))
    berries = bush.copy()
    d = ImageDraw.Draw(berries)
    for x, y in [(7, 8), (13, 5), (19, 9), (10, 14), (17, 15), (22, 13), (5, 15)]:
        d.rectangle((x, y, x + 1, y + 1), fill=(200, 40, 50, 255))
        d.point((x, y), fill=(255, 120, 120, 255))
    return {
        "tree": A.bottom_center([tree, stump]),
        "rock": A.bottom_center([rocks.crop((35, 19, 61, 46))]),
        "berry_bush": A.bottom_center([berries, bush]),
        "grass_tuft": A.bottom_center([veg.crop((112, 176, 128, 192))]),
    }


def main():
    args = [a for a in sys.argv[1:] if a != "--force"]
    force = "--force" in sys.argv
    pack = Path(args[0]) if args else DEFAULT_PACK
    if not (pack / TS).exists():
        raise SystemExit(f"Balik som nenasla: {pack}")

    for name, (sheet, cells) in TILES.items():
        dst = ART / "Tiles" / f"{name}.aseprite"
        if A.has_art(dst) and not force:
            print(f"preskakujem (uz ma kresbu): Tiles/{name}")
            continue
        im = Image.open(pack / sheet).convert("RGBA")
        A.write(dst, [im.crop((x * 16, y * 16, x * 16 + 16, y * 16 + 16)) for x, y in cells])
        print(f"Tiles/{name}: {len(cells)} variant")

    for name, parts in PROP_SETS.items():
        imgs = [Image.open(pack / sheet).convert("RGBA").crop(box) for sheet, box in parts]
        A.write(ART / "Props" / f"{name}.aseprite", A.bottom_center(imgs), grid=16)
        print(f"Props/{name}: {len(imgs)}")

    # tien pod stromy/kriky: farba z balika (Shadows.png), tvar elipsa
    sh = Image.open(pack / PROPS / "Shadows.png").convert("RGBA")
    color = max(sh.getcolors(1 << 16) or [(0, (0, 0, 0, 90))], key=lambda c: c[0] if c[1][3] else -1)[1]
    shadows = []
    for w, h in [(40, 14), (28, 10), (18, 7)]:
        s = Image.new("RGBA", (w, h))
        ImageDraw.Draw(s).ellipse((0, 0, w - 1, h - 1), fill=color)
        shadows.append(s)
    A.write(ART / "Props" / "shadows.aseprite", A.bottom_center(shadows))
    print("Props/shadows: 3")

    for name, frames in gameplay_sprites(pack).items():
        dst = ART / "Objects" / f"{name}.aseprite"
        if A.has_art(dst) and not force:
            print(f"preskakujem (uz ma kresbu): Objects/{name}")
            continue
        A.write(dst, frames)
        print(f"Objects/{name}: {len(frames)} snimky")


if __name__ == "__main__":
    main()
