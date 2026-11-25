r"""
Generator lokacie Explore ("The Wilds") - vacsia oblast nad farmou, kam vedie cesta cez utes.
Nie je to open world: je to jedna uzavreta mapa (husty les a utes okolo), ale vacsia ako farma
a s viacerymi castami, kde sa zbiera nieco ine.

Spustenie (z korena projektu), alebo z Unity: Baa-ricade > Vygenerovat Explore lokaciu:
    python Tools/mapgen/explore_map.py [--seed 777]

Vyrobi:
    Assets/Art/Map/explore_map.json    co je na ktorom policku a kde stoja stromy/kamene (Unity podla toho stavia)
    Assets/Art/Map/explore_ground.png  namalovana zem
    Tools/mapgen/explore_preview.png   nahlad

Rozmery a vstup sa citaju z Assets/Editor/PlaceholderMapBuilder.Explore.cs.
Grafika, noise a malovanie zeme su spolocne s farm_map.py (rovnake dlazdice a pravidla okrajov).

Rozlozenie (pohlad zhora, cesta z farmy prichadza zdola):

    ################### utes ###################
    TT                                        TT
    TT   HLBOKY LES         JAZERO     RUINY  TT
    TT   drevo, huby,       ~~~~~~     srot   TT
    TT   cistina s bobulami   |          |    TT
    TT        \______  krizovatka        |    TT
    TT                  |   \______    LOM    TT
    TT   LUKA           |          kamene     TT
    TT   vlakno, kvety  |                     TT
    TTTTTTTTTTTTTTTTTT cesta TTTTTTTTTTTTTTTTTTTT
                       ^ z farmy
"""

import argparse
import json
import math
import random
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import farm_map as F  # spolocne: noise, dlazdice, vrstvy, malovanie zeme
from farm_map import DIRS, LAYERS, GROUND_ORDER, SHADOW_ORDER, DECOR_ORDER, TILESETS, fbm, hash01
from paint import paint

ROOT, ART = F.ROOT, F.ART
CS = ROOT / "Assets" / "Editor" / "PlaceholderMapBuilder.Explore.cs"
OUT_JSON = ART / "Map" / "explore_map.json"
OUT_GROUND = ART / "Map" / "explore_ground.png"
OUT_PREVIEW = Path(__file__).resolve().parent / "explore_preview.png"

# ================================================================ NASTAVENIA
# Po zmene: Baa-ricade > Vygenerovat Explore lokaciu (alebo spusti skript a pozri explore_preview.png).
# Polohy casti su v podieloch mapy (0-1), takze sedia aj po zmene velkosti v C#.
CFG = dict(
    # okraj mapy (husty les, cez ktory sa neda prejst)
    border=5.0,                # hrubka v polickach
    border_wobble=2.5,         # o kolko sa zvlnuje
    # cesty
    path_width=1.25,           # polovica sirky cesty (min. 1 = vzdy aspon 2 policka)
    path_width_var=0.5,
    # casti mapy
    lake=(0.52, 0.74, 0.10, 0.085),   # jazero: stred x, y a polomery x, y
    forest=(0.38, 0.30),              # les: vlavo od x a nad y
    quarry=(0.63, 0.52),              # lom: vpravo od x a pod y
    clearing=(0.20, 0.62, 3.5),       # cistina v lese: x, y, polomer (policka)
    ruins=(0.84, 0.80, 5.0),          # ruiny: x, y, polomer
    # objekty
    wall_spacing=1.15,         # stromy na okraji (mensie = hustejsie)
    forest_spacing=1.35,       # stromy v lese
    forest_gatherable=0.45,    # podiel stromov v lese, ktore sa daju zrubat (na farme je ich menej)
    wall_gatherable=0.2,       # podiel zrubatelnych na vnutornom okraji steny
    lone_tree_spacing=7,       # osamele stromy na luke
    mushroom_spacing=3.0,      # huby v lese (zbieratelne)
    quarry_rock_spacing=1.8,   # kamene v lome
    quarry_rock_gatherable=0.7,
    scrap_piles=(5, 7),        # kolko hromad srotu je v ruinach
    meadow_tufts=0.35,         # podiel trsov travy (vlakno) medzi drobnostami na luke
    bush_spacing=1.6,
    # ozdoby
    dark_grass=0.62,
    flower_meadows=0.5,
    flower_spacing=2.6,
    flower_cluster=(4, 8),
    sprouts=0.35,
    cattail_spacing=3,
)
# ================================================================


