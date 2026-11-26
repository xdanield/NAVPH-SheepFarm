"""
Generator mapy farmy (scena Farm).

Spustenie (z korena projektu), alebo z Unity: Baa-ricade > Vygenerovat mapu:
    python Tools/mapgen/farm_map.py [--seed 12345]

Vyrobi:
    Assets/Art/Map/farm_map.json   co je na ktorom policku a kde stoja stromy/kriky (Unity podla toho stavia)
    Tools/mapgen/preview.png       nahlad mapy

Rozlozenie (velkost mapy, ohrada, dom, crafting, cesta, zombie pruhy) sa cita z
Assets/Editor/PlaceholderMapBuilder.cs, aby generator a Unity mali vzdy rovnake cisla.

Terén je vo vrstvach ako v mockupe balika Pixel Crawler: tmava trava ako podklad, cez nu svetla trava
so zubatymi okrajmi (diery = fleky tmavej travy a hlina), potok s hnedymi brehmi, navrch ozdoby a objekty.
"""

import argparse
import json
import math
import random
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Assets" / "Art"
sys.path.insert(0, str(ROOT / "Tools"))
import aseprite_io as A  # noqa: E402
from paint import paint  # noqa: E402

CS = ROOT / "Assets" / "Editor" / "PlaceholderMapBuilder.cs"
OUT_JSON = ART / "Map" / "farm_map.json"
OUT_GROUND = ART / "Map" / "farm_ground.png"
OUT_PREVIEW = Path(__file__).resolve().parent / "preview.png"

# ================================================================ NASTAVENIA
# Najcastejsie veci, ktore sa oplati ladit. Po zmene: Baa-ricade > Vygenerovat mapu
# (alebo python Tools/mapgen/farm_map.py a pozri Tools/mapgen/preview.png).
CFG = dict(
    # potok
    river_width=1.75,          # polovica sirky potoka v polickach (1.75 = cca 3-4 policka)
    river_width_var=0.7,       # o kolko sa sirka meni
    river_meander=2.6,         # ako velmi sa kluka (mimo ohrady)
    # les
    forest_spacing=1.3,        # vzdialenost stromov v lese (mensie = hustejsi les)
    forest_pines=0.75,         # podiel ihlicnanov v lese (0-1)
    meadow_pines=0.25,         # podiel ihlicnanov mimo lesa
    tall_trees=0.08,           # podiel vysokych stromov
    forest_edge_gatherable=0.3,  # kolko stromov na okraji lesa sa da zrubat
    lone_tree_spacing=9,       # osamele stromy na luke (vacsie = menej)
    # tráva, hlina
    dark_grass=0.62,           # prah pre fleky tmavej travy (vacsie = menej flekov)
    yard_wobble=1.4,           # o kolko vytrca dvor za okraj crafting plochy
    # ozdoby
    flower_meadows=0.5,        # prah pre kvetinove lucky (mensie = viac luciek)
    flower_spacing=2.5,        # vzdialenost trsov kvetov (mensie = viac trsov)
    flower_cluster=(4, 8),     # kolko kvetov ma trs
    sprouts=0.35,              # drobne travicky na luke (0-1)
    cattail_spacing=4,         # vzdialenost skupiniek palok na brehu
    bush_spacing=1.3,          # kriky (mensie = viac)
    grass_tufts=0.22,          # podiel trsov travy (zber vlakna) medzi drobnostami na luke
)
# ================================================================

N, NE, E, SE, S, SW, W, NW = 1, 2, 4, 8, 16, 32, 64, 128
DIRS = [(0, 1, N), (1, 1, NE), (1, 0, E), (1, -1, SE), (0, -1, S), (-1, -1, SW), (-1, 0, W), (-1, 1, NW)]

# Zem je jeden namalovany obrazok (farm_ground.png, sortingOrder -100). Navrch tilemapy:
# (meno, sortingOrder, kolizia, neviditelna). Water je len kolizia potoka, voda je namalovana v zemi.
GROUND_ORDER = -100
LAYERS = [("Water", -98, True, True), ("Cliff", -96, True, False), ("Decor", -90, False, False)]
# Namalovane tiene pod stromami vypnute - v Unity ich robi SpriteShadow (dynamicky od slnka a lamp).
BAKED_SHADOWS = False
SHADOW_ORDER = -85
DECOR_ORDER = -90  # kvety, travicky... pod tienmi a postavami


