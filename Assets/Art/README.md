# Grafika

Mierka: **1 tile = 32×32 px = 1 Unity unit**. Kamera: 640×360 (20×11 tiles), na 1080p zväčšené 3×, na 1440p 4×.

## Súbory

| Priečinok | Plátno | Poznámka |
|---|---|---|
| `Characters/` | 64×64 | postava je vysoká 2 tiles; nohy na **spodnom okraji** v strede; bočné snímky kresli otočené **doprava** |
| `Objects/` | 32×32 (strom 64×96, dom 256×224) | spodok objektu na spodnom okraji plátna |
| `Tiles/` | 32×32 | musia na seba nadväzovať, zapni si `View > Tiled Mode > Both` |
| `Background/` | 2048 px široké | výhľad z kopca nad mapou: `sky` (obloha, slnko), `mountains`, `city` (mesto vľavo). Kresli od spodku, hore nechaj priehľadné. Vrstvy sa posúvajú s kamerou rôznou rýchlosťou (parallax) |

Snímky a tagy (`idle_down`, `walk_side`…) sú v súboroch pripravené. Z tagov Unity vyrobí animácie.
V Aseprite si zapni mriežku (`View > Show > Grid`), je nastavená na 32×32 (1 tile).

Nepridávaj ani nemaž snímky v strede súboru. Ak treba viac snímok, povedz, upravíme
`Tools/make_aseprite_templates.py`.

## Ako dostať kresbu do hry

1. Nakresli a ulož `.aseprite` (Ctrl+S). Unity ho načíta sama.
2. Tiles, objekty a pozadie: v Unity klikni **Baa-ricade > Aktualizovat sprity z Aseprite**.
   Placeholdery sa nahradia tvojou kresbou, mapa sa neprestavuje.
3. Postavy: animácie sa napoja cez Animator (zatiaľ nie je hotové).

## Ďalšie

- `Placeholder/`: vygenerované dočasné obrázky, nemeň ich.
- `Characters/Temp/`: dočasné postavy (zo screenshotov). Keď nakreslíš frame v `.aseprite`, použije sa ten.
- `TileAssets/`: Tile assety, ktoré používa mapa.
- Nové súbory dostanú správne nastavenia importu samy (32 PPU, Point filter, bez kompresie).
  Ak niečo vyzerá rozmazane: **Baa-ricade > Opravit import grafiky**.