def read_layout():
    src = CS.read_text(encoding="utf-8")

    def num(name):
        return int(re.search(rf"\b{name}\s*=\s*(-?\d+)", src).group(1))

    return dict(W=num("ExploreWidth"), H=num("ExploreHeight"), cliff=num("ExploreCliffRows"),
                entry_x=num("ExploreEntryX"), seed=num("ExploreSeed"))


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy or 1)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


class Wilds:
    def __init__(self, L, seed):
        self.L, self.seed = L, seed
        self.W, self.H = W, H = L["W"], L["H"]
        self.rng = random.Random(seed)
        self.kind = np.full((H, W), "grass", dtype=object)    # grass | dark | dirt | water
        self.cliff = np.zeros((H, W), bool)
        self.zone = np.full((H, W), "meadow", dtype=object)   # meadow | forest | clearing | quarry | ruins | wall
        self.layers = {name: np.full((H, W), None, dtype=object) for name, *_ in LAYERS}
        self.props, self.objects, self.taken = [], [], []
        self.layout()

    # -- pomocne
    def inb(self, x, y):
        return 0 <= x < self.W and 0 <= y < self.H

    def k(self, x, y):
        return self.kind[min(max(y, 0), self.H - 1), min(max(x, 0), self.W - 1)]

    def z(self, x, y):
        cx, cy = int(math.floor(x)), int(math.floor(y))
        return self.zone[cy, cx] if self.inb(cx, cy) else "wall"

    def on_path(self, x, y, m=0):
        """Jadro cesty (vzdy priechodne, 2 policka) + rezerva m."""
        return self.pdist[y, x] < 1.0 + m

    def near_water(self, cx, cy, r=1):
        return any(self.k(cx + a, cy + b) == "water" for a in range(-r, r + 1) for b in range(-r, r + 1))

    def var(self, name, x, y):
        return int(hash01(x, y, self.seed + len(name) * 7919) * len(TILESETS[name]))

    # -- 0. kde je co
    def layout(self):
        W, H, ex = self.W, self.H, self.L["entry_x"] + 0.5
        P = lambda fx, fy: (fx * W, fy * H)
        lx, ly, rx, ry = CFG["lake"]
        self.lake = (lx * W, ly * H, rx * W, ry * H)
        self.cross = (ex, H * 0.38)
        self.clearing = (*P(*CFG["clearing"][:2]), CFG["clearing"][2])
        self.ruins = (*P(*CFG["ruins"][:2]), CFG["ruins"][2])
        self.quarry_center = P(0.80, 0.28)
        cx, cy = self.cross
        lake_x, lake_y, lrx, lry = self.lake
        turn = P(0.72, 0.27)
        self.paths = [
            [(ex, -2), (ex, 5), (ex - 2, H * 0.2), (cx, cy)],                        # z farmy hore ku krizovatke
            [(cx, cy), P(0.36, 0.44), P(0.27, 0.55), self.clearing[:2]],              # do lesa na cistinu
            [(cx, cy), P(0.62, 0.34), turn, self.quarry_center],                      # do lomu
            [(cx, cy), (cx + 2, H * 0.52), (lake_x - lrx * 0.2, lake_y - lry - 1.0)],  # k jazeru
            [turn, P(0.80, 0.50), P(0.83, 0.66), self.ruins[:2]],                     # z lomu k ruinam
        ]
        self.pdist = np.array([[min(seg_dist(x + 0.5, y + 0.5, *a, *b) for p in self.paths for a, b in zip(p, p[1:]))
                                for x in range(W)] for y in range(H)])

    # -- 1. jazero
    def gen_water(self):
        lx, ly, rx, ry = self.lake
        for x in range(self.W):
            for y in range(self.H):
                d = ((x + 0.5 - lx) / rx) ** 2 + ((y + 0.5 - ly) / ry) ** 2
                if d + 0.7 * (fbm(x * 0.15, y * 0.15, self.seed + 5) - 0.5) < 1:
                    self.kind[y, x] = "water"

    # -- 2. utes, okraj a casti mapy
    def gen_zones(self):
        W, H, L, s = self.W, self.H, self.L, self.seed
        top = H - L["cliff"]
        ex = L["entry_x"] + 0.5
        fx, fy = CFG["forest"]
        qx, qy = CFG["quarry"]
        for x in range(W):
            for y in range(H):
                self.cliff[y, x] = y >= top
                px, py = x + 0.5, y + 0.5
                if px < fx * W + 4 * (fbm(py * 0.1, 3.1, s + 22) - 0.5) * 2 and py > fy * H + 3 * (fbm(px * 0.1, 5.2, s + 23) - 0.5) * 2:
                    self.zone[y, x] = "forest"
                if px > qx * W + 3 * (fbm(py * 0.1, 7.3, s + 24) - 0.5) * 2 and py < qy * H + 3 * (fbm(px * 0.1, 9.4, s + 25) - 0.5) * 2:
                    self.zone[y, x] = "quarry"
                for name, (cx, cy, r) in (("clearing", self.clearing), ("ruins", self.ruins)):
                    if math.hypot(px - cx, py - cy) < r + 1.2 * (fbm(x * 0.4, y * 0.4, s + 27) - 0.5) * 2:
                        self.zone[y, x] = name
                # okraj: husty les (pri vstupe z farmy je v nom priechod pre cestu)
                edge = min(px, W - px, py, top - py)
                b = CFG["border"] + CFG["border_wobble"] * (fbm(x * 0.12, y * 0.12, s + 21) - 0.5) * 2
                if edge < b and not self.on_path(x, y, 1.5) and not (abs(px - ex) < 3.5 and py < b + 2):
                    self.zone[y, x] = "wall"
                if self.cliff[y, x]:
                    self.zone[y, x] = "wall"

    # -- 3. hlina: cesty, krizovatka, dno lomu, ruiny
    def gen_dirt(self):
        s = self.seed
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "water" or self.cliff[y, x]:
                    continue
                wob = fbm(x * 0.35, y * 0.35, s + 31)
                zone = self.zone[y, x]
                if self.pdist[y, x] < max(1.0, CFG["path_width"] + CFG["path_width_var"] * (wob - 0.5) * 2):
                    self.kind[y, x] = "dirt"
                elif math.hypot(x + 0.5 - self.cross[0], y + 0.5 - self.cross[1]) < 2.0 + 1.5 * wob:
                    self.kind[y, x] = "dirt"
                elif zone == "quarry" and fbm(x * 0.18, y * 0.18, s + 33) > 0.42:
                    self.kind[y, x] = "dirt"
                elif zone == "ruins" and math.hypot(x + 0.5 - self.ruins[0], y + 0.5 - self.ruins[1]) < 3.0 + 1.5 * wob:
                    self.kind[y, x] = "dirt"

    # -- 4. tmava trava
    def gen_dark(self):
        s = self.seed
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] != "grass":
                    continue
                n = fbm(x * 0.17, y * 0.17, s + 41)
                n2 = fbm(x * 0.4, y * 0.4, s + 42)
                woods = self.zone[y, x] in ("forest", "wall")
                if woods and n2 > 0.28 or (self.zone[y, x] in ("meadow", "quarry") and n + 0.35 * (n2 - 0.5) > CFG["dark_grass"]):
                    self.kind[y, x] = "dark"
        self.fix_dark()

    def fix_dark(self):
        # okraje zeme su kreslene pre svetlu travu: pri hline a vode vzdy svetla
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "dark" and any(self.k(x + a, y + b) in ("dirt", "water") for a, b, _ in DIRS):
                    self.kind[y, x] = "grass"

    # -- 5. upratanie: ziadne pasiky siroke 1 policko (rovnake pravidla ako farm_map.py)
    def cleanup(self):
        open_ = lambda v: v in ("dark", "dirt")
        for _ in range(4):
            changed = False
            for x in range(self.W):
                for y in range(self.H):
                    t = self.kind[y, x]
                    if t == "water":
                        continue
                    n, s, e, w = self.k(x, y + 1), self.k(x, y - 1), self.k(x + 1, y), self.k(x - 1, y)
                    if t == "grass" and ((open_(n) and open_(s)) or (open_(e) and open_(w))):
                        pair = [v for v in (n, s, e, w) if open_(v)]
                        self.kind[y, x] = "dirt" if "dirt" in pair else "dark"
                        changed = True
                    elif t in ("dark", "dirt") and not self.on_path(x, y):
                        same = sum(1 for v in (n, s, e, w) if v == t)
                        if same <= 1 and ((n == s == "grass") or (e == w == "grass") or same == 0):
                            self.kind[y, x] = "grass"
                            changed = True
            if not changed:
                break
        self.fix_dark()

    # -- 6. dlazdice (kolizia jazera, utes)
    def gen_tiles(self):
        for x in range(self.W):
            for y in range(self.H):
                if self.kind[y, x] == "water":
                    self.layers["Water"][y, x] = ("water", 0)
                if self.cliff[y, x]:
                    self.layers["Cliff"][y, x] = ("cliff", x % len(TILESETS["cliff"]))

    def paint_ground(self):
        def var_of(name):
            n = len(TILESETS[name])
            g = np.array([[int(hash01(x, y, self.seed + len(name) * 7919) * n) for x in range(self.W)] for y in range(self.H)])
            return np.flipud(g)
        rgb, _ = paint(np.flipud(self.kind), TILESETS, self.seed, var_of)
        return Image.fromarray(rgb).convert("RGBA")

    # -- 7. objekty
    def blocked(self, x, y, big=True):
        cx, cy = int(math.floor(x)), int(math.floor(y))
        if not self.inb(cx, cy):
            return True
        if self.kind[cy, cx] == "water" or self.cliff[cy, cx] or cy >= self.H - self.L["cliff"] - (1 if big else 0):
            return True
        if big and self.near_water(cx, cy):
            return True
        if self.on_path(cx, cy, 1) or (big and self.kind[cy, cx] == "dirt"):
            return True
        ex = self.L["entry_x"] + 0.5
        if abs(cx + 0.5 - ex) < 3 and cy < 7:          # prichod z farmy
            return True
        if math.hypot(cx + 0.5 - self.cross[0], cy + 0.5 - self.cross[1]) < 3.5:
            return True
        return False

    def free(self, x, y, r):
        return all((x - a) ** 2 + (y - b) ** 2 >= (r + c) ** 2 for a, b, c in self.taken)

    def add_prop(self, sprite, frame, x, y, r, collider=None, shadow=None, flip=False):
        x, y = round(x * 16) / 16, round(y * 16) / 16
        self.taken.append((x, y, r))
        if shadow is not None:
            self.props.append(dict(sprite="Props/shadows", frame=shadow, x=x, y=y - 0.25, order=SHADOW_ORDER,
                                   collider=False, cw=0, ch=0, flip=False))
        self.props.append(dict(sprite=sprite, frame=frame, x=x, y=y, order=0, collider=collider is not None,
                               cw=collider[0] if collider else 0, ch=collider[1] if collider else 0, flip=flip))

    def add_object(self, prefab, cx, cy, r=0.7):
        self.taken.append((cx + 0.5, cy, r))
        self.objects.append(dict(prefab=prefab, x=cx, y=cy))

    def try_object(self, prefab, x, y, r=0.7):
        cx, cy = int(math.floor(x)), int(math.floor(y))
        if self.free(cx + 0.5, cy, r):
            self.add_object(prefab, cx, cy, r)
            return True
        return False

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
            self.try_object("Tree", x, y, 0.9)
            return
        woods = self.z(x, y) in ("forest", "wall")
        pine_share = 0.75 if woods else 0.25
        kind = "trees_pine" if fbm(x * 0.12, y * 0.12, self.seed + 61) < pine_share else "trees_round"
        if hash01(cx, cy, self.seed + 62) < 0.08:
            kind = "trees_tall"
        frame = int(hash01(cx, cy, self.seed + 63) * len(TILESETS[kind]))
        if self.free(x, y, 0.75):
            self.add_prop(f"Props/{kind}", frame, x, y, 0.75, collider=(0.6, 0.4), shadow=0, flip=hash01(cx, cy, 64) < 0.5)

    def big_rock(self, x, y):
        if self.free(x, y, 0.7):
            self.add_prop("Props/rocks", self.rng.randrange(len(TILESETS["rocks"])), x, y, 0.7, collider=(0.9, 0.4), shadow=1)

    def bush(self, x, y):
        if self.free(x, y, 0.6):
            frame = int(hash01(int(x * 7), int(y * 7), self.seed + 82) * len(TILESETS["bushes"]))
            self.add_prop("Props/bushes", frame, x, y, 0.6, collider=(0.8, 0.4), shadow=1, flip=hash01(int(x), int(y), 83) < 0.5)

    def gen_objects(self):
        rng, z, C = self.rng, self.z, CFG
        inner_wall = lambda x, y: z(x, y) == "wall" and any(z(x + a, y + b) != "wall" for a in (-2, 0, 2) for b in (-2, 0, 2))
        woods_edge = lambda x, y: z(x, y) == "forest" and any(z(x + a, y + b) not in ("forest", "wall")
                                                              for a in (-2, 0, 2) for b in (-2, 0, 2))

        # ruiny prve (aby im stromy nezabrali miesto): hromady srotu - vzacna surovina daleko od farmy
        rx, ry, rr = self.ruins
        want = rng.randint(*C["scrap_piles"])
        for _ in range(400):
            if want <= 0:
                break
            a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.5, rr - 1.5)
            x, y = rx + math.cos(a) * d, ry + math.sin(a) * d * 0.8
            if not self.blocked(x, y, big=False) and self.try_object("ScrapPile", x, y, 0.9):
                want -= 1
        for x, y in self.scatter(lambda x, y: z(x, y) == "ruins" and not self.blocked(x, y, big=False), 1.6, 300):
            if rng.random() < 0.5:
                self.big_rock(x, y)
            elif self.free(x, y, 0.5):
                self.add_prop("Props/stumps", rng.randrange(len(TILESETS["stumps"])), x, y, 0.5, collider=(0.7, 0.3))

        # cistina v lese: kruh krikov s bobulami
        cx, cy, cr = self.clearing
        for i in range(7):
            a = i * 2 * math.pi / 7 + rng.uniform(-0.2, 0.2)
            x, y = cx + math.cos(a) * (cr - 0.8), cy + math.sin(a) * (cr - 0.8) * 0.8
            if not self.blocked(x, y, big=False):
                self.try_object("BerryBush", x, y)

        # okraj: husty les (dekoracia), na vnutornom okraji sem-tam zrubatelny strom
        for x, y in self.scatter(lambda x, y: z(x, y) == "wall" and not self.blocked(x, y), C["wall_spacing"], 12000):
            self.tree(x, y, gatherable=inner_wall(x, y) and hash01(int(x), int(y), 71) < C["wall_gatherable"])
        # les: husto a vela dreva
        for x, y in self.scatter(lambda x, y: z(x, y) == "forest" and not self.blocked(x, y), C["forest_spacing"], 7000):
            self.tree(x, y, gatherable=hash01(int(x), int(y), 72) < C["forest_gatherable"])
        # luka a lom: osamele stromy
        for x, y in self.scatter(lambda x, y: z(x, y) in ("meadow", "quarry") and not self.blocked(x, y), C["lone_tree_spacing"], 600):
            self.tree(x, y, gatherable=True)

        # lom: kamene (vacsina sa da vykopat) a par velkych dekoracnych
        for x, y in self.scatter(lambda x, y: z(x, y) == "quarry" and not self.blocked(x, y, big=False), C["quarry_rock_spacing"], 3000):
            if rng.random() < C["quarry_rock_gatherable"]:
                self.try_object("Rock", x, y)
            else:
                self.big_rock(x, y)

        # les: huby (zbieratelne), velke kamene a pniky
        for x, y in self.scatter(lambda x, y: z(x, y) in ("forest", "clearing") and not self.blocked(x, y, big=False), C["mushroom_spacing"], 3000):
            self.try_object("Mushroom", x, y, 0.5)
        for x, y in self.scatter(lambda x, y: z(x, y) == "forest" and not self.blocked(x, y), 4.5, 500):
            if hash01(int(x), int(y), 111) < 0.5:
                self.big_rock(x, y)
            elif self.free(x, y, 0.5):
                self.add_prop("Props/stumps", rng.randrange(len(TILESETS["stumps"])), x, y, 0.5, collider=(0.7, 0.3))

        # kriky: okraj lesa, okolo jazera, pri okrajovej stene
        def bush_zone(x, y):
            if self.blocked(x, y, big=False) or self.kind[int(y), int(x)] == "dirt":
                return False
            return woods_edge(x, y) or inner_wall(x, y) or (z(x, y) == "meadow" and self.near_water(int(x), int(y), 3))
        for x, y in self.scatter(bush_zone, C["bush_spacing"], 3000):
            if hash01(int(x * 3), int(y * 3), 81) < 0.4:
                continue
            if woods_edge(x, y) and rng.random() < 0.25:
                if not self.blocked(x, y, big=False):
                    self.try_object("BerryBush", x, y)
            else:
                self.bush(x, y)

        # luka: trs travy (vlakno), kamen, naprstniky, kamienky; pri jazere viac travy
        meadow = lambda x, y: z(x, y) == "meadow" and not self.blocked(x, y, big=False) and self.kind[int(y), int(x)] != "dirt"
        for x, y in self.scatter(meadow, 2.0, 4000):
            cx, cy = int(x), int(y)
            if not self.free(x, y, 0.5):
                continue
            r = hash01(cx, cy, 101)
            tufts = C["meadow_tufts"] + (0.3 if self.near_water(cx, cy, 3) else 0)
            if r < tufts:
                self.add_object("GrassTuft", cx, cy, 0.5)
            elif r < tufts + 0.06:
                self.add_object("Rock", cx, cy)
            elif r < tufts + 0.18:
                for _ in range(rng.randint(2, 4)):
                    fx, fy = x + rng.uniform(-0.8, 0.8), y + rng.uniform(-0.5, 0.5)
                    if not self.blocked(fx, fy, big=False):
                        self.add_prop("Props/foxgloves", rng.randrange(3), fx, fy, 0.25)
            elif r < tufts + 0.3:
                self.add_prop("Props/rocks_small", rng.randrange(len(TILESETS["rocks_small"])), x, y, 0.3)
        # lom: drobne kamienky
        for x, y in self.scatter(lambda x, y: z(x, y) == "quarry" and not self.blocked(x, y, big=False), 2.5, 800):
            if self.free(x, y, 0.3) and rng.random() < 0.5:
                self.add_prop("Props/rocks_small", rng.randrange(len(TILESETS["rocks_small"])), x, y, 0.3)

    # -- 8. ozdoby na zemi (pod postavami)
    def gen_decor(self):
        rng, z = self.rng, self.z

        def add(name, frame, x, y):
            if all((x - a) ** 2 + (y - b) ** 2 >= min(c, 0.6) ** 2 for a, b, c in self.taken):
                self.props.append(dict(sprite=f"Tiles/{name}", frame=frame, x=round(x * 16) / 16, y=round(y * 16) / 16,
                                       order=DECOR_ORDER, collider=False, cw=0, ch=0, flip=rng.random() < 0.5))

        def tile(x, y):
            cx, cy = int(math.floor(x)), int(math.floor(y))
            return (cx, cy) if self.inb(cx, cy) and not self.cliff[cy, cx] else None

        def at_water(x, y):
            t = tile(x, y)
            return t is not None and self.kind[t[1], t[0]] != "water" and any(self.k(t[0] + a, t[1] + b) == "water" for a, b, _ in DIRS)

        def meadow(x, y):
            t = tile(x, y)
            return (t is not None and z(x, y) == "meadow" and self.kind[t[1], t[0]] not in ("dirt", "water")
                    and not self.on_path(t[0], t[1], 1) and not self.near_water(t[0], t[1], 2))

        flowers = TILESETS["Decor/flowers"]
        placed = []

        def flower(x, y, color):
            if meadow(x, y) and all((x - a) ** 2 + (y - b) ** 2 >= 0.45 ** 2 for a, b in placed):
                placed.append((x, y))
                add("Decor/flowers", min(len(flowers) - 1, color * 4 + rng.randrange(4)), x, y)

        for cx, cy in self.scatter(lambda x, y: meadow(x, y) and fbm(x * 0.12, y * 0.12, self.seed + 53) > CFG["flower_meadows"],
                                   CFG["flower_spacing"], 2500):
            color = rng.randrange(4)
            for _ in range(rng.randint(*CFG["flower_cluster"])):
                flower(cx + rng.gauss(0, 0.65), cy + rng.gauss(0, 0.45), color if rng.random() < 0.8 else rng.randrange(4))
        for x, y in self.scatter(meadow, 6, 600):
            flower(x, y, rng.randrange(4))

        grassy = lambda x, y: (tile(x, y) is not None and self.kind[int(y), int(x)] not in ("dirt", "water")
                               and z(x, y) in ("meadow", "quarry", "clearing") and not at_water(x, y))
        for x, y in self.scatter(grassy, 1.4, 5000):
            if rng.random() < CFG["sprouts"]:
                for _ in range(1 if rng.random() < 0.7 else rng.randint(2, 3)):
                    sx, sy = x + rng.uniform(-0.5, 0.5), y + rng.uniform(-0.4, 0.4)
                    if grassy(sx, sy):
                        add("Decor/sprouts", rng.randrange(len(TILESETS["Decor/sprouts"])), sx, sy)

        woods = lambda x, y: tile(x, y) is not None and z(x, y) in ("forest", "wall", "clearing") and not at_water(x, y)
        for x, y in self.scatter(woods, 1.5, 5000):
            r = rng.random()
            if r < 0.2:
                add("Decor/forest_decor", rng.randrange(len(TILESETS["Decor/forest_decor"])), x, y)
            elif r < 0.5:
                add("Decor/sprouts", rng.randrange(len(TILESETS["Decor/sprouts"])), x, y)

        for cx, cy in self.scatter(at_water, CFG["cattail_spacing"], 2000):
            if rng.random() < 0.7:
                for _ in range(rng.randint(2, 4)):
                    x, y = cx + rng.uniform(-0.8, 0.8), cy + rng.uniform(-0.3, 0.3)
                    if at_water(x, y):
                        add("Decor/cattails", rng.randrange(len(TILESETS["Decor/cattails"])), x, y)

        on_dirt = lambda x, y: tile(x, y) is not None and self.kind[int(y), int(x)] == "dirt"
        for x, y in self.scatter(on_dirt, 2.0, 1500):
            if rng.random() < (0.6 if z(x, y) in ("quarry", "ruins") else 0.25):
                add("Decor/pebbles", rng.randrange(len(TILESETS["Decor/pebbles"])), x, y)

    def generate(self):
        self.gen_water()
        self.gen_zones()
        self.gen_dirt()
        self.gen_dark()
        self.cleanup()
        self.gen_tiles()
        self.gen_objects()
        self.gen_decor()
        self.ground = self.paint_ground()


