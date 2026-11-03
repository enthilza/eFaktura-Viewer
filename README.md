# eFaktúra Viewer

Samostatny prezerac elektronickych faktur **Peppol BIS Billing 3.0** pre Windows 10 (2004+) / Windows 11 x64. C# / WPF, svetla a tmava tema v odtienoch sivej. Predvolena je svetla schema; vlastny vyber temy sa zapamata. Dizajn podla aplikacie Luma: Segoe UI Variable, zaoblene okna a ovladacie prvky, jemne obrysy a priehladne pozadie s priblizne 95 % krytim. Papier faktury zostava nepriehladny.

**[Stiahnut eFakturaViewer.exe](https://github.com/enthilza/eFaktura-Viewer/releases/latest/download/eFakturaViewer.exe)** · [Vydania](https://github.com/enthilza/eFaktura-Viewer/releases)

## Pouzitie

Spustite EXE a pretiahnite XML do okna, pouzite Ctrl+O alebo odovzdajte cestu ako argument:

```bat
eFakturaViewer.exe "C:\Faktury\faktura.xml"
```

- Predvoleny nahlad sa vzdy generuje z XML a prisposobi vyske. Prepinac **Vlozene PDF** zobrazi prilohu; pri viacerych prilohach sa objavi vyber konkretneho PDF. Aktualny zdroj je oznaceny v zahlavi.
- **BIS3 · ZAKLADNY** / **BIS3 · ROZSIRENY FMCG** oznacuje rozpoznany obsah. FMCG sa rozpoznava podla znamych AdditionalItemProperty; nejde o formalnu validaciu profilu.
- **Sipky vlavo/vpravo** prechadzaju XML v rovnakom priecinku, prirodzene zoradene podla nazvu. Na konci sa pokracuje od zaciatku. Poradie mozno obratit v ponuke.
- Pri **jedinom XML** sa spodny panel skryje. Horne ovladanie prisposobenia vyske/sirke, temy, aktualizacie a priloh zostava dostupne.
- **Koliesko** posuva strany, **Ctrl + koliesko** meni priblizenie. Horne tlacidla **Prisposobit vyske** a **Prisposobit sirke** prepnu sposob zobrazenia.
- Kazde nove okno sa otvori na monitore pod kurzorom, vodorovne v strede pracovnej plochy. Spodny okraj je 2 fyzicke pixely nad jej spodnou hranou, teda nad taskbarom umiestnenym dole. Vyska je najviac 97 % monitora, pomer stran A4; dostupna plocha bez taskbaru moze okno zmensit. Okno mozno presuvat za zahlavie a menit jeho velkost tahanim za ktorukolvek hranu alebo roh.
- Texty rozhrania, dialogov, hlasenia a vlastne popisy v PDF pouzivaju spisovnu slovencinu s diakritikou. Obsah faktur a nezname XML oznacenia zostavaju povodne.
- **Ctrl+S** ulozi zobrazene PDF, **Ctrl+P** otvori tlac, **Esc** zavrie aplikaciu.
- Horne tlacidla **Prepnut schemu** a **Aktualizovat aplikaciu** prepinaju temu a kontroluju aktualizacie. Dalsie funkcie su pod ⋯ a v kontextovej ponuke. Vlastne oznamy, potvrdenia a priebeh aktualizacie pouzivaju rovnaky zaobleny dizajn; systemovy vyber suboru a tlaciarne zabezpecuje Windows.
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

Self-test pouziva samostatny docasny profil v `test-results`; nemeni nastavenia ani ulozenu schemu pouzivatela.

## Aktualizacie a vydavanie

Pri starte prebehne automaticka kontrola novej verzie; mozno ju vypnut v ponuke. Instalacia sa spusti po potvrdeni pouzivatela cez **Aktualizovat**. Nefunkcny internet neblokuje prezeranie.

Kontrola na pozadi ma limit 15 sekund. Rucna kontrola ju zrusi a zobrazi vlastny priebeh. Bodka pri hornom tlacidle aktualizacie a jeho popis oznamuju dostupnu verziu aj po prepnuti faktury.

Vydania obsahuju EXE, podpisany `update.json` a `update.sig`. Overuje sa RSA/SHA-256 podpis manifestu aj EXE; **samostatny kontrolny sucet ani fingerprint EXE sa nezverejnuje**. Verejny overovaci kluc je zabudovany v aplikacii a je sucastou zdrojoveho kodu. Podpis aktualizacie nie je Authenticode podpis Windows EXE.

Samotny GitHub vsak od 03.06.2025 automaticky vytvara a zobrazuje nemenny SHA-256 digest kazdeho release suboru. Tuto platformovu vlastnost nasa aplikacia neriadi. Nie je to sukromny kluc ani podpisovacie tajomstvo. [Oznam GitHubu](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/)

Aktualizator pocka na ukoncenie okna, vymeni EXE, overi start a otvori povodny XML. Pri zlyhani kontroly startu obnovi zalohu. Umiestnenie EXE musi povolovat zapis. Subory aktualizacie a pripadna chyba su v `%LOCALAPPDATA%\eFakturaViewer`.

Pre vydavatela:

1. Zvysit `Version` v `src/eFakturaViewer/eFakturaViewer.csproj` a pripravit `release-notes.md`.
2. Spustit `build.cmd -NoPause -Verify` a skontrolovat vysledok vratane preskocenych kontrol.
3. Spustit `powershell -NoProfile -File scripts\prepare-release.ps1`.
4. Overit balik: `eFakturaViewer.exe --verify-package "C:\cesta\k\dist"` (kod 0 = uspech).
5. Commitnut a pushnut zdroje, potom spustit `scripts\publish-release.ps1`.

Podpisovanie aj klient overuju zhodu verzie EXE s manifestom. Publikacny skript stiahne nove vydanie, overi podpis aj start a az potom odstrani starsie releases s ich prilohami. Na GitHube zostava najnovsi release; zdrojova historia a tagy sa nemazu.

Sukromny kluc je **mimo projektu** v `%LOCALAPPDATA%\eFakturaViewerPublisher\signing-key.dpapi`, chraneny DPAPI aktualneho pouzivatela Windows. Nie je v repozitari ani vydani. Nemenit ho pri beznom vydani: nainstalovane aplikacie doveruju povodnemu verejnemu klucu. Samotny DPAPI subor nie je prenosna zaloha medzi pocitacmi. `-InitializeKey` je len pre prve zalozenie identity vydavatela.

## Rozsah a ochrana dokumentov

Podporovane su Invoice a CreditNote, polozky, zakladne mnozstvo ceny, DPH, zlavy/prirazky a platby. Profil FMCG sa rozpoznava podla 38 znamych vlastnosti. Polozky tvoria jednu suvislu tabulku zakladnych udajov; technicke/FMCG doplnky pod polozkami sa nevypisuju. Vsetky udaje vratane neznamych vlastnosti zostavaju v XML strome. Dalsie XML udaje mozno prezriet v strome; PDF nie je potvrdenim platnosti BIS ani zhody s vlozenou prilohou.

Vstupne subory sa iba citaju; nie je tu evidencia, import do databazy ani uhrady. PDF sa vytvara v pamati. Faktury sa nikam neodosielaju. Internet sa pouziva iba na aktualizacie z GitHubu. XML parser zakazuje DTD a externe entity; externe prilohy sa automaticky nestahuju.

Prevadzkove limity: XML 30 MB, 2000 poloziek, nahlad PDF 1000 stran. PDF nad limitom sa da ulozit cele a udaje prezriet v XML strome. Heslovana alebo necitatelna priloha ohlasi chybu; prepinacom sa da vratit ku generovanemu nahladu. Tlac pripravuje jednotlive strany na poziadanie; aplikacia neuchovava bitmapy celeho dokumentu naraz. Limity nie su limitmi BIS standardu.

## Overenie

Verzia 1.0.4 obsahuje regresne kontroly v `SelfTest.cs` a `ReviewTests.cs`. Overuju zakladny BIS, FMCG, dobropis, nemenene vstupy, namespace prefixy, DTD/XXE vratane UTF-16, poskodene XML/PDF/base64, viac priloh, presnost cien a datumov, 2000 poloziek, dlhy text, podpisy a rychle listovanie pri obsadenom rendereri. Dalej overuju obnovu po chybe aktualizacie, ukoncenie zamrznutej sondy, zamknutu zalohu, upratovanie, izolaciu nastaveni, rozmery a uchopy okna, obe temy, modalne okna a horne nastroje pri jedinom XML. Aktualny vysledok je v `test-results/self-test.json`.

V odpojenej relacii Windows nemoze WPF vykreslit ani nezavislu graficku sondu. Vizualne kontroly okna su vtedy oznacene ako **preskocene**, nie uspesne. PDF je mozne vykreslit a overit nezavisle. Rucne overenie fyzickej tlaciarne, roznych DPI/monitorov a vypadkov konkretneho sietoveho disku zostava na cielovom PC.

## Zdrojovy kod

`src/eFakturaViewer`: XML parser, tvorba PDF, WPF prezerac, aktualizator a integrovane kontroly. `scripts`: build, ikona, podpisovanie a publikovanie. `examples`: iba synteticke doklady.

Sukromne testovacie faktury, povodne zdroje cudzieho projektu a originalna dodavatelska specifikacia sa nepublikuju. Verejny repozitar obsahuje cely zdrojovy kod tejto aplikacie. Licencie zavislosti: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
