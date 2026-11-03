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