# ---------------------------------------------------------------- rozlozenie z C#
def read_layout():
    src = CS.read_text(encoding="utf-8")

    def num(name):
        return int(re.search(rf"\b{name}\s*=\s*(-?\d+)", src).group(1))

    def rect(name):
        m = re.search(rf"RectInt {name}\s*=\s*new RectInt\(([^)]*)\)", src)
        return tuple(int(v) for v in m.group(1).split(","))

    def vec(name):
        m = re.search(rf"Vector2Int {name}\s*=\s*new Vector2Int\(([^)]*)\)", src)
        return tuple(int(v) for v in m.group(1).split(","))

    lanes = [int(v) for v in re.search(r"ZombieLanes\s*=\s*\{([^}]*)\}", src).group(1).split(",")]
    L = dict(W=num("Width"), H=num("Height"), cliff=num("CliffRows"), forest=num("ForestDepth"),
             pen=rect("Pen"), craft=rect("CraftArea"), door=vec("HouseDoor"),
             house_w=num("HouseWidth"), house_h=num("HouseHeight"), exit_x=num("ExitX"), lanes=lanes, seed=num("Seed"))
    cx, cy, cw, ch = L["craft"]
    dx, dy = L["door"]
    ex = L["exit_x"]
    # rovnake ako PathRects v C#: od dveri dole ku craftingu, dolava a hore k vychodu
    L["paths"] = [(dx - 1, cy + ch - 1, 2, dy - (cy + ch) + 1), (ex - 1, dy - 2, dx - ex, 2), (ex - 1, dy - 2, 3, L["H"] - dy + 2)]
    return L


# ---------------------------------------------------------------- sum (noise)
def hash01(ix, iy, seed):
    h = (ix * 374761393 + iy * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0x1000000)


def vnoise(x, y, seed):
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    sx, sy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = hash01(ix, iy, seed), hash01(ix + 1, iy, seed)
    c, d = hash01(ix, iy + 1, seed), hash01(ix + 1, iy + 1, seed)
    return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy


def fbm(x, y, seed, octaves=3):
    v, amp, tot = 0.0, 1.0, 0.0
    for o in range(octaves):
        v += vnoise(x * (2 ** o), y * (2 ** o), seed + o * 101) * amp
        tot += amp
        amp *= 0.5
    return v / tot


