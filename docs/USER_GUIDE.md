# Používateľský návod — AI Garage

Platí pre Štádium 1 a krok A2: aplikácia s profilmi viacerých zariadení,
vždy jedným aktívnym pripojením, ESP32, relé, reed senzorom a BME280.
Vývojový základ ešte nemá potvrdené fungovanie na
skutočnom pohone. Kamerový systém, NAS, história a automatizácie nie sú súčasťou
tejto verzie.

## 1. Čo potrebujete

| Súčasť | Požiadavka |
|---|---|
| Mobil | Android 11+ so zaregistrovanou silnou biometriou a pripojením na internet. |
| Vývojový počítač | Windows, Visual Studio 2026 a .NET MAUI workload s Android SDK. |
| Zariadenie | ESP32 DevKit, vhodné relé, reed senzor a BME280. |
| Sieť | Wi-Fi pre ESP32, prístup k TLS brokeru a synchronizácii času. |
| Broker | Vlastný HiveMQ Cloud cluster alebo kompatibilný broker s TLS a obmedzenými účtami. |

Na Windows môžete nastavovať pripojenie a sledovať údaje. Otvorenie je dostupné
iba na podporovanom Androide. Face Unlock možno použiť, iba ak ho konkrétny
telefón klasifikuje ako silnú biometriu. Alternatívou je podporovaný odtlačok.
PIN ani slabá biometria otvorenie nenahrádzajú.

## 2. Príprava zariadenia a účtov

1. Zapojenie a firmware pripravte podľa [návodu ESP32](../firmware/README.md).
   Pred pripojením k pohonu overte relé samostatne. Zachovajte bezpečnostné
   prvky pohonu a jeho pôvodné ovládanie.
