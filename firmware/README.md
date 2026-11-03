# ESP32 — Štádium 1

Firmvér pre ESP32 DevKit, ovládajúci bezpotenciálové relé na tlačidlovom vstupe
pohonu a publikujúci stav reed senzora a hodnoty BME280 cez MQTTS. Firmvér
prijíma výhradne príkaz `open`; potvrdenie `pulsed` znamená ukončený impulz,
nie potvrdenie úplného otvorenia brány.

## Zapojenie

| Komponent | Pin ESP32 | Poznámka |
|---|---|---|
| Relé IN | GPIO 26 | Aktívne LOW, neaktívne HIGH. |
| Reed | GPIO 27 ↔ GND | Kontakt zopnutý iba pri úplne zatvorenej bráne. |
| BME280 SDA | GPIO 21 | I2C, adresa 0x76 alebo 0x77. |
| BME280 SCL | GPIO 22 | I2C. |
| BME280 VCC | 3,3 V | Použiť vhodný modul, spoločná GND. |

Relé musí mať **hardvérový neaktívny bias počas resetu** (napríklad vhodný
pull-up vstupu aktívneho LOW modulu). Kód nemôže zabezpečiť GPIO pred štartom
procesora. Na ESP32 nikdy nepripájať 5 V signály. Relé musí mať bezpotenciálový
kontakt pripojený výlučne na výrobcom určený tlačidlový vstup pohonu. Zachovať
bezpečnostné prvky pohonu; ich vlastnosti tento firmvér nenahrádza.

Jeden reed potvrdzuje iba **zatvorené / nie je zatvorené**. `moving_estimated`
je 30-sekundový odhad po impulze. Na potvrdenie úplného otvorenia treba ďalší
senzor. Prerušený reed vodič vyzerá ako „nie je zatvorené“ a blokuje impulz;
skrat vodiča môže vyzerať ako zatvorená brána.

## Konfigurácia

1. Skopírovať `include/secrets.example.h` na `include/secrets.h`.
2. Vyplniť Wi-Fi, broker hostname/port a samostatné ESP32 prihlasovacie údaje.
3. Provisionovať náhodný 32-bajtový HMAC kľúč (64 malých hex znakov). Rovnaký
   kľúč uložiť v mobilnej aplikácii. Nepoužívať príklad ani zdieľaný verejný kľúč.
4. Vložiť dôveryhodný CA certifikát pre konkrétny broker. CA overiť podľa jeho
   oficiálnej dokumentácie. `setInsecure()` sa nepoužíva.

Bez lokálneho `secrets.h` sa dá zostaviť kontrolný firmvér, ale placeholdery
pri štarte zablokujú pripojenie aj ovládanie relé. Skutočné tajomstvá sú ignorované
Gitom. PlatformIO 6.1.18 môže byť nainštalované do lokálneho `.venv`; súbor
`platformio.ini` pripína platformu aj priame knižnice.

Provisionovaný kľúč je v tejto etape súčasťou obrazu firmvéru. Secure Boot,
šifrovanie flash a ochrana proti fyzickému čítaniu zariadenia tu ešte nie sú
zapnuté; ignorovanie súboru Gitom túto ochranu nenahrádza.

MQTTS používa ESP32 Arduino 2.0.17 cez platformu `espressif32@6.9.0`.
Táto zostava používa TLS 1.2; TLS 1.3 na ESP32 týmto základom nie je garantované.
Na TLS 1.3 treba samostatný upgrade platformy a overenie vyjednanej verzie.
Čas je inicializovaný cez SNTP; bez platného času sa broker nepripojí a príkazy
sa nevykonajú. SNTP v tejto etape nie je kryptograficky autentifikované.

Pre neskorší súkromný broker zmeniť konfiguráciu hostname/portu, účtu a CA.
Použiť hostname pokrytý certifikátom a smerovanie cez VPN bránu/lokálnu sieť.
Na bežnom ESP32 tu nebeží Tailscale klient; priamy prístup na Tailscale IP
vyžaduje vhodné sieťové smerovanie.

## MQTT kontrakt