# ---------------------------------------------------------------- vystup
def export(m):
    keys, index, layers = [], {}, []
    for name, order, coll, hidden in LAYERS:
        cells = []
        for y in range(m.H):
            for x in range(m.W):
                v = m.layers[name][y, x]
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
    data = dict(width=m.W, height=m.H, seed=m.seed, ground="Map/explore_ground", groundOrder=GROUND_ORDER,
                tiles=keys, layers=layers, objects=m.objects, props=m.props)
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")


# Objekty bez nakreslenej grafiky (v Unity maju placeholder) su v nahlade farebne znacky.
PREVIEW_MARKERS = {"Mushroom": (200, 60, 60), "ScrapPile": (150, 150, 165)}


def render(m):
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
    sprites = {}
    for prefab, file in (("Tree", "tree"), ("Rock", "rock"), ("BerryBush", "berry_bush"), ("GrassTuft", "grass_tuft")):
        p = ART / "Objects" / f"{file}.aseprite"
        sprites[prefab] = F.A.read(p)[0] if p.exists() else None

    def prop_sprite(path):
        return TILESETS[path[len("Tiles/"):]] if path.startswith("Tiles/") else TILESETS[path.split("/")[-1]]

    draw = [(p["order"], 1 if p["sprite"].startswith("Tiles/") else 0, p["x"], p["y"], prop_sprite(p["sprite"])[p["frame"]], p["flip"])
            for p in m.props]
    markers = []
    for o in m.objects:
        if sprites.get(o["prefab"]) is not None:
            draw.append((0, 0, o["x"] + 0.5, o["y"], sprites[o["prefab"]], False))
        else:
            markers.append(o)
    draw.sort(key=lambda d: (d[0], -d[3]))
    for order, centered, x, y, spr, flip in draw:
        s = spr.transpose(Image.FLIP_LEFT_RIGHT) if flip else spr
        top = (m.H - y) * T - (s.height / 2 if centered else s.height)
        img.alpha_composite(s, (int(round(x * T - s.width / 2)), int(round(top))))
    d = ImageDraw.Draw(img)
    for o in markers:
        px, py = (o["x"] + 0.5) * T, (m.H - o["y"]) * T
        d.ellipse((px - 5, py - 9, px + 5, py - 1), fill=PREVIEW_MARKERS.get(o["prefab"], (255, 0, 255)), outline=(26, 28, 44))
    # vstup z farmy
    ex = (m.L["entry_x"] + 0.5) * T
    d.rectangle((ex - 24, m.H * T - 24, ex + 24, m.H * T - 1), outline=(255, 220, 80))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--no-preview", action="store_true")
    args = ap.parse_args()
    L = read_layout()
    TILESETS.update(F.load_tilesets())
    m = Wilds(L, args.seed if args.seed is not None else L["seed"])
    m.generate()
    export(m)
    counts = {}
    for o in m.objects:
        counts[o["prefab"]] = counts.get(o["prefab"], 0) + 1
    print(f"Explore {m.W}x{m.H} (seed {m.seed}): " + ", ".join(f"{v}x {k}" for k, v in sorted(counts.items()))
          + f", {len(m.props)} dekoracii -> {OUT_JSON.relative_to(ROOT)}")
    if not args.no_preview:
        render(m).save(OUT_PREVIEW)
        print(f"Nahlad: {OUT_PREVIEW.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
