# eFaktúra Viewer

Samostatny prezerac elektronickych faktur **Peppol BIS Billing 3.0** pre Windows 10 (2004+) / Windows 11 x64. C# / WPF, sedo-oranzova svetla a tmava tema.

**[Stiahnut eFakturaViewer.exe](https://github.com/enthilza/eFaktura-Viewer/releases/latest/download/eFakturaViewer.exe)** · [Vydania](https://github.com/enthilza/eFaktura-Viewer/releases)

## Pouzitie

Spustite EXE a pretiahnite XML do okna, pouzite Ctrl+O alebo odovzdajte cestu ako argument:

```bat
eFakturaViewer.exe "C:\Faktury\faktura.xml"
```

- Predvoleny nahlad sa vzdy generuje z XML. Ak dokument obsahuje PDF prilohy, v ponuke vyberte konkretnu prilohu. Aktualny zdroj je oznaceny v zahlavi.
- **BIS3 · ZAKLADNY** / **BIS3 · ROZSIRENY FMCG** oznacuje rozpoznany obsah. FMCG sa rozpoznava podla znamych AdditionalItemProperty; nejde o formalnu validaciu profilu.
- **Sipky vlavo/vpravo** prechadzaju XML v rovnakom priecinku, prirodzene zoradene podla nazvu. Na konci sa pokracuje od zaciatku. Poradie mozno obratit v ponuke.
- Pri **jedinom XML** sa spodny panel skryje. Ostane dokument, oznacenie profilu a zatvaracie tlacidlo. Nastroje su dostupne cez **prave tlacidlo mysi**.
- **Koliesko** posuva strany, **Ctrl + koliesko** meni priblizenie. Tlacidlo ↔ prisposobi dokument sirke.
- **Ctrl+S** ulozi zobrazene PDF, **Ctrl+P** otvori tlac, **Esc** zavrie aplikaciu.
- Tlacidlo ☼ prepne temu. Tlacidlo ⟳ skontroluje aktualizacie. Dalsie funkcie su pod ⋯ a v kontextovej ponuke.
- Pri inom XML sa zobrazi **„Toto nie je eFaktúra.“** a rozbalovaci strom prvkov a atributov. Sipky v aktivnom strome ovladaju strom; na dalsi subor pouzite spodne tlacidla alebo kontextovu ponuku.
- Poskodene XML, DTD, neplatna priloha alebo nedostupny subor zobrazi chybu; ostatne subory mozno dalej prezerat.

## Otvaranie dvojklikom

V kontextovej ponuke zvolte **Otvarat XML v tejto aplikacii…**. Aplikacia sa zaregistruje do ponuky „Otvorit v programe“ a otvori nastavenia Windows. Tam ju mozete nastavit pre `.xml`, pripadne pouzite prave tlacidlo na XML → Otvorit v programe → Vybrat inu aplikaciu.

Vyber predvolenej aplikacie potvrdi pouzivatel. Program neobchadza Windows UserChoice. Asociacia sa vztahuje na vsetky XML; ne-faktury sa zobrazia ako strom. Po premiestneni EXE registraciu zopakujte.

## Samostatny EXE a zostavenie

Na cielovom PC netreba instalovat .NET, Python, prehliadac ani PDF aplikaciu. PDF zobrazuje komponent Windows. Niektore vnutorne kniznice sa pri starte automaticky rozbalia do systemovej cache .NET.

Na zostavenie treba Windows x64, .NET 10 SDK a pri prvom zostaveni internet na obnovenie NuGet balikov:

```bat
build.cmd -NoPause -Verify
```

Vznikne `dist\eFakturaViewer.exe`. Skript odstrani vlastny docasny adresar `.build`; nemaze zdroje ani spolocnu NuGet cache. `-Verify` ponecha protokol a synteticke overovacie dokumenty v `test-results`. Bez tohto prepinaca sa testovacie vystupy nevytvaraju.

## Aktualizacie a vydavanie

Pri starte prebehne automaticka kontrola novej verzie; mozno ju vypnut v ponuke. Instalacia sa spusti po potvrdeni pouzivatela cez **Aktualizovat**. Nefunkcny internet neblokuje prezeranie.

Vydania obsahuju EXE, podpisany `update.json` a `update.sig`. Overuje sa RSA/SHA-256 podpis manifestu aj EXE; **samostatny kontrolny sucet ani fingerprint EXE sa nezverejnuje**. Verejny overovaci kluc je zabudovany v aplikacii a je sucastou zdrojoveho kodu. Podpis aktualizacie nie je Authenticode podpis Windows EXE.

Aktualizator pocka na ukoncenie okna, vymeni EXE, overi start a otvori povodny XML. Pri zlyhani kontroly startu obnovi zalohu. Umiestnenie EXE musi povolovat zapis. Subory aktualizacie a pripadna chyba su v `%LOCALAPPDATA%\eFakturaViewer`.

Pre vydavatela:

1. Zvysit `Version` v `src/eFakturaViewer/eFakturaViewer.csproj` a pripravit `release-notes.md`.
2. Spustit `build.cmd -NoPause -Verify` a skontrolovat vysledok vratane preskocenych kontrol.
3. Spustit `powershell -NoProfile -File scripts\prepare-release.ps1`.
4. Overit balik: `eFakturaViewer.exe --verify-package "C:\cesta\k\dist"` (kod 0 = uspech).
5. Commitnut a pushnut zdroje, potom spustit `scripts\publish-release.ps1`.

Sukromny kluc je **mimo projektu** v `%LOCALAPPDATA%\eFakturaViewerPublisher\signing-key.dpapi`, chraneny DPAPI aktualneho pouzivatela Windows. Nie je v repozitari ani vydani. Nemenit ho pri beznom vydani: nainstalovane aplikacie doveruju povodnemu verejnemu klucu. Samotny DPAPI subor nie je prenosna zaloha medzi pocitacmi. `-InitializeKey` je len pre prve zalozenie identity vydavatela.

## Rozsah a ochrana dokumentov

Podporovane su Invoice a CreditNote, polozky, zakladne mnozstvo ceny, DPH, zlavy/prirazky, platby a 38 znamych FMCG vlastnosti. Nezname vlastnosti zostavaju zobrazene. Dalsie XML udaje mozno prezriet v strome; PDF nie je potvrdenim platnosti BIS ani zhody s vlozenou prilohou.

Vstupne subory sa iba citaju; nie je tu evidencia, import do databazy ani uhrady. PDF sa vytvara v pamati. Faktury sa nikam neodosielaju. Internet sa pouziva iba na aktualizacie z GitHubu. XML parser zakazuje DTD a externe entity; externe prilohy sa automaticky nestahuju.

Prevadzkove limity prvej verzie: XML 30 MB, 2000 poloziek, PDF 1000 stran. Heslovane alebo necitatelne PDF ohlasi chybu; generovany nahlad zostava dostupny. Pri tlaci viac ako 100 stran ulozte PDF a pouzite PDF aplikaciu. Limity nie su limitmi BIS standardu.

## Overenie

Kontroly overuju zakladny BIS, FMCG, dobropis, nemenene vstupy, namespace prefixy, DTD/XXE vratane UTF-16, poskodene XML/PDF/base64, viac priloh, predvoleny generovany nahlad, 2000 poloziek, dlhy text, podpisy a rychle listovanie. Overuju aj skrytie panelu pri jednom XML a oznacenie profilu.

V odpojenej relacii Windows nemoze WPF vykreslit ani nezavislu graficku sondu. Vizualne kontroly okna su vtedy oznacene ako **preskocene**, nie uspesne. PDF je mozne vykreslit a overit nezavisle. Rucne overenie fyzickej tlaciarne, roznych DPI/monitorov a vypadkov konkretneho sietoveho disku zostava na cielovom PC.

## Zdrojovy kod

`src/eFakturaViewer`: XML parser, tvorba PDF, WPF prezerac, aktualizator a integrovane kontroly. `scripts`: build, ikona, podpisovanie a publikovanie. `examples`: iba synteticke doklady.

Sukromne testovacie faktury, povodne zdroje cudzieho projektu a originalna dodavatelska specifikacia sa nepublikuju. Verejny repozitar obsahuje cely zdrojovy kod tejto aplikacie. Licencie zavislosti: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
