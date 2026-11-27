# eFaktura Viewer 1.2.0

Nové horné tlačidlo **Premenuj** nahradí automatické názvy XML prehľadnými názvami podľa dodávateľa a dátumov faktúry.

- Formát: `Dodávateľ_2026-11-27_2026-11-26_1.xml`. Ak chýba dátum zdaniteľného plnenia, použije sa `bez-datumu-ZP`.
- Faktúry sa spracujú podľa dátumu a času úpravy, od najstarších. Číslovanie je samostatné pre každého dodávateľa a pokračuje za najvyšším existujúcim číslom.
- Už pomenované faktúry zostávajú zachované. Zakázané znaky sa nahradia pomlčkou; diakritika zostáva.
- Existujúce súbory sa neprepisujú ani pri kolízii vzniknutej počas operácie. Obsah XML a vložených príloh sa nemení.
- Spoločný sieťový priečinok chráni výhradné otvorenie zámkového súboru počas celej dávky. Samotný pozostatok súboru po páde aplikácie ďalšiu dávku neblokuje.
- Otvorená faktúra aj jej priradený cieľ zostávajú zachované pod novým názvom.

Pre zostavovanie zo zdrojov: výsledný EXE je teraz priamo v koreni projektu; dočasné súbory a testy vznikajú mimo Nextcloudu. Existujúce nainštalované aplikácie sa aktualizujú na svojom doterajšom mieste.
