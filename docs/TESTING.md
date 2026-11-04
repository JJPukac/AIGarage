# Testovanie AI Garage

Testy majú tri oddelené úrovne: automatické kontroly logiky, zostavenie aplikácie
a praktické overenie telefónu/hardvéru. Výsledok každej úrovne zaznamenávajte
samostatne. Zelené automatické testy nepotvrdzujú bezpečné elektrické zapojenie.

## Predpoklady

- .NET SDK 10; pre zostavy mobilu príslušné MAUI/Android/Windows workloads.
- NuGet balíčky z projektového `NuGet.Config`; prvé obnovenie potrebuje internet.
- Pre firmware PlatformIO podľa [firmware návodu](../firmware/README.md).
- Pre manuálne testy vlastný broker, testovacie ESP32 a Pixel so silnou biometriou.
- Testovať najprv na oddelenom relé, až po úspešných kontrolách na pohone.

Všetky nasledujúce príkazy okrem sekcie firmware sa spúšťajú z koreňa repozitára,
kde je `AIGarage.slnx`. Skript vie koreň určiť aj pri spustení z iného priečinka.

## 1. Automatické kontroly

```powershell
.\scripts\Test.ps1
```

Skript zastaví vykonávanie pri prvom zlyhaní. Počet kontrol v kroku A2:

| Projekt | Počet | Overovaný rozsah |
|---|---:|---|
| `AIGarage.Core.Tests` | 98 | Pôvodných 77 kontrol protokolu a konfigurácie + 21 kontrol profilov, katalógu, funkcií a limitov. |
| `AIGarage.Mobile.Tests` | 84 | Pôvodných 25 kontrol MQTT + 27 ViewModelu, 16 kontrol úložiska a 16 scenárov prepínania/funkcií. |

Samostatné spustenie:

```powershell
dotnet run --project AIGarage.Core.Tests -c Release
dotnet run --project AIGarage.Mobile.Tests -c Release
```

Tieto projekty sú spustiteľné kontrolné programy s návratovým kódom 0 pri úspechu.
Nie sú zaregistrované vo Visual Studio Test Explorer. Spustenie solution s
mobilom ako startup projektom tieto kontroly nevykonáva.

Po predchádzajúcom obnovení závislostí možno použiť `-NoRestore`:

```powershell
.\scripts\Test.ps1 -NoRestore
```

Mobilné kontroly kompilujú rovnaký zdroj `GarageMqttService`, `DeviceProfileStore`
a `MainViewModel` ako aplikácia. Bežné testy používajú riadeného MQTT klienta,
testovacie úložisko a falošnú biometriu. Nepotvrdzujú funkčnosť Android
SecureStorage ani reálny biometrický dialóg.

Kontroly úložiska používajú skutočnú implementáciu `DeviceProfileStore`
s náhradou platformových Preferences/SecureStorage. Overujú oddelenie
credentials, nezhodu verejných a šifrovaných metadát, čiastočné zápisy, migráciu
aj zachovanie ostatných profilov pri odstránení. Scenáre A → B → A používajú
skutočný MQTT servis s riadeným klientom a overujú replay aj po návrate.

Jedna TLS kontrola používa skutočný MQTTnet klient a dočasný lokálny server
na loopback adrese s nedôveryhodným certifikátom. Musí dostať chybu overenia
certifikátu. Neinštaluje certifikát do úložiska dôveryhodných certifikátov a
nepripája sa na vlastný cloudový broker používateľa.

## 2. Zostavenie aplikácií

```powershell
.\scripts\Test.ps1 -BuildWindows -BuildAndroid
```

Alternatívne samostatne:

```powershell
dotnet build AIGarage.Mobile -f net10.0-windows10.0.19041.0
dotnet build AIGarage.Mobile -f net10.0-android
```

Sledujte chyby aj upozornenia. Android build alebo vytvorený APK ešte neznamená,
že aplikácia bola nainštalovaná či spustená na Pixeli. iOS/MacCatalyst zostávajú
šablónovými cieľmi; nie sú pokryté týmto testovacím plánom.

## 3. Firmware bez nahrávania do zariadenia

Z priečinka `firmware` použite postup v jeho README na PlatformIO build a C++
kontroly. Súbor `tests/GarageSafetyTests.cpp` overuje 26 podmienok v piatich
compile-time scenároch: debounce, replay/čas vrátane rollbacku, dĺžku impulzu,
blokáciu po neúspešnom pohybe, reconnect, návrat a pretečenie počítadla času.

Kroky A1 ani A2 firmware nemenia. Pred nasadením zostavte presne tú verziu, ktorú budete
nahrávať. Nezamieňajte úspešnú kompiláciu s meraním skutočného GPIO impulzu.

## 4. Manuálna kontrola obrazovky a telefónu

Základná konfigurácia a označenie tlačidiel sú v [používateľskom návode](USER_GUIDE.md).
Pre každý riadok zaznamenajte dátum, commit, zariadenie/OS a výsledok.

