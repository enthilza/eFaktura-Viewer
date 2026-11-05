# eFaktura Viewer 1.1.2

Aktualizácie lepšie zvládajú dočasné výpadky spojenia a servera.

- Pri chybe HTTP 504 a podobných dočasných chybách sa sťahovanie automaticky opakuje, celkovo najviac tri pokusy s krátkymi prestávkami.
- Prerušený súbor sa stiahne od začiatku. Overenie veľkosti, digitálneho podpisu a verzie zostáva povinné.
- Ak problém pretrvá, zobrazí sa zrozumiteľné slovenské hlásenie. Po odstránení ponúknutého vydania sa pri ďalšom pokuse znova vyhľadá aktuálna verzia.
- Obnova predchádzajúcej verzie počká na krátkodobé uvoľnenie zamknutého súboru.

Vydanie obsahuje samostatný EXE pre Windows x64 a podpísané podklady pre automatickú aktualizáciu.
