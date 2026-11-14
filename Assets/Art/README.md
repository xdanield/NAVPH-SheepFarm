# Grafika

Mierka: **1 tile = 16×16 px = 1 Unity unit**. Kamera: 320×180 (20×11 tiles), na 1080p zväčšené 6×, na 1440p 8×.

## Súbory

| Priečinok | Plátno | Poznámka |
|---|---|---|
| `Characters/` | 32×32 | postava je vysoká 2 tiles; nohy na **spodnom okraji** v strede; bočné snímky kresli otočené **doprava** |
| `Objects/` | 16×16 (strom 32×48, dom 128×112, stôl 32×32) | spodok objektu na spodnom okraji plátna |
| `Tiles/` | 16×16 | musia na seba nadväzovať, zapni si `View > Tiled Mode > Both` |
| `Background/` | 1024 px široké | výhľad z kopca nad mapou: `sky` (obloha, slnko), `mountains`, `city` (mesto vľavo). Kresli od spodku, hore nechaj priehľadné. Vrstvy sa posúvajú s kamerou rôznou rýchlosťou (parallax) |

Snímky a tagy (`idle_down`, `walk_side`…) sú v súboroch pripravené. Z tagov Unity vyrobí animácie.
V Aseprite si zapni mriežku (`View > Show > Grid`), je nastavená na 16×16 (1 tile).

Nepridávaj ani nemaž snímky v strede súboru. Ak treba viac snímok, povedz, upravíme
`Tools/make_aseprite_templates.py`.

## Ako dostať kresbu do hry

1. Nakresli a ulož `.aseprite` (Ctrl+S). Unity ho načíta sama.
2. Tiles, objekty a pozadie: v Unity klikni **Baa-ricade > Aktualizovat sprity z Aseprite**.
   Placeholdery sa nahradia tvojou kresbou, mapa sa neprestavuje.
3. Postavy: animácie sa napoja cez Animator (zatiaľ nie je hotové).

## Suroviny (zber na E)

Niektoré objekty majú dva snímky: ako vyzerajú celé a po vyzbieraní.

| Súbor | Snímok 1 | Snímok 2 (po vyzbieraní) |
|---|---|---|
| `Objects/tree.aseprite` | `tree` | `stump`: pník, kresli ho na rovnaké miesto ako kmeň (päta musí sedieť) |
| `Objects/berry_bush.aseprite` | `full` | `empty`: ker bez bobúľ |
| `Objects/rock.aseprite` | `rock` | žiadny, po vyzbieraní zmizne |
| `Objects/grass_tuft.aseprite` | `grass` | žiadny, po vyzbieraní zmizne |

Po nakreslení klikni **Baa-ricade > Aktualizovat sprity z Aseprite** a napojí sa aj druhý snímok.
Pri zbere sa objekt kýve okolo päty, preto nech je päta naozaj na spodnom okraji.

Ikonky predmetov v inventári (drevo, kameň…) sú zatiaľ v `Placeholder/Items/`. Vlastnú ikonku
priraď v `Assets/Items/<predmet>.asset` do poľa **Icon**.

Animácia zberu hráčky: ak v Animatori pridáš bool parameter `IsGathering`, hra ho zapína,
kým hráčka drží E. Kód netreba meniť.

## Mapa farmy

Mapu vymýšľa Python generátor `Tools/mapgen/farm_map.py`. V Unity stačí kliknúť
**Baa-ricade > Vygenerovat mapu**: spustí generátor a postaví mapu (zmaže a nanovo vytvorí `Environment` a `Backdrop`).

- Výsledok je v `Map/farm_map.json` (čo je na ktorom políčku, kde stoja stromy, kríky…) a `Map/farm_ground.png`
  (zem namaľovaná po pixeloch: tráva, hlina, potok s brehmi). Oba súbory sú v gite, takže kto nemá Python,
  postaví poslednú uloženú mapu.
- Generátor potrebuje Python 3 s balíkmi `numpy` a `pillow` (`pip install numpy pillow`).
- Náhľad bez Unity: `python Tools/mapgen/farm_map.py` uloží aj `Tools/mapgen/preview.png`.
  Iná náhodná mapa: `python Tools/mapgen/farm_map.py --seed 777`.
- Veľkosť mapy, ohradu, dom, crafting a zombie pruhy berie generátor z konštánt na začiatku
  `Assets/Editor/PlaceholderMapBuilder.cs`. Stačí ich zmeniť tam.
- Ohradu, obranu, dom, crafting a body (spawn, štart, východ) stavia C# ako predtým.

## Dlaždice, ozdoby a dekorácie (Pixel Crawler)

Grafika terénu a prírody je z free balíka **Pixel Crawler** od Anokolisa (komerčne OK, kredit netreba, ale poteší).
Načítal ju `Tools/import_pixel_crawler_tiles.py` (ručne kreslené súbory neprepíše, `--force` prepíše všetko).

- `Tiles/*.aseprite`: textúry pre zem (`grass`, `grass_dark`, `dirt`, `water`, `water_deep`) a útes (`cliff`, ide zľava
  doprava za sebou). Každý snímok je **varianta**. Zem z nich generátor namaľuje, okraje počíta sám.
- `Tiles/Decor/`: ozdoby na vrstve Decor: `flowers` (4 farby × 4 varianty, v tomto poradí), `sprouts`,
  `forest_decor`, `cattails` (pri potoku), `pebbles` (na hline).
- `Props/`: stromy (`trees_round`, `trees_pine`, `trees_tall`), `bushes`, `rocks`, `rocks_small`, `foxgloves`,
  `stumps`, `shadows`. Generátor ich rozmiestni, v scéne sú v `Environment/Props`.
- `Objects/tree|rock|berry_bush|grass_tuft`: zbierateľné veci, tiež z balíka (dajú sa prekresliť).
- Po úprave grafiky: **Baa-ricade > Vygenerovat mapu**.

## Ďalšie

- `Placeholder/`: vygenerované dočasné obrázky, nemeň ich.
- `TileAssets/`: Tile assety, ktoré používa mapa.
- Nové súbory dostanú správne nastavenia importu samy (16 PPU, Point filter, bez kompresie).
  Ak niečo vyzerá rozmazane: **Baa-ricade > Opravit import grafiky**.