| ID | Scenár | Očakávaný výsledok |
|---|---|---|
| UI-01 | Spustiť Windows bez údajov. | Neznámy stav, pomlčky pri meraniach, otvorenie nedostupné. |
| UI-02 | Zadať neplatný port. | Zrozumiteľná chyba, žiadny pokus o spojenie, formulár sa odblokuje. |
| UI-03 | Prihlásiť sa správnym účtom. | Pripojenie, vyprázdnené tajné polia, čakanie na platnú telemetriu. |
| UI-04 | Priviesť aktuálnu podpísanú telemetriu. | Správna poloha a jednotky; na Windows zostáva otvorenie blokované. |
| UI-05 | Zastaviť telemetriu na viac než 5 sekúnd. | Neznámy stav, skryté staré merania, otvorenie blokované. |
| UI-06 | Zamietnuť biometrické prihlásenie. | Žiadne pripojenie uloženými údajmi. |
| UI-07 | Odpojiť a potom sa biometricky prihlásiť. | Spojenie sa obnoví iba po úspešnej biometrii; čaká sa na nové údaje. |
| UI-08 | Prepnúť aplikáciu do pozadia počas biometrie otvorenia. | Žiadny impulz, relácia ostane uzamknutá aj po neskoršom výsledku dialógu. |
| UI-09 | Prepnúť do pozadia počas prihlasovania. | Dokončenie pôvodnej operácie nesmie obnoviť ovládanie. |
| UI-10 | Opakovane stláčať otvorenie počas rozpracovanej operácie. | Najviac jedno odoslanie z tejto operácie, formulár blokovaný. |
| UI-11 | Zrušiť odstránenie údajov. | Údaje zostanú uložené. |
| UI-12 | Potvrdiť odstránenie údajov. | Relácia uzamknutá, ďalšie prihlásenie vyžaduje nové údaje. |
| UI-13 | Uložiť dva vlastné testovacie profily s odlišnými témami/kľúčmi. | Dva názvy v zozname; údaje každého sa načítajú iba pre jeho profil. |
| UI-14 | Pri pripojení A vybrať B a potvrdiť načítanie. | Už od výberu je otvorenie blokované; načítanie odpojí A, vymaže tajné polia a vyžaduje nové prihlásenie B. |
| UI-15 | Vypnúť otváranie profilu, uložiť a priviesť čerstvé `closed`. | Telemetria sa zobrazuje, tlačidlo otvorenia je skryté a žiadny príkaz nevznikne. |
| UI-16 | Prejsť z A1 s uloženými údajmi na A2. | Garáž 1 sa zobrazí bez čítania credentials; po overení endpointu a biometrickom prihlásení sa migrácia dokončí. |
| UI-17 | Odstrániť profil A pri existujúcom B. | Potvrdenie menuje A; B a jeho biometrické prihlásenie fungujú ďalej. |
| UI-18 | Zadať duplicitný názov alebo endpoint/tému. | Uloženie sa odmietne; pôvodné profily zostanú zachované. |
| UI-19 | Počas prepínania profilu poslať aplikáciu do pozadia. | Neobnoví sa prihlásenie, neodošle sa otvorenie, tajné polia ostanú prázdne. |

## 5. Broker, relé a pohon

Testovať pod dohľadom osoby, ktorá môže zastaviť pohon. Najprv použiť oddelené
relé a merať výstup, bez pripojenej brány. Protokolový testovací nástroj musí
používať iba vlastný testovací broker a vlastné kľúče.

| ID | Scenár | Očakávaný výsledok |
|---|---|---|
| HW-01 | Zapnutie/reset ESP32. | Relé sa samovoľne nezopne vrátane času pred štartom firmware. |
| HW-02 | Platný príkaz pri stabilnom zatvorenom vstupe. | Jeden približne 500 ms impulz; potvrdenie až po vypnutí výstupu. |
| HW-03 | Reed hlási inú než zatvorenú polohu. | Žiadny impulz. |
| HW-04 | Zmenený podpis, cudzí HMAC alebo stará relácia. | Žiadny impulz. |
| HW-05 | Opakovať rovnaký podpísaný príkaz. | Žiadny ďalší impulz. |
| HW-06 | Expirácia a následný návrat času späť. | Zamietnutý príkaz sa nesmie oživiť. |
| HW-07 | MQTT reconnect so starým uloženým príkazom. | Nová relácia, starý príkaz sa nevykoná. |
| HW-08 | Výpadok Wi-Fi/brokera počas impulzu. | Impulz sa ukončí; mobil oznámi neznámy výsledok, bez automatického opakovania. |
| HW-09 | Po impulze reed vôbec neopustí zatvorenú polohu. | Ďalší impulz blokovaný aj po reconnecte. |
| HW-10 | Odchod/návrat reed senzora a cooldown. | Opätovné povolenie až po splnení všetkých interlockov. |
| HW-11 | Odpojiť BME280 alebo reed vodič. | Chýbajúce merania; prerušený reed blokuje otvorenie. Skrat reed obvodu nie je týmto zapojením rozlíšiteľný. |
| HW-12 | Neplatný certifikát/hostname brokera. | Spojenie odmietnuté, bez bezpečnostného bypassu. |

Po skúške oddeleného relé zopakovať relevantné scenáre na skutočnom pohone
a overiť jeho bezpečnostné prvky. Potvrdenie `pulsed` nie je potvrdenie úplne
otvorenej polohy. Testy nemenia bezpečnostnú kategóriu pôvodného pohonu.

## 6. Záznam výsledkov a pravidlo dokončenia kroku

Príklad záznamu bez tajných údajov:

```text
Dátum / commit:
SDK / OS / telefón / doska / firmware:
Automatické kontroly: Core __/98, Mobile __/84
Windows build: PASS / FAIL / NOT RUN
Android build: PASS / FAIL / NOT RUN
Firmware build: PASS / FAIL / NOT RUN
Manuálne scenáre UI/HW: ID, výsledok, pozorovanie
Zostávajúce chyby a neoverené oblasti:
```

Pred commitom spustiť príslušné kontroly a `git diff --check`. Pri zmenách
obrazovky zostaviť oba podporované ciele a zaznamenať, či prebehla aj vizuálna
kontrola. Zlyhaný alebo nevykonaný manuálny test sa nesmie označiť ako úspešný
na základe testu s falošnou službou.
