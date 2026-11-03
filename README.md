# AI Garage — Štádium 1

Prvý lokálny základ pre ESP32 DevKit a .NET MAUI aplikáciu na Android.
Šablóna projektu zostáva multiplatformová; na Windows je možné nastavovať
pripojenie a sledovať telemetriu. Fyzické otvorenie je v tejto etape dostupné
iba na Androide 11+ so zaregistrovanou silnou biometriou.

## Súbory

- `AIGarage.Mobile`: obrazovka garáže, nastavenia, SecureStorage, biometria,
  MQTTnet 4.3.7.1207, TLS a podpísané potvrdenia príkazov.
- `AIGarage.Core`: konfigurácia, modely, HMAC protokol, kontrola čerstvosti
  a ochrana pred opakovaním správ, bez závislosti na MAUI.
- `AIGarage.Core.Tests`: spustiteľné kontroly protokolu bez testovacích balíčkov.
- `AIGarage.Mobile.Tests`: kontroly skutočného MQTT servisu s riadeným
  transportom a lokálnou TLS skúškou, bez MAUI a bez fyzického zariadenia.
- `firmware`: ESP32 firmware pre PlatformIO; podrobnosti v `firmware/README.md`.

## Prvé spustenie vo Visual Studiu 2026

1. Otvoriť `AIGarage.slnx`. Ak už bol otvorený, prijať obnovenie projektu.
2. Nastaviť `AIGarage.Mobile` ako startup projekt.
3. Na prvú kontrolu obrazovky vybrať Windows Machine a spustiť aplikáciu.
   Otváranie je na Windows zámerne zablokované.
4. Na Pixel vybrať Android zariadenie. Zapnúť vývojárske nastavenia a USB
   debugging; potvrdiť dôveru tomuto počítaču na telefóne. Potom spustiť cez VS.
5. Bez nakonfigurovaného ESP32 bude stav neznámy a ovládanie nedostupné.

Projektový `NuGet.Config` používa iba nuget.org. Globálne nastavenia balíčkov
sa nemenia. iOS/MacCatalyst šablóny zostali zachované, ale v tejto etape nie sú
implementované ani overované ako plné cieľové platformy.

## HiveMQ Cloud a prístupové práva

Použiť vlastný cloudový cluster s TLS hostname a portom 8883; anonymný verejný
testovací broker nie je vhodný na ovládanie brány. Vytvoriť dva rozdielne účty.
Práva obmedziť na konkrétne témy:

| Účet | Publish | Subscribe |
|---|---|---|
| ESP32 | `aigarage/garage1/telemetry`, `aigarage/garage1/ack` | `aigarage/garage1/command` |
| Mobil | `aigarage/garage1/command` | `aigarage/garage1/telemetry`, `aigarage/garage1/ack` |

Ak plán brokera nedovoľuje tieto ACL, zvoliť plán alebo broker, ktorý ich
podporuje. Nedávať účtom prístup na všetky zariadenia cez `#`.

V mobilnej aplikácii nastaviť režim Cloud, hostname bez `mqtts://`, TLS port,
koreň tém, MQTT používateľa/heslo a HMAC kľúč. Tlačidlo „Uložiť a pripojiť“
uloží prihlasovacie údaje cez SecureStorage a otvorí MQTT spojenie.
Budúce prihlásenie uloženými údajmi vyžaduje biometriu. Každé otvorenie brány
vyžaduje nové biometrické potvrdenie. Pri prechode aplikácie do pozadia sa
relácia uzamkne a odpojí; príkazy sa nikdy automaticky neopakujú.

## Provisionovanie kľúča a firmware

Rovnaký náhodný 32-bajtový kľúč je potrebný v ESP32 aj mobile. Vygenerovať ho
lokálne napríklad v PowerShell 7:

```powershell
$garageKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
[Convert]::ToHexString($garageKey).ToLowerInvariant()
[System.Security.Cryptography.CryptographicOperations]::ZeroMemory($garageKey)
```

Výstup je tajný kľúč. Nevkladať ho do Git ani do verejného chatu.
Použiť samostatný kľúč pre každú garáž.

Skopírovať `firmware/include/secrets.example.h` na `secrets.h` a vyplniť Wi-Fi,
ESP32 MQTT účet, hostname, port, rovnaký koreň tém a HMAC kľúč.
Vložiť dôveryhodný koreňový CA certifikát pre broker; zdroj certifikátu
overiť podľa poskytovateľa. Placeholder konfigurácia sa zostaví, ale nechá
relé vypnuté a nespustí spojenie. `secrets.h` je ignorovaný v Git.

Zapojenie: relé GPIO26 (aktívne LOW), reed GPIO27 proti GND pri zatvorenej
bráne, BME280 SDA21/SCL22. Overiť polaritu relé pred pripojením k pohonu a
zabezpečiť hardvérový neaktívny stav počas resetu. Podrobnosti aj postup
nahrávania sú vo `firmware/README.md`.

## Správy a správanie

Každá aplikačná MQTT správa má obálku:

```json
{"body":"presný JSON text","mac":"64 malých hex znakov"}
```

Podpis: `HMAC-SHA256(key, UTF8(topic + "\n" + body))`. JSON sa podpisuje ako
presný text; prijímateľ ho pred overením neserializuje znova.

