# Git workflow — AI Garage

Aktívny repozitár projektu je **[Pukac/AIGarage](https://github.com/Pukac/AIGarage)**.
Pre prístup a nové commity tohto projektu používame účet **Pukac**.
Repozitár je verejný. História existujúcich commitov zostáva zachovaná.

Príkazy spúšťajte z priečinka, v ktorom je `AIGarage.slnx`.

## Kontrola nastavenia

```powershell
git remote -v
git status --short --branch
git config --local user.name
git config --local user.email
git config --local credential.https://github.com.username
```

`origin` má smerovať na `https://github.com/Pukac/AIGarage.git` a účet na `Pukac`.
Autorské nastavenia sú lokálne pre tento projekt. Pre súkromie používame GitHub
noreply adresu; e-mailová schránka sa tým nemení:

```powershell
git config --local user.name Pukac
git config --local user.email 129390152+Pukac@users.noreply.github.com
git config --local credential.https://github.com.username Pukac
```

Tieto nastavenia samy nevytvoria prihlásenie. Pri prvom pushi na inom počítači
sa prihláste do GitHubu účtom Pukac. Heslo ani prístupový token nepatria do
remote URL, zdrojových súborov alebo dokumentácie.

## Visual Studio 2026 na tomto laptope

Projekt už obsahuje Git repozitár a nastavený `origin`. Visual Studio ho pri
otvorení solution rozpozná; ďalší repozitár netreba vytvárať.

1. Cez **File > Open > Project/Solution** otvorte existujúci `AIGarage.slnx`.
2. Cez **View > Git Changes** zobrazte zmeny a aktuálnu vetvu. Po otvorení
   tohto projektu skontrolujte, že pracujete s repozitárom `AIGarage`.
3. Cez **Git > Manage Remotes** overte adresu
   `https://github.com/Pukac/AIGarage.git` pre `origin`.
4. Ak Visual Studio vyžiada GitHub prihlásenie, vyberte účet **Pukac**.
   Prihlásenie vo Visual Studiu môže vyžadovať vlastné autorizovanie aj vtedy,
   keď už funguje push z príkazového riadka.

Ak menu Git chýba, v **Tools > Options > All Settings > Source Control >
Plug-in Selection** vyberte Git. Pri staršom rozložení Options sa sekcia
Source Control nachádza priamo bez All Settings.

## Jedna úloha = jedna pracovná vetva

`main` používame pre dokončené a overené kroky. Novú funkciu alebo opravu
rozpracujte vo vlastnej vetve, napríklad `feature/device-catalog` alebo
`fix/connection-status`.

1. Pri čistom pracovnom priečinku prepnite na `main`, zvoľte **Git > Fetch**
   a potom **Git > Pull**.
2. Zvoľte **Git > New Branch**, zadajte názov, ako základ vyberte aktualizovanú
   `main` a nechajte zaškrtnuté **Checkout branch**.
3. Urobte jednu súvisiacu zmenu a vykonajte príslušné kontroly z
   [TESTING.md](TESTING.md).
4. V **Git Changes** otvorte rozdiely súborov. Cez **Stage** alebo tlačidlo `+`
   pridajte iba súbory, ktoré patria k tejto zmene.
5. Napíšte zrozumiteľnú správu a zvoľte **Commit Staged**. Potom **Push**;
   pri prvom odoslaní publikujte novú vetvu na `origin`.
6. Na GitHube vytvorte Pull Request z tejto vetvy do `main`. Pred zlúčením
   skontrolujte rozdiely a výsledky testovania. Po zlúčení aktualizujte
   lokálnu `main` cez Fetch a Pull.

Commit zostáva lokálne až do úspešného pushu. Vetvu s rozpracovanou úlohou
možno pushnúť aj pred jej zlúčením do `main`; tým ju sprístupníte druhému PC.

## Druhý laptop: prvé nastavenie

1. Nainštalujte Visual Studio 2026 s podporou .NET MAUI pre Android a Windows
   a .NET SDK 10 podľa [TESTING.md](TESTING.md). Pre prácu na firmware
   pripravte aj PlatformIO podľa [firmware návodu](../firmware/README.md).
2. Zvoľte **Git > Clone Repository** a vložte
   `https://github.com/Pukac/AIGarage.git`. Použite samostatný lokálny
   priečinok mimo OneDrive/Dropbox synchronizácie.
3. Otvorte naklonovaný `AIGarage.slnx`. Vo Visual Studiu vyberte GitHub účet
   Pukac; verejné čítanie funguje aj bez prihlásenia, push potrebuje oprávnenie.
4. V koreňovom priečinku nového klonu nastavte tri lokálne autorské/prístupové
   voľby zo sekcie Kontrola nastavenia. `.git/config` sa cez Git neprenáša.
5. Obnovte NuGet balíčky a spustite kontroly podľa [TESTING.md](TESTING.md).
   Každý laptop má vlastné build výstupy a nástroje.

Git prenáša verziované zdrojové súbory, firmware a dokumentáciu. MQTT heslá,
HMAC kľúče, `secrets.h`, uložené údaje aplikácie a nastavenia `.vs` sa
neprenášajú. Potrebné tajné údaje nastavte na druhom zariadení samostatne
bez ich pridania do repozitára.

## Odovzdanie práce medzi dvoma laptopmi

| Kedy | Čo urobiť vo Visual Studiu |
|---|---|
| Končíte na laptope A | Uložte súbory, skontrolujte zmeny, vytvorte commit a úspešne ho pushnite na pracovnú vetvu. Zapamätajte si jej názov. |
| Začínate na laptope B | Zvoľte Fetch. Pri prvom pokračovaní cez Git > Manage Branches vyhľadajte rovnakú vetvu pod `origin` a zvoľte Checkout, čím vytvoríte lokálnu sledovaciu vetvu. Ak už existuje, prepnite na ňu. Potom Pull. |
| Vrátite sa na laptop A | Najprv prepnite na tú istú pracovnú vetvu a urobte Fetch a Pull. Až potom pokračujte v úpravách. |

Rozpracované, necommitnuté súbory ani lokálny stash sa na druhý laptop
neprenesú. Pred zmenou počítača vždy overte úspešný push a zobrazenie commitu
na GitHube. Oba laptopy môžu používať rovnaký účet Pukac, každý má vlastné
prihlásenie a lokálny klon.

Pri úpravách rovnakej vetvy na oboch PC bez priebežného Pull môžu vzniknúť
konflikty. Skontrolujte históriu a zmeny z oboch strán, vyriešte konflikty
v editore zlúčenia, znovu overte výsledok a vytvorte commit. Force push ani
zahodenie lokálnych zmien nepoužívajte ako automatické riešenie.

## Pred novým krokom

Pri čistom pracovnom priečinku aktualizujte vetvu:

```powershell
git fetch origin
git pull --ff-only
```

Ak existujú rozpracované zmeny alebo aktualizácia zlyhá pre rozdielne vetvy,
najprv skontrolujte stav a históriu. Na vyriešenie tejto situácie nepoužívajte
automatický force push ani mazanie lokálnych zmien.

## Jeden overiteľný krok = jeden zrozumiteľný commit

1. Upravte jednu súvisiacu časť a príslušnú dokumentáciu.
2. Spustite kontroly podľa [TESTING.md](TESTING.md); zaznamenajte aj nevykonané skúšky.
3. Skontrolujte `git diff` a `git diff --check`.
4. Pridajte konkrétne súbory príkazom `git add -- cesta/k/suboru`.
5. Skontrolujte `git diff --cached --stat` a `git diff --cached`.
6. Vytvorte commit a odošlite aktuálnu vetvu na `origin`.

Pre aktuálnu vetvu `main`:

```powershell
git commit -m "Strucny opis dokonceneho kroku"
git push origin main
git status --short --branch
```

Pri práci na inej vetve použite jej skutočný názov. Commit vytvára záznam
lokálne; až úspešný push ho uloží na GitHub. Ak push zlyhá, commit zostáva
v počítači a najprv treba odstrániť príčinu chyby.

## Súbory, ktoré sa neodosielajú

Do repozitára nepatria `firmware/include/secrets.h`, heslá, HMAC kľúče,
podpisové kľúče, `.env`, build výstupy, APK ani lokálne nástroje a zálohy.
Pred stagingom overte `.gitignore`. Vzor `secrets.example.h` obsahuje iba
placeholder hodnoty a do repozitára patrí.

Lokálne nastavenie účtu pre tento projekt nemení nastavenia ostatných projektov
na počítači.

## Dokumentácia Visual Studia

- [Klonovanie a otvorenie existujúceho repozitára](https://learn.microsoft.com/en-us/visualstudio/version-control/git-clone-repository?view=visualstudio)
- [Vytvorenie vetvy](https://learn.microsoft.com/en-us/visualstudio/version-control/git-create-branch?view=visualstudio)
- [Stage a commit](https://learn.microsoft.com/en-us/visualstudio/version-control/git-make-commit?view=visualstudio)
- [Fetch, Pull, Push a Sync](https://learn.microsoft.com/en-us/visualstudio/version-control/git-fetch-pull-sync?view=visualstudio)
