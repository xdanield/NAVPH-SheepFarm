# Baa-Ricade – plán vývoja

Orientačné tasky z GDD podľa priority. Najprv celé P1, aby sa hra dala zahrať od začiatku do konca. P2 a P3 robíme, ak zostane čas.

Kto si berie task, napíše to do skupiny („robím P1-05“). ID tasku dávajte do commitu: `P1-05 zombie AI`.

## Už hotové

- [x] Unity projekt (URP 2D) a scéna `Farm`
- [x] Pohyb hráča cez WASD
- [x] Kamera sleduje hráča
- [x] Základný cyklus deň/noc
- [x] Základné prefaby ovce a zombie

## P1 – Musí byť

Hrateľné jadro hry. Stačí placeholder grafika. Poradie je zhruba také, v akom to dáva zmysel robiť.

- [ ] **P1-01 Mapa** – tilemap: farma s ohradou vpravo, les vľavo, okraje mapy s collidermi - ADI
- [x] **P1-02 Ovce v ohrade** – na štarte 10 oviec, túlajú sa vnútri ohrady
- [x] **P1-03 HP hráča** – 4 srdcia, hryznutie uberie 1 srdce, smrť = game over
- [x] **P1-04 Boj nožom** – ľavý klik, 10 dmg, dosah 0,4 m, cooldown 2 s; zombie má 100 HP
- [x] **P1-05 Zombie AI** – algoritmus, spawn vľavo, pohyb pomaly vo svojej linke, zastavenie o obranu, male nasledovanie hrača a chytanie ovce
- [x] **P1-06 Ohrada s HP** – 200 HP, zombie do nej búchajú po 10
- [ ] **P1-07 Odnášanie oviec** – 2 zombie pri ovci ju odnesú (stačí, že zmiznú)
- [ ] **P1-08 Noc vo vlnách** – 2–3 vlny; 10 zombie, +2 každú noc, +1 za každých 10 oviec nad 10; noc končí, keď zombie zmiznú, nie časovačom
- [ ] **P1-09 Zber surovín** – stromy, kamene, tráva cez E (drevo, kameň, vlákno)
- [ ] **P1-10 Batoh a sklad** – hotbar 6 slotov + inventár na I ako v Core Keeper (hotové), truhla na farme bez limitu (chýba)
- [ ] **P1-11 Stavanie obrany** – Q, mriežka; plot (2 drevo, 20 HP), bodce (2 drevo + 1 kameň, 15 dmg/s); odstránenie
- [ ] **P1-12 Ráno** – každý pár má 60 % šancu na jahňa; zhrnutie prežilo / odnesené / narodené
- [ ] **P1-13 Výhra a prehra** – 100 oviec = výhra; 0 oviec alebo smrť = game over
- [ ] **P1-14 HUD** – srdcia, čas dňa, počet oviec, hotbar
- [ ] **P1-15 Menu a pauza** – New Game, Exit; ESC = pauza

## P2 – Mala by byť

- [ ] **P2-01 Zombie ovca** – odnesená ovca sa ďalšiu noc vráti ako zombie (200 HP)
- [ ] **P2-02 Ovce utekajú** – pred zombie, navzájom sa odtláčajú
- [ ] **P2-03 Varenie** – stanica, 2 ingrediencie, objavené recepty sa uložia, inak Mush; bobule, bylinky, huby
- [ ] **P2-04 Kŕmenie oviec** – Love Potion (+30 % šanca, max 90 %), Thick Stew (treba 3 zombie na odnesenie)
- [ ] **P2-05 Návrat pred nocou** – kto nie je doma, zobudí sa na farme a stratí polovicu batohu
- [ ] **P2-06 Opravy (R)** – plot a ohrada za drevo
- [ ] **P2-07 Obtiažnosť** – zombie +5 % HP a dmg každú noc; skupina odnesie max 3–4 ovce
- [ ] **P2-08 Rozmiestnenie zdrojov** – bežné blízko, vzácne ďaleko; na štarte pár plotov a bodcov
- [ ] **P2-09 Varovanie útoku** – červený výkričník na ľavom okraji
- [ ] **P2-10 Grafika postáv** – hráčka, ovca, zombie, zombie ovca + animácie
- [ ] **P2-11 Tileset** – farma, les, lúky, stromy, kamene
- [ ] **P2-12 Ukladanie** – auto-save ráno, Continue v menu
- [ ] **P2-13 Obrazovka skladu** – presun vecí batoh ↔ truhla
- [ ] **P2-14 Animacia hracoveho utoku** - Pridat animaciu nozika k utoku

## P3 – Ak bude čas

- [ ] **P3-01 Padajúci kmeň** – 3 drevo + 1 vlákno, 50 dmg v rade
- [ ] **P3-02 Pasca na zombie** – 2 šrot, drží zombie 4 s
- [ ] **P3-03 Výbušná pochúťka** – ovca vybuchne pri zombie
- [ ] **P3-04 Vylepšenie ohrady** – elektrický šok, nájdené v lese
- [ ] **P3-05 Opustené stavby** – šrot a strelný prach
- [ ] **P3-06 Noc a svetlá** – tma, svetlo okolo farmy
- [ ] **P3-07 Animácia odnášania oviec**
- [ ] **P3-08 Záverečná scéna** – stádo zaženie hordu
- [ ] **P3-09 Presun a otáčanie obrany**
- [ ] **P3-10 Zvuky a hudba**
- [ ] **P3-11 Options** – hlasitosť, ovládanie, fullscreen
- [ ] **P3-12 Posteľ** – preskočenie dňa
- [ ] **P3-13 Úvod prvej noci** – krátke texty z príbehu