| Téma | Podpísané údaje v `body` |
|---|---|
| `/telemetry` | `boot`, `seq`, `ts`, `door`, voliteľne `temperatureC`, `humidityPct`, `pressureHpa` |
| `/command` | `boot`, `seq`, `action: "open"`, `expires` |
| `/ack` | `boot`, `seq` príkazu, `ts`, `result` |

`boot` je náhodný identifikátor **MQTT relácie zariadenia**, mení sa pri
každom reconnecte. Tým zneplatní staré aj prípadne uložené príkazy brokera.
Poradové číslo chráni pred opakovaním v rámci relácie; platnosť príkazu je
10 sekúnd. ESP32 kontroluje čas, podpis, reláciu, poradové číslo a aktuálny
debouncovaný aj surový stav reed vstupu.

Telemetria prichádza približne každé 2 sekundy. Údaje staršie než 5 sekúnd,
opakované, zo starých relácií, s neplatným podpisom alebo retain príznakom
mobil odmieta. Čerstvosť pre zobrazenie aj ovládanie sa kontroluje tiež
monotónnym časom, takže zmena hodín telefónu nepredĺži platnosť prijatých dát.
História predchádzajúcich relácií sa zachová pri odpojení aj po odoslaní
príkazu. Po 64 zmenách relácie v jednom behu aplikácia ďalšiu odmietne;
nový beh aplikácie vytvorí novú históriu.

Príkaz vytvorí jediný 500 ms impulz. Žiadne sieťové ani senzorové operácie
sa nevykonávajú počas aktívneho relé. Podpísané `pulsed` potvrdzuje ukončenie
softvérového impulzu, **nie otvorenie brány**. Pri chybe alebo chýbajúcom
potvrdení je výsledok neznámy; príkaz sa neopakuje.

Jeden reed potvrdzuje iba „zatvorené / nie je zatvorené“. „Pohyb“ je
30-sekundový odhad. Po impulze je ďalšie otvorenie blokované až do reálneho
odchodu zo zatvorenej polohy a návratu, minimálne 30 sekúnd. Ak sa brána
nepohne, reconnect túto blokáciu neodstráni. Na spoľahlivé určenie úplného
otvorenia treba ďalší koncový senzor alebo spätnú väzbu pohonu.

## Bezpečnostné hranice a Štádium 2

- Certifikát a hostname sa vždy overujú. Neexistuje prepínač na vypnutie TLS.
- Mobil povoľuje TLS 1.2 aj 1.3; konkrétne vyjednanie závisí od OS a brokera.
  Pripnutý ESP32 Arduino stack používa TLS 1.2. TLS 1.3 na ESP32 tu nie je
  implementované ani garantované.
- Android používa systémový `BiometricPrompt` s `BIOMETRIC_STRONG`; Face Unlock
  je dostupný iba ak ho konkrétny Pixel/Android klasifikuje ako silnú biometriu.
  Slabšia biometria ani PIN tento krok nenahrádza.
- Biometria je aplikačné potvrdenie; SecureStorage kľúč nie je v tejto etape
  kryptograficky viazaný na každé použitie cez Keystore CryptoObject. ESP32
  provisioning v hlavičke tiež nenahrádza secure boot alebo flash encryption.
- Android cloud backup je vypnutý. Heslá ani kľúč nie sú uložené v obyčajných
  nastaveniach alebo v zdrojovom kóde mobilnej aplikácie.
- Profil Súkromný broker používa rovnaký protokol s iným endpointom. Na mobile
  bude potrebný Tailscale a server s dôveryhodným TLS certifikátom. ESP32 nemá
  Tailscale klienta; k brokeru potrebuje LAN alebo vhodné smerovanie subnetov.
  Sieťové ACL/firewall, VPN, NAS a kamera sa v tejto etape ešte nenasadzujú.

## Lokálne overenie

Príkazy spustiť z priečinka `AIGarage`:

```powershell
dotnet run --project AIGarage.Core.Tests -c Release
dotnet run --project AIGarage.Mobile.Tests -c Release
dotnet build AIGarage.Mobile -f net10.0-android
dotnet build AIGarage.Mobile -f net10.0-windows10.0.19041.0
```

Firmware obsahuje čisté C++ kontroly interlocku a kompiláciu pre ESP32.
Pri príprave 10. 10. 2026 prešlo 77 kontrol spoločného protokolu a 25 kontrol
MQTT servisu vrátane odmietnutia nedôveryhodného TLS certifikátu. Android a
Windows Debug buildy skončili bez chýb aj upozornení. ESP32 PlatformIO release
build prešiel; 5 compile-time C++ scenárov overilo 26 bezpečnostných podmienok.
Kontroly `.Tests` sú spustiteľné programy, nie testy pre Visual Studio Test Explorer.

Úspešná kompilácia alebo kontroly nepreukazujú správne elektrické zapojenie,
biometriu na Pixeli, živé spojenie s HiveMQ ani mechanické správanie brány.
Pred prvým pripojením k pohonu overiť reset, polaritu a skutočnú dĺžku pulzu
na oddelenom relé; potom sensor fault, výpadok siete a reštart zariadenia.

Použité API: [MQTTnet](https://github.com/dotnet/MQTTnet),
[MAUI SecureStorage](https://learn.microsoft.com/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0),
[Android biometria](https://developer.android.com/identity/sign-in/biometric-auth).
