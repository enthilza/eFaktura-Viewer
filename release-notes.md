# eFaktura Viewer 1.0.4

Opravy funkčných chýb z nezávislého auditu a pohodlnejšie zobrazenie faktúr.

- Jednotkové ceny zachovávajú všetky desatinné miesta. Dátum faktúry sa neposúva podľa časového pásma.
- Náhľad obsahuje aj obchodný názov partnera. Dlhé texty a adresy sa zalamujú na ďalšie riadky a strany; IČO a IČ DPH majú jednoznačné popisy.
- Položky tvoria jednu súvislú tabuľku so základnými údajmi. Doplnkové výpisy pod každou položkou boli odstránené; úplné údaje zostávajú v strome XML.
- Rýchle listovanie a približovanie už nestrácajú požiadavku na vykreslenie. XML sa otvorí aj vtedy, keď nie je možné načítať zoznam súborov v priečinku.
- Okno rešpektuje panel úloh: jeho spodný okraj je 2 pixely nad spodnou hranou pracovnej plochy. Zachováva pomer A4 a výšku najviac 97 % obrazovky.
- Veľkosť okna možno meniť potiahnutím za ktorýkoľvek okraj alebo roh. Kurzor označuje smer zmeny veľkosti.
- Texty aplikácie, dialógov a popisy vo vygenerovanom PDF sú v slovenčine s diakritikou. Údaje prevzaté z faktúr sa nemenia.
- Ručná kontrola aktualizácie funguje aj počas pomalej kontroly na pozadí. Upozornenie na novú verziu zostáva viditeľné pri listovaní.
- Aktualizátor ukončí zamrznutú štartovaciu sondu pred obnovou zálohy. Zamknutá stará verzia nevráti úspešnú aktualizáciu späť; nepotrebné pomocné súbory sa upracú pri ďalšom spustení.
- Overuje sa aj zhoda verzie EXE s podpísaným manifestom. Testy používajú vlastné nastavenia a nemenia uloženú schému používateľa.
- Tlač pripravuje strany postupne. PDF nad limitom 1 000 strán zostáva dostupné na uloženie; údaje možno prezerať v strome XML.

Predvolená zostáva svetlá schéma, uložená voľba používateľa má prednosť. Vydanie obsahuje samostatný EXE pre Windows x64 a podpísané podklady pre automatickú aktualizáciu.