Root je `aigarage/garage1`. Všetky správy sú UTF-8 JSON obálky:

```json
{"body":"presny JSON text","mac":"64 malych hex znakov"}
```

`mac = HMAC-SHA256(key, UTF8(topic + "\n" + body))`. Podpisuje sa presný
text `body`, vrátane poradia a medzier; JSON po deserializácii nekanonizovať.

| Topic | Smer | Body |
|---|---|---|
| `…/telemetry` | ESP32 → mobil | `boot`, `seq`, `ts`, `door`, voliteľne `temperatureC`, `humidityPct`, `pressureHpa`. |
| `…/command` | mobil → ESP32 | `boot`, `seq` (kladné int64), `action: "open"`, `expires`. |
| `…/ack` | ESP32 → mobil | `boot`, rovnaké `seq`, `ts`, `result`. |

`ts`/`expires` sú UTC Unix sekundy. `boot` je náhodný 32-hex **identifikátor
aktuálnej MQTT relácie**, mení sa aj pri reconnecte. Telemetria ide každé
2 sekundy; `seq` telemetrie je samostatné uint32. MQTT publish je QoS 0,
subscribe na príkazy QoS 1; všetky publikované správy majú retain=false.

Broker účtu ESP32 povoliť publish iba `…/telemetry` a `…/ack`, subscribe iba
`…/command`. Mobilu opačne. Príkazy neposielať ako retained. PubSubClient
callback retain flag nevystavuje, preto sa session nonce mení **pred subscribe**;
zadržaný príkaz starej relácie sa tým stane nepoužiteľný.

Výsledky ACK: `pulsed`, `rejected_state`, `rejected_replay`, `rejected_expired`.
Malformed správa, nesprávny HMAC/session/action sa potichu zahodí. ACK môže
pri výpadku siete chýbať; v takom prípade príkaz automaticky neopakovať.

## Interlocky

- Reed musí byť zatvorený a stabilný minimálne 100 ms; pred pulzom sa znova číta.
- Overuje sa HMAC v konštantnom čase pre správne formátované podpisy, session,
  monotónne `seq` a platnosť maximálne 15 sekúnd.
- Každé nové autentifikované `seq` sa spotrebuje aj pri zamietnutí pre stav alebo
  čas. Posun systémového času späť nemôže oživiť už zamietnutý príkaz.
- GPIO aktívne 500 ms; počas impulzu sa nevykonáva MQTT, reconnect ani I2C.
  Až po vypnutí GPIO sa posiela podpísané potvrdenie.
- Ďalší impulz sa povoľuje až po pozorovanom odchode reed senzora zo zatvorenej
  polohy, návrate do nej a 30-sekundovej prestávke. Keď sa pohon nepohne,
  reconnect ďalší impulz neodblokuje.

## Overenie

`tests/GarageSafetyTests.cpp` obsahuje päť scenárov a 26 podmienok vykonaných
pri kompilácii pomocou `static_assert`: debounce/raw vstup, expirácia/replay
vrátane posunu času späť, 500-ms časovanie, neúspešný pohyb/reconnect,
odchod/návrat/cooldown a pretečenie `millis()`.

Vo Visual Studio Developer PowerShell:

```powershell
cl /nologo /std:c++17 /EHsc /W4 /WX /c /I include tests\GarageSafetyTests.cpp /Fo:tests\GarageSafetyTests.obj
```

PlatformIO zostavenie (žiadne nahrávanie):

```powershell
$env:PLATFORMIO_CORE_DIR = Join-Path (Get-Location).Path '.platformio'
.\.venv\Scripts\python.exe -m platformio run
```

Host testy a zostavenie nepotvrdzujú správanie reálneho pohonu. Pred pripojením
pohonu na stole overiť polaritu relé, neaktívny reset/boot, dĺžku impulzu,
stabilitu reed senzora, chybné podpisy, expiráciu, opakovanie, retained/reconnect,
výpadok Wi-Fi/brokera, BME280 a správny TLS certifikát. Potom overiť zariadenie
a mobil s vlastným brokerom; v tejto etape sa nič automaticky neflashuje.