# ---------------------------------------------------------------- generator
class Map:
    def __init__(self, L, seed):
        self.L, self.seed = L, seed
        self.W, self.H = L["W"], L["H"]
        self.rng = random.Random(seed)
        W, H = self.W, self.H
        self.kind = np.full((H, W), "grass", dtype=object)   # grass (svetla) | dark | dirt | water
        self.cliff = np.zeros((H, W), bool)
        self.forest = np.zeros((H, W), bool)
        self.layers = {name: np.full((H, W), None, dtype=object) for name, *_ in LAYERS}
        self.props, self.objects = [], []
        self.taken = []  # (x, y, r) obsadene miesta pre objekty

    # -- pomocne
    def inb(self, x, y):
        return 0 <= x < self.W and 0 <= y < self.H

    def k(self, x, y):
        return self.kind[min(max(y, 0), self.H - 1), min(max(x, 0), self.W - 1)]

    def in_rect(self, r, x, y, m=0):
        return r[0] - m <= x < r[0] + r[2] + m and r[1] - m <= y < r[1] + r[3] + m

    def on_path(self, x, y, m=0):
        return any(self.in_rect(r, x, y, m) for r in self.L["paths"])

    def in_pen(self, x, y, m=0):
        return self.in_rect(self.L["pen"], x, y, m)

    def in_craft(self, x, y, m=0):
        return self.in_rect(self.L["craft"], x, y, m)

    def in_house(self, x, y, m=0):
        dx, dy = self.L["door"]
        return abs(x + 0.5 - dx) <= self.L["house_w"] / 2 + m and dy - m <= y < dy + self.L["house_h"] + m

    def in_lane(self, y):
        return any(abs(y - l) <= 1 for l in self.L["lanes"])

    def var(self, name, x, y):
        """Varianta tile (stale rovnaka pre dane policko)."""
        return int(hash01(x, y, self.seed + len(name) * 7919) * len(TILESETS[name]))

    # -- 1. potok
    def river_center(self, x):
        # pod ohradou sa potok drzi nizsie, inde sa kluka viac (horny breh max ~2 policka pod ohradou)
        px, py, pw, ph = self.L["pen"]
        near_pen = max(0.0, 1 - max(px - 3 - x, x - (px + pw + 2), 0) / 6)
        amp = CFG["river_meander"] - 1.4 * near_pen
        c = 5.0 + amp * (fbm(x * 0.07, 3.3, self.seed + 11) - 0.5) * 2
        if near_pen > 0:
            c = min(c, py - 2.2 - self.river_half(x))
        return max(c, self.river_half(x) + 0.6)

    def river_half(self, x):
        return CFG["river_width"] + CFG["river_width_var"] * fbm(x * 0.11, 8.1, self.seed + 12)

    def gen_water(self):
        for x in range(self.W):
            c, hw = self.river_center(x), self.river_half(x)
            for y in range(self.H):
                if abs(y + 0.5 - c) < hw:
                    self.kind[y, x] = "water"
        self.river_bottom = [min([y for y in range(self.H) if self.kind[y, x] == "water"] or [0]) for x in range(self.W)]
        self.river_top = [max([y for y in range(self.H) if self.kind[y, x] == "water"] or [0]) for x in range(self.W)]

    # -- 2. utes a les
    def gen_cliff_forest(self):
        L = self.L
        for x in range(self.W):
            for y in range(self.H):
                self.cliff[y, x] = y >= self.H - L["cliff"] and abs(x - L["exit_x"]) > 1
                edge = L["forest"] + 4.0 * (fbm(y * 0.13, 1.7, self.seed + 21) - 0.5) * 2
                below = y < self.river_bottom[x]
                self.forest[y, x] = (x < edge and y > self.river_top[x]) or below and x < edge + 6

    # -- 3. hlina: dvor, cesta, fleky
    def gen_dirt(self):
        L = self.L
        cx, cy, cw, ch = L["craft"]
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "water" or self.cliff[y, x] and not self.on_path(x, y) or self.in_pen(x, y, 1):
                    continue
                wob = fbm(x * 0.35, y * 0.35, self.seed + 31)
                # dvor: obdlznik craftingu s kostrbatym okrajom (do 2 policok von)
                ddx = max(cx - x - 0.5, 0, x + 0.5 - (cx + cw))
                ddy = max(cy - y - 0.5, 0, y + 0.5 - (cy + ch))
                if math.hypot(ddx, ddy) < CFG["yard_wobble"] * wob - 0.1:
                    self.kind[y, x] = "dirt"
                # cesta: zakladna cesta z C# (vzdy priechodna) + kolisajuca sirka
                if self.on_path(x, y) or (self.on_path(x, y, 1) and wob > 0.58):
                    self.kind[y, x] = "dirt"
                # predok domu: maly dvorcek pred dverami
                dx, dy = L["door"]
                if dy - 2 <= y < dy and abs(x + 0.5 - dx) < 1.5 + 1.5 * wob:
                    self.kind[y, x] = "dirt"

    def free_meadow(self, x, y, m):
        return not (self.forest[y, x] or self.in_pen(x, y, m) or self.in_house(x, y, m) or self.in_craft(x, y, m)
                    or self.on_path(x, y, m) or self.cliff[y, x] or abs(y - self.river_center(x)) < self.river_half(x) + m
                    or y >= self.H - self.L["cliff"] - 1)

    # -- 4. tmava trava (les + fleky na luke)
    def gen_dark(self):
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] != "grass":
                    continue
                n = fbm(x * 0.17, y * 0.17, self.seed + 41)
                n2 = fbm(x * 0.4, y * 0.4, self.seed + 42)
                if self.forest[y, x] and n2 > 0.28 or (not self.in_pen(x, y) and n + 0.35 * (n2 - 0.5) > CFG["dark_grass"]):
                    self.kind[y, x] = "dark"
        # pri hline a vode vzdy svetla trava (okraje su kreslene pre svetlu travu)
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "dark" and any(self.k(x + a, y + b) in ("dirt", "water") for a, b, _ in DIRS):
                    self.kind[y, x] = "grass"

    # -- 5. upratanie: ziadne pasiky siroke 1 policko (vyzeraju skaredo)
    def cleanup(self):
        for _ in range(4):
            changed = False
            for x in range(self.W):
                for y in range(self.H):
                    t = self.kind[y, x]
                    if t == "water":
                        continue
                    n, s, e, w = self.k(x, y + 1), self.k(x, y - 1), self.k(x + 1, y), self.k(x - 1, y)
                    open_ = lambda v: v in ("dark", "dirt")
                    if t == "grass" and ((open_(n) and open_(s)) or (open_(e) and open_(w))):
                        pair = [v for v in (n, s, e, w) if open_(v)]
                        self.kind[y, x] = "dirt" if "dirt" in pair else "dark"
                        changed = True
                    elif t in ("dark", "dirt") and not self.on_path(x, y) and not self.in_craft(x, y):
                        same = sum(1 for v in (n, s, e, w) if v == t)
                        if same <= 1 and ((n == s == "grass") or (e == w == "grass") or same == 0):
                            self.kind[y, x] = "grass"
                            changed = True
            if not changed:
                break
        # znovu: tmava trava nesmie susedit s hlinou/vodou
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "dark" and any(self.k(x + a, y + b) in ("dirt", "water") for a, b, _ in DIRS):
                    self.kind[y, x] = "grass"

    # -- 6. dlazdice: kolizia potoka a utes (zem sa maluje v paint.py)
    def gen_tiles(self):
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "water":
                    self.layers["Water"][y, x] = ("water", 0)
                if self.cliff[y, x]:
                    self.layers["Cliff"][y, x] = ("cliff", x % len(TILESETS["cliff"]))

    def paint_ground(self):
        """Zem ako obrazok (riadok 0 = hore). Varianty textur podla policka, aby sa neopakovali po 16 px."""
        def var_of(name):
            n = len(TILESETS[name])
            g = np.array([[int(hash01(x, y, self.seed + len(name) * 7919) * n) for x in range(self.W)] for y in range(self.H)])
            return np.flipud(g)
        rgb, _ = paint(np.flipud(self.kind), TILESETS, self.seed, var_of)
        return Image.fromarray(rgb).convert("RGBA")

    # -- 7. ozdoby na zemi
    def gen_decor(self):
        """Drobnosti na zemi ako volne polozene sprity (nie v mriezke): kvety v trsoch, riedke travicky,
        palky v skupinkach na brehu, kamienky na hline. Kreslia sa pod postavami (DECOR_ORDER)."""
        rng = self.rng

        def add(name, frame, x, y):
            if all((x - a) ** 2 + (y - b) ** 2 >= min(c, 0.6) ** 2 for a, b, c in self.taken):  # nie pod kmenom/kamenom
                self.props.append(dict(sprite=f"Tiles/{name}", frame=frame, x=round(x * 16) / 16, y=round(y * 16) / 16,
                                       order=DECOR_ORDER, collider=False, cw=0, ch=0, flip=rng.random() < 0.5))

        def tile(x, y):
            cx, cy = int(math.floor(x)), int(math.floor(y))
            return (cx, cy) if self.inb(cx, cy) and not self.cliff[cy, cx] and not self.in_house(cx, cy, 1) else None

        def meadow(x, y, m=1):  # luka, aspon m policok od ohrady, cesty...
            t = tile(x, y)
            return (t is not None and self.kind[t[1], t[0]] != "dirt" and self.free_meadow(t[0], t[1], m)
                    and not any(self.k(t[0] + a, t[1] + b) == "water"  # 2 policka od vody
                                for a in range(-2, 3) for b in range(-2, 3)))

        def near_water(x, y):
            t = tile(x, y)
            return t is not None and self.kind[t[1], t[0]] != "water" and                 any(self.k(t[0] + a, t[1] + c) == "water" for a, c, _ in DIRS)

        flowers = TILESETS["Decor/flowers"]
        placed = []

        def flower(x, y, color):
            if meadow(x, y) and all((x - a) ** 2 + (y - b) ** 2 >= 0.45 ** 2 for a, b in placed):
                placed.append((x, y))
                add("Decor/flowers", min(len(flowers) - 1, color * 4 + rng.randrange(4)), x, y)

        # kvety: trsy jednej farby (obcas primiesana ina), niekde luka bez kvetov
        for cx, cy in self.scatter(lambda x, y: meadow(x, y) and fbm(x * 0.12, y * 0.12, self.seed + 53) > CFG["flower_meadows"],
                                   CFG["flower_spacing"], 1500):
            color = rng.randrange(4)
            for _ in range(rng.randint(*CFG["flower_cluster"])):
                c = color if rng.random() < 0.8 else rng.randrange(4)
                flower(cx + rng.gauss(0, 0.65), cy + rng.gauss(0, 0.45), c)
        # par osamelych kvetov
        for x, y in self.scatter(meadow, 6, 400):
            flower(x, y, rng.randrange(4))

        # travicky: riedko a nahodne (luka aj ohrada), obcas dve-tri pri sebe
        grassy = lambda x, y: (tile(x, y) is not None and self.kind[int(y), int(x)] not in ("dirt", "water")
                               and not self.forest[int(y), int(x)] and not near_water(x, y) and not self.on_path(int(x), int(y), 1))
        for x, y in self.scatter(grassy, 1.4, 3000):
            if rng.random() < CFG["sprouts"]:
                for _ in range(1 if rng.random() < 0.7 else rng.randint(2, 3)):
                    sx, sy = x + rng.uniform(-0.5, 0.5), y + rng.uniform(-0.4, 0.4)
                    if grassy(sx, sy):
                        add("Decor/sprouts", rng.randrange(len(TILESETS["Decor/sprouts"])), sx, sy)

        # les: hribiky, papradie, travicky
        in_forest = lambda x, y: tile(x, y) is not None and self.forest[int(y), int(x)] and not near_water(x, y)
        for x, y in self.scatter(in_forest, 1.6, 2500):
            r = rng.random()
            if r < 0.15:
                add("Decor/forest_decor", rng.randrange(len(TILESETS["Decor/forest_decor"])), x, y)
            elif r < 0.45:
                add("Decor/sprouts", rng.randrange(len(TILESETS["Decor/sprouts"])), x, y)

        # palky: par skupiniek na brehu
        for cx, cy in self.scatter(near_water, CFG["cattail_spacing"], 1500):
            if rng.random() < 0.6:
                for _ in range(rng.randint(2, 4)):
                    x, y = cx + rng.uniform(-0.8, 0.8), cy + rng.uniform(-0.3, 0.3)
                    if near_water(x, y):
                        add("Decor/cattails", rng.randrange(len(TILESETS["Decor/cattails"])), x, y)

        # kamienky na hline
        on_dirt = lambda x, y: tile(x, y) is not None and self.kind[int(y), int(x)] == "dirt"
        for x, y in self.scatter(on_dirt, 2.5, 800):
            if rng.random() < 0.3:
                add("Decor/pebbles", rng.randrange(len(TILESETS["Decor/pebbles"])), x, y)

    # -- 8. objekty: stromy, kriky, kamene, zbieratelne veci
    def blocked(self, x, y, big=True):
        L = self.L
        cx, cy = int(math.floor(x)), int(math.floor(y))
        if not self.inb(cx, cy):
            return True
        if self.kind[cy, cx] == "water" or self.cliff[cy, cx] or cy >= self.H - L["cliff"]:
            return True
        if big and any(self.k(cx + a, cy + b) == "water" for a, b, _ in DIRS):
            return True
        if self.in_pen(cx, cy, 2) or self.in_house(cx, cy, 1) or self.in_craft(cx, cy, 1) or self.on_path(cx, cy, 1):
            return True
        if self.forest[cy, cx] and self.in_lane(cy) or (big and self.kind[cy, cx] == "dirt"):
            return True
        px, py, pw, ph = L["pen"]
        if px - 5 <= cx < px and py <= cy < py + ph:   # miesto pre obranu pred ohradou
            return True
        dx, dy = L["door"]
        if abs(cx - dx) <= 2 and dy - 4 <= cy < dy:    # start hraca pred dverami
            return True
        return False

    def free(self, x, y, r):
        return all((x - a) ** 2 + (y - b) ** 2 >= (r + c) ** 2 for a, b, c in self.taken)

    def add_prop(self, sprite, frame, x, y, r, collider=None, shadow=None, flip=False):
        x, y = round(x * 16) / 16, round(y * 16) / 16
        self.taken.append((x, y, r))
        if shadow is not None and BAKED_SHADOWS:
            self.props.append(dict(sprite="Props/shadows", frame=shadow, x=x, y=y - 0.25, order=SHADOW_ORDER,
                                   collider=False, cw=0, ch=0, flip=False))
        self.props.append(dict(sprite=sprite, frame=frame, x=x, y=y, order=0, collider=collider is not None,
                               cw=collider[0] if collider else 0, ch=collider[1] if collider else 0, flip=flip))

    def add_object(self, prefab, cx, cy, r=0.7):
        self.taken.append((cx + 0.5, cy, r))
        self.objects.append(dict(prefab=prefab, x=cx, y=cy))

    def scatter(self, region, spacing, tries):
        """Rovnomerne nahodne body (Poisson disc) v oblasti region(x, y)."""
        pts = []
        for _ in range(tries):
            x, y = self.rng.uniform(0, self.W), self.rng.uniform(0, self.H)
            if region(x, y) and all((x - a) ** 2 + (y - b) ** 2 >= spacing ** 2 for a, b in pts):
                pts.append((x, y))
        return pts

    def tree(self, x, y, gatherable):
        cx, cy = int(x), int(y)
        if gatherable:
            if self.free(cx + 0.5, cy, 0.9):
                self.add_object("Tree", cx, cy, 0.9)
            return
        # v lese prevazuju ihlicnany, mimo lesa listnate
        pine_share = CFG["forest_pines"] if self.forest[min(cy, self.H - 1), min(cx, self.W - 1)] else CFG["meadow_pines"]
        kind = "trees_pine" if fbm(x * 0.12, y * 0.12, self.seed + 61) < pine_share else "trees_round"
        if hash01(cx, cy, self.seed + 62) < CFG["tall_trees"]:
            kind = "trees_tall"
        n = len(TILESETS[kind])
        frame = int(hash01(cx, cy, self.seed + 63) * n)
        if self.free(x, y, 0.75):
            self.add_prop(f"Props/{kind}", frame, x, y, 0.75, collider=(0.6, 0.4), shadow=0,
                          flip=hash01(cx, cy, 64) < 0.5)

    def gen_objects(self):
        L = self.L
        forest_edge = lambda x, y: not all(self.forest[min(max(int(y) + b, 0), self.H - 1), min(max(int(x) + a, 0), self.W - 1)]
                                           for a in (-2, -1, 0, 1, 2) for b in (-1, 0, 1))
        # les: husto, na okraji zbieratelne stromy, hlbsie dekoracne (vacsie, prekryvaju sa)
        for x, y in self.scatter(lambda x, y: self.forest[int(y), int(x)] and not self.blocked(x, y), CFG["forest_spacing"], 6000):
            self.tree(x, y, gatherable=forest_edge(x, y) and hash01(int(x), int(y), 71) < CFG["forest_edge_gatherable"])
        # pravy okraj mapy a okolie domu: lesik
        dx, dy = L["door"]
        right = lambda x, y: (x > self.W - 4.5 + 1.5 * fbm(y * 0.2, 0, self.seed + 72) or
                              (x > dx + L["house_w"] / 2 + 1 and y > dy - 1)) and not self.blocked(x, y)
        for x, y in self.scatter(right, 1.7, 1500):
            self.tree(x, y, gatherable=hash01(int(x), int(y), 73) < 0.5)
        # pod utesom sem-tam strom
        for x, y in self.scatter(lambda x, y: y > self.H - L["cliff"] - 3 and not self.blocked(x, y), 4.5, 600):
            self.tree(x, y, gatherable=True)
        # luka: par osamelych stromov
        for x, y in self.scatter(lambda x, y: self.free_meadow(int(x), int(y), 2) and not self.blocked(x, y), CFG["lone_tree_spacing"], 300):
            self.tree(x, y, gatherable=True)

        # kriky na okraji lesa, pri dome, pod utesom, za potokom
        def bush_zone(x, y):
            cx, cy = int(x), int(y)
            if self.blocked(x, y, big=False) or self.kind[cy, cx] == "dirt":
                return False
            return (self.forest[cy, cx] and forest_edge(x, y)) or y < self.river_bottom[cx] or \
                (y > self.H - L["cliff"] - 2) or (self.in_house(cx, cy, 3) and not self.in_house(cx, cy, 1))
        for x, y in self.scatter(bush_zone, CFG["bush_spacing"], 2500):
            if not self.free(x, y, 0.6) or hash01(int(x * 3), int(y * 3), 81) < 0.35:
                continue
            frame = int(hash01(int(x * 7), int(y * 7), 82) * len(TILESETS["bushes"]))
            self.add_prop("Props/bushes", frame, x, y, 0.6, collider=(0.8, 0.4), shadow=1, flip=hash01(int(x), int(y), 83) < 0.5)

        # za potokom: kamene a bobule (zbieratelne)
        for x, y in self.scatter(lambda x, y: y < self.river_bottom[int(x)] - 0.5 and not self.blocked(x, y, big=False), 2.0, 800):
            cx, cy = int(x), int(y)
            if self.free(cx + 0.5, cy, 0.7):
                self.add_object("Rock" if hash01(cx, cy, 91) < 0.55 else "BerryBush", cx, cy)
        # na kraji lesa par krikov s bobulami
        for x, y in self.scatter(lambda x, y: self.forest[int(y), int(x)] and forest_edge(x, y) and not self.blocked(x, y, big=False), 5, 600):
            if self.free(int(x) + 0.5, int(y), 0.7):
                self.add_object("BerryBush", int(x), int(y))
        # luka: trs travy (vlakno), kamene, naprstniky, drobne kamienky
        for x, y in self.scatter(lambda x, y: self.free_meadow(int(x), int(y), 1) and not self.blocked(x, y, big=False), 2.2, 2500):
            cx, cy = int(x), int(y)
            r = hash01(cx, cy, 101)
            if not self.free(x, y, 0.5):
                continue
            if r < CFG["grass_tufts"]:
                self.add_object("GrassTuft", cx, cy, 0.5)
            elif r < CFG["grass_tufts"] + 0.08:
                self.add_object("Rock", cx, cy)
            elif r < CFG["grass_tufts"] + 0.2:
                for i in range(self.rng.randint(2, 4)):  # skupinka naprstnikov
                    fx, fy = x + self.rng.uniform(-0.8, 0.8), y + self.rng.uniform(-0.5, 0.5)
                    if not self.blocked(fx, fy, big=False):
                        self.add_prop("Props/foxgloves", self.rng.randrange(3), fx, fy, 0.25)
            elif r < CFG["grass_tufts"] + 0.33:
                self.add_prop("Props/rocks_small", self.rng.randrange(len(TILESETS["rocks_small"])), x, y, 0.3)
        # les: velke kamene a pniky
        for x, y in self.scatter(lambda x, y: self.forest[int(y), int(x)] and not self.blocked(x, y), 4, 500):
            if self.free(x, y, 0.7):
                if hash01(int(x), int(y), 111) < 0.5:
                    self.add_prop("Props/rocks", self.rng.randrange(len(TILESETS["rocks"])), x, y, 0.7, collider=(0.9, 0.4), shadow=1)
                else:
                    self.add_prop("Props/stumps", self.rng.randrange(len(TILESETS["stumps"])), x, y, 0.5, collider=(0.7, 0.3))

    def generate(self):
        self.gen_water()
        self.gen_cliff_forest()
        self.gen_dirt()
        self.gen_dark()
        self.cleanup()
        self.gen_tiles()
        self.gen_objects()
        self.gen_decor()
        self.ground = self.paint_ground()


