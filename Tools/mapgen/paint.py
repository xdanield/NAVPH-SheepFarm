"""
Namaluje zem celej mapy po pixeloch (jeden obrazok), aby okraje travy, hliny a brehy potoka neboli
z opakujucich sa dlazdic. Textury a farby su z balika Pixel Crawler, tvar okrajov je zo sumu.

Pravidla okrajov su odpozerane z dlazdic balika:
  - svetla trava: na spodnom okraji tmavsi zeleny pixel, pod nou 1 px polopriehladny cierny tien
  - breh potoka: pod travou tmavohnedy pas (hrubsi, ked je voda pod travou), miestami svetlejsie hrudy hliny,
    potom svetla ciara peny a voda
"""

import numpy as np

T = 16
RIM = np.array([46, 109, 3])
SHADOW = 66 / 255
BANK_DARK = np.array([134, 89, 50])
BANK_LIGHT = np.array([159, 108, 63])
FOAM = np.array([123, 170, 219])
WATER_SHADE = np.array([65, 133, 202])


def value_noise(h, w, cell, seed):
    """Hladky sum (h x w pixelov) s velkostou 'cell' pixelov, hodnoty 0..1."""
    rng = np.random.default_rng(seed)
    gh, gw = h // cell + 2, w // cell + 2
    g = rng.random((gh, gw))
    ys = np.arange(h) / cell
    xs = np.arange(w) / cell
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = ys - y0, xs - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def edge_noise(h, w, seed):
    """Sum pre okraje: velke vlny + stredne hrbole + drobne zubky. Stred okolo 0."""
    return (0.24 * (value_noise(h, w, 28, seed) - 0.5) + 0.16 * (value_noise(h, w, 11, seed + 1) - 0.5)
            + 0.16 * (value_noise(h, w, 3, seed + 2) - 0.5)) * 2


def blur(a, r):
    """Box blur s polomerom r (dvakrat = hladke prechody medzi polickami)."""
    for _ in range(2):
        for axis in (0, 1):
            p = np.pad(a, [(r + 1, r) if i == axis else (0, 0) for i in range(2)], mode="edge")
            c = np.cumsum(p, axis=axis)
            if axis == 0:
                a = (c[2 * r + 1:] - c[:-2 * r - 1]) / (2 * r + 1)
            else:
                a = (c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)
    return a


def up(cells):
    """Policka (H x W, riadok 0 = hore) -> pixely (H*16 x W*16), plynulo."""
    big = np.kron(cells.astype(float), np.ones((T, T)))
    return blur(big, 7)


def tidy(mask):
    """Odstrani osamele pixely (vyzeraju ako sum, nie pixelart)."""
    m = mask.astype(int)
    p = np.pad(m, 1, mode="edge")
    n = sum(p[1 + dy:1 + dy + m.shape[0], 1 + dx:1 + dx + m.shape[1]] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) - m
    out = mask.copy()
    out[(m == 1) & (n <= 2)] = False
    out[(m == 0) & (n >= 6)] = True
    return out


def shift(a, dy, dx, fill=False):
    """Posun masky: vysledok[y, x] = a[y - dy, x - dx]."""
    out = np.full_like(a, fill)
    h, w = a.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = a[ys, xs]
    return out


def texture(tiles, variant_of_cell, h, w):
    """Vyplni plochu texturou dlazdic, kazde policko svoja varianta (bez 16 px opakovania)."""
    arr = np.stack([np.array(t.convert("RGB"), dtype=float) for t in tiles])
    H, W = h // T, w // T
    idx = np.kron(variant_of_cell, np.ones((T, T), int))
    yy, xx = np.mgrid[0:h, 0:w]
    return arr[idx, yy % T, xx % T]


def paint(kind, tiles, seed, var_of):
    """
    kind: pole H x W (riadok 0 = HORE na obrazovke) s hodnotami grass|dark|dirt|water.
    tiles: dict nazov -> zoznam 16x16 obrazkov. var_of(nazov) -> pole H x W s variantami.
    Vrati RGB obrazok (numpy uint8) a masku vody v pixeloch.
    """
    H, W = kind.shape
    h, w = H * T, W * T
    is_ = lambda k: (kind == k)

    # --- tvary v pixeloch
    light = up(is_("grass") | is_("water")) + edge_noise(h, w, seed + 1) > 0.5
    water = up(is_("water")) + 0.8 * edge_noise(h, w, seed + 2) > 0.5
    dirt_f, dark_f = up(is_("dirt")), up(is_("dark"))
    light, water = tidy(light), tidy(water)
    light &= ~water

    img = np.where((dirt_f > dark_f)[..., None], texture(tiles["dirt"], var_of("dirt"), h, w),
                   texture(tiles["grass_dark"], var_of("grass_dark"), h, w))

    # --- svetla trava: tien pod okrajom (a slabsi po bokoch), tmavsi pixel na spodnom okraji
    shadow = ~light & ~water & shift(light, 1, 0)
    shadow2 = ~light & ~water & ~shadow & shift(light, 2, 0)
    side = ~light & ~water & ~shadow & (shift(light, 0, 1) | shift(light, 0, -1))
    img[shadow] *= (1 - SHADOW)
    img[shadow2] *= (1 - SHADOW * 0.5)
    img[side] *= (1 - SHADOW * 0.5)
    img = np.where(light[..., None], texture(tiles["grass"], var_of("grass"), h, w), img)
    rim = light & ~shift(light, -1, 0, True) & (value_noise(h, w, 3, seed + 5) > 0.35)
    img[rim] = RIM

    # --- potok: breh, pena, voda
    land = ~water
    fine = value_noise(h, w, 3, seed + 7)
    thick = 3 + np.round(4 * value_noise(h, w, 9, seed + 8) + fine).astype(int)   # 3-8 px, ked je voda pod
    bank = np.zeros((h, w), bool)
    depth = np.zeros((h, w), int)  # ako daleko nad vodou (pre svetlejsie hrudy pri vode)
    for k in range(1, 9):
        below = shift(water, -k, 0)          # voda k pixelov pod
        hit = land & below & (k <= thick) & ~bank
        depth[hit] = k
        bank |= hit
    for dx in (-2, -1, 1, 2):
        bank |= land & shift(water, 0, dx) & (abs(dx) <= 1 + (fine > 0.5))
    bank |= land & (shift(water, 1, 0) | (shift(water, 2, 0) & (fine > 0.45)))  # voda nad: tenky breh
    lumps = bank & (depth > 0) & (depth <= 4) & (value_noise(h, w, 6, seed + 9) > 0.5)
    img[bank] = BANK_DARK
    img[lumps] = BANK_LIGHT
    img = np.where(water[..., None], texture(tiles["water"], var_of("water"), h, w), img)
    foam = water & (shift(land, 1, 0) | ((shift(land, 0, 1) | shift(land, 0, -1)) & (fine > 0.4)))
    shade = water & ~foam & (shift(foam, 1, 0) | shift(foam, 2, 0)) & (value_noise(h, w, 5, seed + 10) > 0.4)
    img[shade] = WATER_SHADE
    img[foam] = FOAM
    return np.clip(img, 0, 255).astype(np.uint8), water
