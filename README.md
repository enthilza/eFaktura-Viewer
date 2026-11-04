# eFaktura Viewer

Samostatný prezerač elektronických faktúr **Peppol BIS Billing 3.0** pre Windows 10 (2004+) / Windows 11 x64. C# / WPF, svetlá a tmavá téma v odtieňoch sivej. Predvolená je svetlá schéma; vlastný výber témy sa zapamätá. Dizajn podľa aplikácie Luma: Segoe UI Variable, zaoblené okná a ovládacie prvky, jemné obrysy a priehľadné pozadie s približne 95 % krytím. Papier faktúry zostáva nepriehľadný.

**[Stiahnuť eFakturaViewer.exe](https://github.com/enthilza/eFaktura-Viewer/releases/latest/download/eFakturaViewer.exe)** · [Vydania](https://github.com/enthilza/eFaktura-Viewer/releases)

## Použitie

Spustite EXE a pretiahnite XML do okna, použite Ctrl+O alebo odovzdajte cestu ako argument:

```bat
eFakturaViewer.exe "C:\Faktury\faktura.xml"
```

- Náhľad sa vždy generuje z XML. Pri prvom spustení sa prispôsobí výške; potom sa zachová posledná voľba výšky, šírky alebo ručného priblíženia aj pri prechode na ďalšiu faktúru a po reštarte. Prepínač **Vložené PDF** zobrazí prílohu; pri viacerých prílohách sa objaví výber konkrétneho PDF. Aktuálny zdroj je označený v záhlaví.
- **BIS3 · ZÁKLADNÝ** / **BIS3 · ROZŠÍRENÝ FMCG** označuje rozpoznaný obsah. FMCG sa rozpoznáva podľa známych vlastností `AdditionalItemProperty`; nejde o formálne overenie platnosti profilu.
- **Šípky vľavo/vpravo** prechádzajú XML v rovnakom priečinku, prirodzene zoradené podľa názvu. Na konci sa pokračuje od začiatku. Poradie možno obrátiť v ponuke.
- Pri **jedinom XML** sa spodný panel skryje. Horné ovládanie prispôsobenia výške/šírke, témy, aktualizácie a príloh zostáva dostupné.
- **Koliesko** posúva strany, **Ctrl + koliesko** mení priblíženie. Horné tlačidlá **Prispôsobiť výške** a **Prispôsobiť šírke** prepnú spôsob zobrazenia.
- **Šípky hore/dole** posúvajú zobrazenú faktúru. **Page Up / Page Down** posúvajú obsah o výšku náhľadu, aj keď má fokus tlačidlo v záhlaví. Na kraji dokumentu sa posun zastaví; medzi faktúrami naďalej prepínajú iba šípky vľavo/vpravo. Otvorená ponuka, výber prílohy a strom XML si zachovávajú vlastné ovládanie.
- Okno si pamätá posledný monitor, polohu, veľkosť a maximalizáciu. Pri odpojení monitora alebo zmene mierky Windows sa upraví do dostupnej pracovnej plochy. Minimalizácia pred zatvorením nezmení posledný viditeľný stav. Pri prvom spustení sa použije monitor pod kurzorom, pomer A4, najviac 97 % výšky obrazovky a spodný okraj 2 pixely nad panelom úloh. Okno možno presúvať za záhlavie a meniť jeho veľkosť ťahaním za ktorúkoľvek hranu alebo roh.
- Texty rozhrania, dialógov, hlásenia a vlastné popisy v PDF používajú spisovnú slovenčinu s diakritikou. Obsah faktúr a neznáme označenia XML zostávajú pôvodné.
- **Ctrl+S** uloží zobrazené PDF, **Ctrl+P** otvorí tlač, **Esc** zavrie aplikáciu.
- Horné tlačidlá **Prepnúť schému** a **Aktualizovať aplikáciu** prepínajú tému a kontrolujú aktualizácie. Ďalšie funkcie sú pod ⋯ a v kontextovej ponuke. Vlastné oznamy, potvrdenia a priebeh aktualizácie používajú rovnaký zaoblený dizajn; systémový výber súboru a tlačiarne zabezpečuje Windows.
- Pri inom XML sa zobrazí **„Toto nie je eFaktúra.“** a rozbaľovací strom prvkov a atribútov. Šípky v aktívnom strome ovládajú strom; na ďalší súbor použite spodné tlačidlá alebo kontextovú ponuku.
- Pri poškodenom XML, DTD, neplatnej prílohe alebo nedostupnom súbore sa zobrazí chyba; ostatné súbory možno ďalej prezerať.

## Zapamätané nastavenia

Poloha a veľkosť okna, maximalizácia, prispôsobenie výške/šírke, ručné priblíženie, farebná schéma, poradie súborov, posledný priečinok a kontrola aktualizácií sa ukladajú pre aktuálneho používateľa do `HKEY_CURRENT_USER\Software\eFakturaViewer`, do hodnoty `Settings`. Platia aj po premiestnení alebo aktualizácii EXE na tom istom počítači. Každý používateľ Windows má vlastné nastavenia.

Nastavenia zo starších verzií v `%LOCALAPPDATA%\eFakturaViewer\settings.json` sa pri prvom uložení prevezmú do registra. Pôvodný súbor sa nemení; po prevzatí má register prednosť. Viac otvorených okien ukladá iba zmenené voľby, aby si navzájom neprepisovali ostatné nastavenia. Testy používajú samostatnú dočasnú vetvu registra.

## Otváranie dvojklikom

V kontextovej ponuke zvoľte **Otvárať XML v tejto aplikácii…**. Aplikácia sa zaregistruje do ponuky „Otvoriť v programe“ a otvorí priamo systémový výber predvolenej aplikácie pre **`.xml`**. Vyberte eFaktura Viewer a potvrďte voľbu; príponu už netreba vyhľadávať. Ak daný Windows priamy výber neposkytne, aplikácia zobrazí pokyny a otvorí všeobecné nastavenia predvolených aplikácií. Alternatívne použite pravé tlačidlo na XML → Otvoriť v programe → Vybrať inú aplikáciu.

Výber predvolenej aplikácie potvrdí používateľ. Program neobchádza Windows UserChoice. Priradenie aplikácie sa vzťahuje na všetky XML; dokumenty, ktoré nie sú faktúrami, sa zobrazia ako strom. Po premiestnení EXE registráciu zopakujte.

## Samostatný EXE a zostavenie

Na cieľovom PC netreba inštalovať .NET, Python, prehliadač ani aplikáciu na PDF. PDF zobrazuje komponent Windows. Niektoré vnútorné knižnice sa pri štarte automaticky rozbalia do systémovej vyrovnávacej pamäte .NET.

Na zostavenie treba Windows x64, .NET 10 SDK a pri prvom zostavení internet na obnovenie balíkov NuGet:

```bat
build.cmd -NoPause -Verify
```

Vznikne `dist\eFakturaViewer.exe`. Skript odstráni vlastný dočasný adresár `.build`; nemaže zdroje ani spoločnú vyrovnávaciu pamäť NuGet. `-Verify` ponechá protokol a syntetické overovacie dokumenty v `test-results`. Bez tohto prepínača sa testovacie výstupy nevytvárajú.

Vlastný test používa samostatný dočasný profil v `test-results`; nemení nastavenia ani uloženú schému používateľa.

## Aktualizácie a vydávanie

Pri štarte prebehne automatická kontrola novej verzie; možno ju vypnúť v ponuke. Inštalácia sa spustí po potvrdení používateľa cez **Aktualizovať**. Nefunkčný internet neblokuje prezeranie.

Kontrola na pozadí má limit 15 sekúnd. Ručná kontrola ju zruší a zobrazí vlastný priebeh. Bodka pri hornom tlačidle aktualizácie a jeho popis oznamujú dostupnú verziu aj po prepnutí faktúry.

Vydania obsahujú EXE, podpísaný `update.json` a `update.sig`. Overuje sa podpis RSA/SHA-256 manifestu aj EXE; **samostatný kontrolný súčet ani odtlačok EXE sa nezverejňuje**. Verejný overovací kľúč je zabudovaný v aplikácii a je súčasťou zdrojového kódu. Podpis aktualizácie nie je podpisom Authenticode súboru Windows EXE.

Samotný GitHub však od 03.06.2025 automaticky vytvára a zobrazuje nemenný odtlačok SHA-256 každého súboru vydania. Túto vlastnosť platformy naša aplikácia neriadi. Nie je to súkromný kľúč ani podpisovacie tajomstvo. [Oznam GitHubu](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/)

Aktualizátor počká na ukončenie okna, vymení EXE, overí štart a otvorí pôvodný XML. Pri zlyhaní kontroly štartu obnoví zálohu. Umiestnenie EXE musí povoľovať zápis. Súbory aktualizácie a prípadný záznam chyby sú v `%LOCALAPPDATA%\eFakturaViewer`.

Pre vydavateľa:

1. Zvýšiť `Version` v `src/eFakturaViewer/eFakturaViewer.csproj` a pripraviť `release-notes.md`.
2. Spustiť `build.cmd -NoPause -Verify` a skontrolovať výsledok vrátane preskočených kontrol.
3. Spustiť `powershell -NoProfile -File scripts\prepare-release.ps1`.
4. Overiť balík: `eFakturaViewer.exe --verify-package "C:\cesta\k\dist"` (kód 0 = úspech).
5. Uložiť zmeny do commitu a odoslať zdroje na GitHub, potom spustiť `scripts\publish-release.ps1`.

Podpisovanie aj klient overujú zhodu verzie EXE s manifestom. Publikačný skript stiahne nové vydanie, overí podpis aj štart a až potom odstráni staršie vydania s ich prílohami. Na GitHube zostáva najnovšie vydanie; história zdrojov a značky verzií sa nemažú.

Súkromný kľúč je **mimo projektu** v `%LOCALAPPDATA%\eFakturaViewerPublisher\signing-key.dpapi`, chránený DPAPI aktuálneho používateľa Windows. Nie je v repozitári ani vydaní. Pri bežnom vydaní ho nemeňte: nainštalované aplikácie dôverujú pôvodnému verejnému kľúču. Samotný súbor DPAPI nie je prenosnou zálohou medzi počítačmi. `-InitializeKey` slúži len na prvé založenie identity vydavateľa.

## Rozsah a ochrana dokumentov

Podporované sú Invoice a CreditNote, položky, základné množstvo ceny, DPH, zľavy/prirážky a platby. Profil FMCG sa rozpoznáva podľa 38 známych vlastností. Položky tvoria jednu súvislú tabuľku základných údajov; technické doplnky a údaje FMCG sa pod položkami nevypisujú. Všetky údaje vrátane neznámych vlastností zostávajú v strome XML. PDF nie je potvrdením platnosti BIS ani zhody s vloženou prílohou.

Vstupné súbory sa iba čítajú; nie je tu evidencia, import do databázy ani úhrady. PDF sa vytvára v pamäti. Faktúry sa nikam neodosielajú. Internet sa používa iba na aktualizácie z GitHubu. Parser XML zakazuje DTD a externé entity; externé prílohy sa automaticky nesťahujú.

Prevádzkové limity: XML 30 MB, 2 000 položiek, náhľad PDF 1 000 strán. PDF nad limitom sa dá uložiť celé a údaje prezrieť v strome XML. Pri prílohe chránenej heslom alebo nečitateľnej prílohe sa zobrazí chyba; prepínačom sa dá vrátiť ku generovanému náhľadu. Tlač pripravuje jednotlivé strany na požiadanie; aplikácia neuchováva bitmapy celého dokumentu naraz. Limity nie sú limitmi štandardu BIS.

## Overenie

Regresné kontroly sú v `SelfTest.cs` a `ReviewTests.cs`. Overujú základný BIS, FMCG, dobropis, nezmenené vstupy, prefixy menných priestorov XML, DTD/XXE vrátane UTF-16, poškodené XML/PDF/base64, viac príloh, presnosť cien a dátumov, 2 000 položiek, dlhý text, podpisy a rýchle listovanie počas vykresľovania. Ďalej overujú obnovu po chybe aktualizácie, ukončenie zamrznutej sondy, zamknutú zálohu, upratovanie, izoláciu nastavení, rozmery a úchopy okna, obe témy, modálne okná a horné nástroje pri jedinom XML. Aktuálny výsledok je v `test-results/self-test.json`.

V odpojenej relácii Windows nemusí WPF vykresliť ani nezávislú grafickú sondu. Vizuálne kontroly okna sú vtedy označené ako **preskočené**, nie úspešné. PDF je možné vykresliť a overiť nezávisle. Ručné overenie fyzickej tlačiarne, monitorov s rôznym DPI a výpadkov konkrétneho sieťového disku zostáva na cieľovom PC.

## Zdrojový kód

`src/eFakturaViewer`: parser XML, tvorba PDF, prezerač WPF, aktualizátor a integrované kontroly. `scripts`: zostavenie, ikona, podpisovanie a publikovanie. `examples`: iba syntetické doklady.

Súkromné testovacie faktúry, pôvodné zdroje cudzieho projektu a originálna dodávateľská špecifikácia sa nepublikujú. Verejný repozitár obsahuje celý zdrojový kód tejto aplikácie. Licencie závislostí: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
