# eFaktura Viewer 1.0.0

Prve verejne vydanie pre Windows 10/11 x64.

- Otvaranie XML argumentom, pretiahnutim alebo cez Ctrl+O; volitelna asociacia Windows.
- BIS3, FMCG a dobropisy; generovany viacstranovy nahlad a vyber vlozenych PDF.
- Viditelny profil zakladny/rozsireny, svetla a tmava sedo-oranzova tema.
- Listovanie susednymi XML, skryty panel pri jedinom subore.
- Stromovy pohlad a oznam „Toto nie je eFaktúra.“ pre ine XML.
- Zoom, export PDF, tlac, automaticka kontrola aktualizacii a manualne tlacidlo.
- Aktualizacie overuju digitalny podpis EXE a manifestu; nevydavame samostatny checksum subor. GitHub automaticky zobrazuje vlastny digest kazdeho nahrateho suboru.

Automaticke kontroly su uspesne. Vizualna kontrola WPF okna je v aktualnej odpojenej relacii Windows preskocena; PDF nahlady boli overene samostatne. Tlac na fyzickej tlaciarni, rozne DPI a konkretne sietove disky vyzaduju rucne overenie.

Stiahnite `eFakturaViewer.exe`. Nie je potrebna instalacia .NET ani Pythonu.