# ---------------------------------------------------------------- grafika
def load_tilesets():
    sets = {}
    for f in sorted((ART / "Tiles").rglob("*.aseprite")):
        sets[f.relative_to(ART / "Tiles").with_suffix("").as_posix()] = A.read(f)
    for f in sorted((ART / "Props").glob("*.aseprite")):
        sets[f.stem] = A.read(f)
    return sets


TILESETS = {}


def export(m):
    keys, index = [], {}
    layers = []
    for name, order, coll, hidden in LAYERS:
        grid = m.layers[name]
        cells = []
        for y in range(m.H):
            for x in range(m.W):
                v = grid[y, x]
                if v is None:
                    cells.append(-1)
                    continue
                key = f"{v[0]}:{v[1]}"
                if key not in index:
                    index[key] = len(keys)
                    keys.append(key)
                cells.append(index[key])
        layers.append(dict(name=name, order=order, collision=coll, hidden=hidden, cells=cells))
    m.ground.save(OUT_GROUND)
    data = dict(width=m.W, height=m.H, seed=m.seed, ground="Map/farm_ground", groundOrder=GROUND_ORDER,
                tiles=keys, layers=layers, objects=m.objects, props=m.props)
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")


def render(m, scale=1):
    T = 16
    img = m.ground.copy()
    for name, _, _, hidden in LAYERS:
        if hidden:
            continue
        for y in range(m.H):
            for x in range(m.W):
                v = m.layers[name][y, x]
                if v is not None:
                    img.alpha_composite(TILESETS[v[0]][v[1]], (x * T, (m.H - 1 - y) * T))
    obj_sprites = {}
    for n in ("tree", "rock", "berry_bush", "grass_tuft", "house"):
        p = ART / "Objects" / f"{n}.aseprite"
        obj_sprites[n] = A.read(p)[0] if p.exists() else None
    names = {"Tree": "tree", "Rock": "rock", "BerryBush": "berry_bush", "GrassTuft": "grass_tuft"}
    # sprity z Tiles/ maju pivot v strede (ostatne dole v strede), ako v Unity
    def prop_sprite(path):
        return TILESETS[path[len("Tiles/"):]] if path.startswith("Tiles/") else TILESETS[path.split("/")[-1]]
    draw = [(p["order"], 1 if p["sprite"].startswith("Tiles/") else 0, p["x"], p["y"], prop_sprite(p["sprite"])[p["frame"]], p["flip"])
            for p in m.props]
    for o in m.objects:
        draw.append((0, 0, o["x"] + 0.5, o["y"], obj_sprites[names[o["prefab"]]], False))
    # dom a ohrada len pre predstavu (stavia ich C#)
    dx, dy = m.L["door"]
    if obj_sprites["house"] is not None:
        draw.append((0, 0, dx, dy, obj_sprites["house"], False))
    # plot: rovnake snimky a pravidlo ako PlaceFarm/FencePost v C#
    fence = A.read(ART / "Objects" / "pen_fence.aseprite")

    def post(i, n):
        if n % 2 == 0 and i == n // 2:
            return False
        return min(i, n - 1 - i) % 2 == 0

    px, py, pw, ph = m.L["pen"]
    for x in range(px, px + pw):
        for y in range(py, py + ph):
            v, h = x in (px, px + pw - 1), y in (py, py + ph - 1)
            if v and h:
                f = 4 + (0 if x == px else 1) + (0 if y == py else 2)
            elif h:
                f = 0 if post(x - px, pw) else 2
            elif v:
                f = 1 if post(y - py, ph) else 3
            else:
                continue
            draw.append((0, 0, x + 0.5, y, fence[f], False))
    draw = [d for d in draw if d[4] is not None]
    draw.sort(key=lambda d: (d[0], -d[3]))  # ako Unity: sortingOrder, potom co je vyssie, to je vzadu
    for order, centered, x, y, spr, flip in draw:
        if order == SHADOW_ORDER and spr is None:
            continue
        s = spr.transpose(Image.FLIP_LEFT_RIGHT) if flip else spr
        top = (m.H - y) * T - (s.height / 2 if centered else s.height)
        img.alpha_composite(s, (int(round(x * T - s.width / 2)), int(round(top))))
    if scale != 1:
        img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--no-preview", action="store_true")
    args = ap.parse_args()
    L = read_layout()
    TILESETS.update(load_tilesets())
    m = Map(L, args.seed if args.seed is not None else L["seed"])
    m.generate()
    export(m)
    print(f"Mapa {m.W}x{m.H} (seed {m.seed}): {len(m.objects)} zbieratelnych objektov, {len(m.props)} dekoracii -> {OUT_JSON.relative_to(ROOT)}")
    if not args.no_preview:
        render(m).save(OUT_PREVIEW)
        print(f"Nahlad: {OUT_PREVIEW.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
