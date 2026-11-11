# NAVPH-SheepFarm (Baa-Ricade)

Plán úloh je v [PLAN.md](PLAN.md).

## Mapa farmy (P1-01, Adi)

Hotová je **placeholder mapa** scény `Farm`. Grafika je dočasná (farebné kocky, postavy zo screenshotov), finálny pixelart doplním neskôr. Kód a pozície sa pri výmene grafiky nemenia, takže na mape sa dá stavať už teraz.

**Layout:** hore okraj kopca a za ním výhľad (obloha, hory, mesto), vľavo pás lesa, odkiaľ chodia zombie, v strede ohrada s ovcami a bodcami, vpravo hore dom, pod ním crafting (stôl, hrniec, truhla), dole potok. Zber surovín bude v inej lokácii, kam vedie cesta hore pri dome (`ExploreExit`).

### Ako to funguje

- **Mierka:** 1 tile = 32 px = 1 Unity unit. Postavy majú 64×64 px (2 tiles). Kamera je Pixel Perfect 640×360, to je 20×11 tiles.
- **Menu `Baa-ricade`** v Unity:
  - `Vygenerovat placeholder mapu`: zmaže objekty `Environment` a `Backdrop` a postaví mapu nanovo.
  - `Aktualizovat sprity z Aseprite`: vymení placeholdery za nakreslenú grafiku, mapa ostane.
- Generátor je [Assets/Editor/PlaceholderMapBuilder.cs](Assets/Editor/PlaceholderMapBuilder.cs). Grafika a návod pre kreslenie sú v [Assets/Art/](Assets/Art/README.md).

### Čo je v scéne

| Objekt | Na čo |
|---|---|
| `Environment/Grid` | tilemapy `Ground`, `Decor`, `Collision` (voda, útes; použiť pre NavMeshPlus) |
| `Environment/Pen` | ploty ohrady, sem patrí skript s HP ohrady (P1-06) |
| `Environment/Defenses` | ploty a bodce na štart |
| `Environment/Farm` | dom, crafting stôl, hrniec, truhla |
| `Environment/Points` | `PlayerStart`, `ZombieSpawn_*`, `ExploreExit` (trigger) |
| `Backdrop` | pozadie s parallaxom |

Prefaby prostredia sú v `Assets/Prefabs/Environment/`.

### Čo robiť / na čo dať pozor

- **Negenerujte mapu znova**, ak už v `Environment` niekto niečo ručne zmenil, lebo sa to zmaže. Mapu odteraz upravujte ručne.
- Pri spawne, ovciach, stavaní a pod. používajte objekty z `Points` a `Pen`, nie natvrdo zadané súradnice.
- Colliders postáv a objektov sú **len pri nohách** (ako v Stardew). Na zásahy nožom a hryzenie si pridajte vlastný trigger.
- Stavanie (P1-11) prichytávajte na mriežku `Grid` (bunka = 1 unit).
- **P1-09 a P2-08 (zber surovín)** sa presúvajú do inej scény, treba sa dohodnúť, kto ju robí.
- Ohrada zatiaľ nemá bránku.
- VS Code hlási, že `.csproj` je „in unsupported format“: v Unity nastavte `Edit > Preferences > External Tools > External Script Editor` na Visual Studio Code a kliknite `Regenerate project files`. Vo VS Code treba mať rozšírenie **Unity** od Microsoftu.
