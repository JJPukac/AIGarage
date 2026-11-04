# AI Garage — Štádium 1

Repozitár: [Pukac/AIGarage](https://github.com/Pukac/AIGarage).

Prvý lokálny základ pre ESP32 DevKit a .NET MAUI aplikáciu na Android.
Šablóna projektu zostáva multiplatformová; na Windows je možné nastavovať
pripojenie a sledovať telemetriu. Fyzické otvorenie je v tejto etape dostupné
iba na Androide 11+ so zaregistrovanou silnou biometriou.

## Dokumentácia

- [Používateľský návod](docs/USER_GUIDE.md): príprava, prihlásenie, ovládanie a riešenie problémov.
- [Testovanie](docs/TESTING.md): automatické kontroly, buildy, manuálne scenáre a záznam výsledkov.
- [Architektúra a plán](docs/ARCHITECTURE.md): rozdelenie zodpovedností a ďalšie kroky pre Arduino/Raspberry Pi.
- [Git workflow](docs/GIT_WORKFLOW.md): Visual Studio 2026, vetvy, commity a práca z dvoch laptopov cez účet Pukac.
- [ESP32](firmware/README.md): zapojenie, provisioning, zostavenie a bezpečnostné hranice.
- [Overenie A1](docs/validation/2026-11-03-A1.md): predchádzajúci krok MVVM a jeho praktické hranice.
- [Overenie A2](docs/validation/2026-11-04-A2.md): profily zariadení, migrácia a kontrola oddelenia relácií.

Aktuálne je implementovaný krok **A2**: katalóg najviac 32 pomenovaných profilov,
stabilné lokálne ID, samostatné prihlasovacie údaje a nastaviteľné funkcie.
Aplikácia používa jednu aktívnu reláciu. Načítanie iného profilu pôvodnú reláciu
uzamkne a odpojí; nové prihlásenie ani otvorenie nevykoná automaticky.
MQTT formát a ESP32 firmware zostávajú kompatibilné. Simulátor je ďalší krok.

## Súbory

- `AIGarage.Mobile`: obrazovka garáže, nastavenia, SecureStorage, biometria,
  ViewModel, MQTTnet 4.3.7.1207, TLS a podpísané potvrdenia príkazov.
- `AIGarage.Core`: konfigurácia, katalóg a funkcie zariadení, modely, HMAC protokol, kontrola čerstvosti
  a ochrana pred opakovaním správ, bez závislosti na MAUI.
- `AIGarage.Core.Tests`: spustiteľné kontroly protokolu bez testovacích balíčkov.
- `AIGarage.Mobile.Tests`: kontroly skutočného MQTT servisu s riadeným
  transportom, lokálnou TLS skúškou a kontroly ViewModelu, bez MAUI a fyzického zariadenia.
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
koreň tém, názov zariadenia, jeho funkcie, MQTT používateľa/heslo a HMAC kľúč.
Tlačidlo „Uložiť a pripojiť“ uloží profil a jeho prihlasovacie údaje cez
SecureStorage a otvorí MQTT spojenie. Ďalší profil vytvoríte cez „Pridať nové
zariadenie“. Pred úpravou existujúceho pripojenia sa odpojte a uzamknite.
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

História sa uchováva aj pri prepínaní profilov A → B → A. Najviac 64 rôznych
identít témy/kľúča sa sleduje počas jedného behu; pri prekročení limitu
sa nové spojenie odmietne bez vymazania starších záznamov. Profil s vypnutým
otváraním nepovolí príkaz ani pri priamom volaní MQTT služby.

Funkcie profilu opisujú používané merania a ovládanie. Nenainštalujú nový
firmware ani nepridajú protokol pre inú dosku. Aj profil iba na meranie v tejto
etape používa existujúci podpísaný formát vrátane položky `door`; univerzálny
senzorový protokol a Arduino gateway patria do ďalších krokov.

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
.\scripts\Test.ps1
# S buildmi oboch podporovaných cieľov:
.\scripts\Test.ps1 -BuildAndroid -BuildWindows
```

Samostatné príkazy:

```powershell
dotnet run --project AIGarage.Core.Tests -c Release
dotnet run --project AIGarage.Mobile.Tests -c Release
dotnet build AIGarage.Mobile -f net10.0-android
dotnet build AIGarage.Mobile -f net10.0-windows10.0.19041.0
```

Aktuálne testovacie programy obsahujú 98 kontrol Core a 84 kontrol mobilnej
časti, spolu 182. Zahŕňajú odmietnutie nedôveryhodného TLS certifikátu,
zrušenie prihlasovania/otvorenia pri prechode do pozadia, oddelenie credentials,
migráciu a replay ochranu pri prepínaní profilov. Podrobnosti sú v
`docs/TESTING.md`.

Firmware obsahuje čisté C++ kontroly interlocku. Pri príprave 10. 10. 2026
prešiel ESP32 PlatformIO release build; 5 compile-time scenárov overilo 26
bezpečnostných podmienok. Tento historický výsledok nepredstavuje nové meranie hardvéru.
Kontroly `.Tests` sú spustiteľné programy, nie testy pre Visual Studio Test Explorer.

Úspešná kompilácia alebo kontroly nepreukazujú správne elektrické zapojenie,
biometriu na Pixeli, živé spojenie s HiveMQ ani mechanické správanie brány.
Pred prvým pripojením k pohonu overiť reset, polaritu a skutočnú dĺžku pulzu
na oddelenom relé; potom sensor fault, výpadok siete a reštart zariadenia.

Použité API: [MQTTnet](https://github.com/dotnet/MQTTnet),
[MAUI SecureStorage](https://learn.microsoft.com/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0),
[Android biometria](https://developer.android.com/identity/sign-in/biometric-auth).