2. Na brokeri vytvorte samostatný účet pre ESP32 a samostatný účet pre mobil.
3. Nastavte práva podľa tabuľky v [README](../README.md#hivemq-cloud-a-prístupové-práva).
   Mobil publikuje iba príkazy svojej garáže; ESP32 publikuje jej telemetriu a potvrdenia.
4. Pre garáž vygenerujte náhodný 32-bajtový HMAC kľúč. Rovnaký kľúč vložte do
   lokálnej konfigurácie ESP32 a neskôr do mobilnej aplikácie. Postup je
   v [README](../README.md#provisionovanie-kľúča-a-firmware).
5. Do ESP32 vložte správny dôveryhodný CA certifikát brokera. Overenie
   certifikátu sa nevypína. Skutočný `secrets.h` ani heslá neukladajte do Git.

## 3. Spustenie aplikácie

1. Otvorte `AIGarage.slnx` vo Visual Studiu 2026.
2. Ako startup projekt nastavte `AIGarage.Mobile`.
3. Pre prvú kontrolu zobrazenia vyberte Windows Machine a spustite aplikáciu.
4. Pre Pixel zapnite vývojárske nastavenia a USB debugging. Pripojte telefón,
   potvrďte oprávnenie počítača a vo Visual Studiu vyberte Android zariadenie.
5. Spustite aplikáciu cez Visual Studio. Na overenie skutočnej biometrie použite
   telefón; Windows test nepotvrdzuje jej funkčnosť.

## 4. Prvé prihlásenie

Vyplňte časť **Pripojenie a prihlásenie**:

| Pole | Čo zadať |
|---|---|
| Názov zariadenia | Jedinečný zrozumiteľný názov, napríklad „Domáca garáž“, najviac 64 znakov. |
| Režim | Cloud (Štádium 1). |
| Hostname | DNS názov vášho clusteru bez `mqtts://`, cesty a portu. |
| TLS port | Spravidla `8883`, podľa nastavenia brokera. |
| Koreň tém | Napríklad `aigarage/garage1`; musí byť rovnaký ako vo firmware a ACL. |
| MQTT používateľ | Účet určený pre mobilnú aplikáciu. |
| MQTT heslo | Heslo tohto mobilného účtu. |
| HMAC kľúč | 64 hexadecimálnych znakov zodpovedajúcich kľúču ESP32. |

Vo **Funkcie zariadenia** nechajte pri existujúcom ESP32 zapnuté sledovanie
polohy, otváranie, teplotu, vlhkosť a tlak. Na sledovanie bez ovládania vypnite
**Otváranie garáže**. Otváranie vyžaduje aj sledovanie polohy. Prepínač funkcie
nepridáva nový senzor ani firmware; zobrazuje a obmedzuje podporované možnosti.

Stlačte **Uložiť a pripojiť**. Heslo, používateľ a HMAC sa ukladajú cez
SecureStorage; ich vstupné polia sa po uložení vyprázdnia. Hostname, port,
režim a koreň tém zostávajú v bežných nastaveniach.

Pred úpravou pripojeného profilu stlačte **Odpojiť a uzamknúť**. Pri úprave
mena, endpointu alebo funkcií zadajte jeho prihlasovacie údaje znova a použite
**Uložiť a pripojiť**. Rozpracované zmeny sa nezapisujú automaticky.

Uloženie údajov ešte neznamená úspešné overenie účtu. Ak spojenie zlyhá,
opravené údaje treba zadať a uložiť znova. Pri úspešnom pripojení aplikácia
čaká na podpísané údaje ESP32. Samotné prihlásenie bránu neovláda.

## 5. Bežné používanie

Pri ďalšom spustení použite **Prihlásiť uloženými údajmi cez biometriu**.
Uložené prihlasovacie údaje sa načítajú po úspešnom biometrickom overení.

### Viac zariadení

1. **Pridať nové zariadenie** odpojí pôvodný profil a otvorí prázdne pripojenie.
   Vyplňte nový názov, endpoint, koreň tém a vlastné credentials. Uložte a
   pripojte; tým profil pribudne do zoznamu. Limit je 32 profilov.
2. V zozname **Vyberte uložený profil** vyberte zariadenie a stlačte
   **Načítať vybraný profil a odpojiť**. Samotný výber ešte neprepne spojenie;
   ovládanie je dovtedy zablokované, ak sa výber líši od aktívneho profilu.
3. Načítanie uzamkne staré ovládanie, odpojí staré spojenie a vyprázdni tajné
   polia. Potom sa prihláste uloženými údajmi cez biometriu. Až nová aktuálna
   podpísaná telemetria môže sprístupniť ovládanie.
4. Pred opustením editovaného profilu uložte požadované zmeny; načítanie iného
   alebo vytvorenie nového zahodí neuložené polia.

Každé zariadenie musí mať vlastný koreň tém, ACL a náhodný HMAC kľúč.
Profily môžu používať rovnaký broker. Rovnaký endpoint a koreň tém sa nedajú
uložiť pod dvoma profilmi. Pri nastavení iba na meranie stále platí súčasný
MQTT formát vrátane `door`; univerzálny Arduino protokol pribudne neskôr.

Po aktualizácii z A1 sa pôvodné nastavenia zobrazia ako **Garáž 1**. Overte
ich hostname a témy. Pri prvom biometrickom prihlásení alebo opätovnom uložení
sa prevedú do nového úložiska. Zobrazenie profilu samotné nečíta tajné údaje.

| Zobrazený stav | Význam |
|---|---|
| Zatvorené | Reed senzor potvrdzuje zatvorenú polohu. |
| Nie je zatvorené | Reed nepotvrdzuje zatvorenú polohu; úplné otvorenie tým nie je potvrdené. |
| Pohyb (odhad) | Tridsaťsekundový odhad po vykonaní impulzu. |
| Stav neznámy | Údaje chýbajú, sú neaktuálne alebo aplikácia nie je prihlásená. |

Teplota je v °C, vlhkosť v % a tlak v hPa. Chýbajúci senzor alebo neaktuálne
údaje sa zobrazia pomlčkou. Telemetria prichádza približne každé dve sekundy;
po viac než piatich sekundách bez platných aktuálnych údajov sa ovládanie zablokuje.

## 6. Otvorenie garáže

1. Overte, že je bezpečné otvoriť bránu a že aplikácia zobrazuje **Zatvorené**.
2. Stlačte **Otvoriť garáž** a potvrďte biometriu.
3. Aplikácia po biometrickom overení znovu preverí aktuálnosť stavu a odošle
   jediný príkaz. Zariadenie nezávisle overí podpis, reláciu, čas a reed senzor.
4. Potvrdenie impulzu znamená, že ESP32 ukončilo 500 ms softvérový impulz relé.
   Sledujte aj následný stav a pohon. Úplné otvorenie jeden reed nepotvrdzuje.

Ak potvrdenie nepríde, príkaz sa **mohol vykonať**. Najprv skontrolujte bránu;
aplikácia ho automaticky neopakuje. Pri zmene polohy alebo expirácii údajov
počas biometrického dialógu sa príkaz neodošle.

ESP32 po impulze blokuje ďalší impulz až do skutočne pozorovaného odchodu zo
zatvorenej polohy, návratu a uplynutia minimálne 30 sekúnd. Keď sa pohon vôbec
nepohne, reconnect blokáciu neodstráni. Aj pri neskoršom zobrazení **Zatvorené**
môže zariadenie odmietnuť otvorenie pre tento interlock. Overte pohon a zapojenie;
neobchádzajte blokáciu opakovaným posielaním príkazov.

## 7. Odpojenie a odstránenie údajov

- **Odpojiť a uzamknúť** zruší rozpracovanú operáciu, uzamkne ovládanie a odpojí MQTT.
- Uzamknutie neodvolá už odoslaný príkaz a nezastaví pohyb pohonu.
- Pri prechode do pozadia alebo uzamknutí telefónu sa relácia uzamkne. Po návrate
  sa prihláste znova; spojenie ani otvorenie sa automaticky neobnovujú.
- **Odstrániť aktuálny profil a jeho údaje** vyžaduje potvrdenie s názvom
  načítaného profilu, uzamkne aplikáciu a odstráni iba tento profil a jeho
  credentials. Ostatné zostanú uložené. Účet na brokeri sa tým neodstráni.
- Ak aplikácia prejde do pozadia počas potvrdzovania otvorenia, neskoršie
  dokončenie biometrie nesmie spustiť bránu.

## 8. Riešenie problémov

| Problém | Čo overiť |
|---|---|
| Spojenie zlyhalo | Hostname, TLS port, internet, účet, certifikát a čas zariadení. |
| Pripojené, údaje neprichádzajú | Stav ESP32, rovnaký koreň tém, HMAC kľúč a ACL pre odber. |
| Údaje po chvíli zmiznú | Wi-Fi ESP32, broker a čas telefónu; starý stav sa nesmie použiť na otvorenie. |
| Otvorenie nie je dostupné | Prihlásenie, čerstvý zatvorený stav a silná biometria na Androide. |
| Biometria nefunguje | Zaregistrovaný odtlačok/podporovaná tvár a systémové obmedzenia po reštarte či zablokovaní biometrie. |
| Príkaz vypršal | Synchronizácia času telefónu a ESP32, sieťové oneskorenie. |
| Príkaz bol odmietnutý pre stav | Skutočná poloha, reed, predchádzajúci impulz a interlock pohybu. |
| Teplota/vlhkosť/tlak chýbajú | Zapojenie BME280, napájanie 3,3 V a I2C adresu podľa firmware návodu. |
| Profil sa nedá odomknúť po úprave | Načítajte uložený profil alebo zadajte jeho credentials a uložte zmeny. Údaje sa nesmú použiť s iným endpointom. |
| Neplatný alebo nepodporovaný katalóg | Overte verziu aplikácie. Pôvodné dáta sa zámerne neprepísali; bez zálohy ich nemažte. |
| Limit identít počas behu aplikácie | Po mnohých zmenách endpointov/kľúčov aplikáciu ukončite a znovu spustite, potom sa prihláste. |

Profil Súkromný broker pripravuje iný endpoint s rovnakými pravidlami TLS.
Tailscale, broker na Raspberry Pi a jeho certifikáty táto aplikácia sama
nenainštaluje. Postup ich nasadenia pribudne v príslušnej etape.

Praktické overenie zariadenia vykonajte podľa [testovacieho postupu](TESTING.md).
