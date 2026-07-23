# Plan: GitHub Actions CI/CD release pipeline for SUSModder

**Data:** 2026-06-28  
**Status:** Plan do implementacji przed integracją SignPath/OSSign lub Sigstore/Cosign  
**Priorytet:** P0/P1 — wymagany fundament dla darmowego podpisywania OSS  
**Zakres:** GitHub Actions, release/beta Velopack, test/build gates, artifact handling, SignPath/OSSign readiness, Sigstore/Cosign provenance, upload do `susmodder.app` / GitHub Releases  
**Kontekst:** W repo nie ma obecnie .github/workflows/*. Release jest lokalnym PowerShellem (SKRYPTY/Build/*). SignPath/OSSign i Sigstore/Cosign mają sens dopiero, gdy artefakty powstają w powtarzalnym CI/CD z jasnym pochodzeniem. Repo SUSModder jest otwarte i publiczne; konto ma też inne prywatne repozytoria. GitHub Free pokazuje 2 000 CI/CD minutes/month oraz 500 MB Packages storage, więc plan musi oszczędzać zarówno minuty Windows runnerów, jak i storage dużych release artefaktów.

---

## 1. Goal

Zaprojektować i wdrożyć oszczędny storage'owo pipeline CI/CD, który:

- buduje SUSModder z czystego checkoutu na GitHub-hosted Windows runnerze,
- uruchamia szybkie testy i release gates,
- publikuje `PublishSingleFile=false`,
- pakuje Velopack release/beta,
- przygotowuje artefakty do SignPath/OSSign i/lub Sigstore/Cosign,
- nie przepala budżetu 2 000 min/mies. i nie zapycha storage dużymi artefaktami,
- może finalnie wypchnąć artefakty do GitHub Releases oraz/lub `susmodder.app`.

## 2. Non-goals

- Nie wdrażamy od razu płatnego code signing certyfikatu.
- Nie migrujemy backendu `susmodder.app`.
- Nie robimy pełnej automatycznej publikacji bez ręcznego zatwierdzenia na start.
- Nie uploadujemy dużych artefaktów z każdego PR/commita.
- Nie używamy larger runners ani płatnych funkcji GitHub Actions.
- Nie zakładamy, że Sigstore usuwa ostrzeżenia SmartScreen — traktujemy go jako provenance.

---

## 3. GitHub Free / public repository constraints and storage strategy

### 3.1 Limity do uwzględnienia

Założenie dla SUSModder: repo publiczne/OSS na GitHub Free; inne prywatne repozytoria na tym samym koncie mogą współdzielić miesięczny budżet minut.

- Pricing UI dla Free wskazuje **2 000 CI/CD minutes/month** — traktujemy to jako miesięczny budżet konta do pilnowania, szczególnie ponieważ inne repozytoria są prywatne i mogą go realnie zużywać.
- Pricing UI wskazuje **500 MB Packages storage** dla public repositories — nie używać GitHub Packages jako głównego hostingu release `.nupkg`/ZIP/Setup.
- Dla SUSModder jako publicznego repo minuty są mniej ryzykowne niż dla prywatnych repo, ale workflow release powinien być ręczny/tagowany, żeby nie przepalać budżetu konta ani nie blokować innych projektów.
- Actions artifacts/release artifacts nadal wymagają ostrożności, bo duże `.nupkg`/ZIP/Setup EXE szybko robią setki MB i utrudniają release hygiene.
- Cache storage per repository jest osobnym mechanizmem i nie powinien być używany do przechowywania release output.

Użytkownik zgłasza, że obecne zużycie storage jest około **0,5 GB**, więc przed wdrożeniem pipeline trzeba założyć cleanup i minimalną retencję dużych artefaktów.

### 3.2 Zasady storage dla SUSModder CI/CD

- PR/CI workflow w publicznym SUSModder: build/test może działać na PR/push, ale bez publish/release pack na każdym commicie.
- PR/CI workflow: **zero dużych artefaktów**; upload tylko małych logów/test results i tylko na failure, retention 1 dzień.
- Release workflow: upload tylko minimalnego artefaktu wymaganego przez SignPath albo końcowego pakietu do publikacji, retention 1 dzień.
- Nie uploadować `publish/`, `bin/`, `obj/`, pełnych TestResults ani cache z outputem aplikacji.
- Nie trzymać jednocześnie release i beta przez wiele dni w Actions artifacts.
- Preferować finalną dystrybucję poza Actions artifacts:
  - GitHub Release assets,
  - `susmodder.app/releases/...`,
  - ewentualnie oba.
- Dodać osobny cleanup/runbook: kasowanie starych Actions artifacts i GitHub Packages, jeśli storage dobije do limitu.
- Ustawić `retention-days: 1` przy `actions/upload-artifact` dla dużych artefaktów.

### 3.3 Budżet rozmiaru artefaktów

Szacunkowo:

- full `.nupkg`: ~55–80 MB,
- setup/bootstrapper EXE: ~1–70 MB zależnie od formatu,
- release + beta naraz: potencjalnie 120–200+ MB,
- artifact wejściowy do SignPath może być ZIP-em z plikami do podpisu albo paczką Velopack.

Decyzja MVP: zacząć od **jednego kanału na jeden workflow run** (`release` albo `beta`), aby obniżyć peak storage.

---

## 4. User / maintainer workflow

### 4.1 PR / normalny commit

1. Developer robi PR/commit.
2. GitHub Actions uruchamia lekkie CI:
   - restore,
   - build Debug/Release bez publish,
   - unit tests Core/UI where feasible.
3. Workflow nie tworzy release artefaktów.
4. Artefakty testowe uploadowane tylko przy FAIL i z retencją 1 dzień.

### 4.2 Release candidate

1. Maintainer uruchamia ręcznie workflow `release-candidate.yml` z parametrami:
   - version,
   - channel: `release` albo `beta`,
   - publish target: none/GitHub/susmodder.app.
2. Workflow robi build/test/publish/Velopack pack.
3. Workflow sprawdza:
   - `PublishSingleFile=false`,
   - nupkg/RELEASES/manifest istnieją,
   - hash SHA256 wygenerowany,
   - Authenticode status zapisany w logu.
4. Artefakt ma retencję 1 dzień albo od razu trafia do GitHub Release draft.

### 4.3 SignPath/OSSign flow

1. Release workflow buduje artefakt na GitHub-hosted runnerze.
2. Uploaduje minimalny workflow artifact wymagany przez SignPath.
3. Submituje signing request do SignPath.
4. Maintainer zatwierdza signing request, jeśli polityka tego wymaga.
5. Signed artifact wraca do workflow / release.
6. Release gate weryfikuje podpis `Get-AuthenticodeSignature` / `signtool verify`.

### 4.4 Sigstore/Cosign flow

1. Workflow ma `id-token: write`.
2. Po buildzie podpisuje blob/artifacts keyless przez Cosign.
3. Publikuje bundle/signature obok artefaktów.
4. Release notes zawierają komendy `cosign verify-blob` z przypiętą `certificate-identity` i `certificate-oidc-issuer`.

---

## 5. Core business logic responsibilities

Bez zmian funkcjonalnych w Core w ramach samego CI/CD planu.

Powiązania z innymi planami:

- CI/CD powinno uruchamiać testy dodane w planie AV hardening dla SHA256 Epic/DLL/Legendary.
- CI/CD powinno failować, jeśli testy hash verification nie przechodzą.
- CI/CD nie powinno wymagać prawdziwego Steam/Epic konta w automatycznych testach.
- E2E z realnym Steam/Epic zostaje manualnym release gate albo osobnym workflow manualnym bez secrets w MVP.

---

## 6. UI / Avalonia responsibilities

Brak bezpośrednich zmian UI w tym planie.

Pośrednio pipeline musi:

- buildować Avalonia project na Windows runnerze,
- nie wymagać designer/runtime interakcji,
- uruchamiać testy, które nie otwierają okien GUI bez potrzeby.

Jeżeli release workflow generuje release notes/changelog, UI copy pozostaje w locale files; workflow tylko waliduje obecność plików i bundling zasobów.

---

## 7. Language / i18n impact

- Brak nowych user-facing strings w aplikacji z samego CI/CD.
- Jeżeli workflow generuje release notes lub publiczny opis podpisu, powinien mieć PL/EN template albo jasną decyzję, że release notes są po polsku z krótkim angielskim security note.
- CI może dodać walidację i18n w przyszłości:
  - `pl.json` i `en.json` są poprawnym JSON,
  - klucze krytyczne istnieją w obu locale,
  - placeholdery są zgodne.
- Fallback locale `pl` pozostaje bez zmian.

---

## 8. Config and migration implications

- Brak migracji SQLite.
- Brak runtime writes do `appsettings.json`.
- Versioning powinien opierać się na:
  - tagu `vX.Y.Z` albo ręcznym input `version`,
  - generowaniu `version.json` w publish output,
  - niecommitowaniu wygenerowanych artefaktów.
- Sekrety GitHub Actions do rozważenia:
  - `SIGNPATH_API_TOKEN` / IDs/policies jako secrets/vars,
  - SSH/SFTP deploy key do `susmodder.app` dopiero po stabilizacji,
  - brak prywatnego certyfikatu PFX w MVP.
- Workflow files są repo config i powinny być reviewowane jak kod release-critical.

---

## 9. Platform, packaging, updater, telemetry, privacy, and AV constraints

### Platform

- Runner: `windows-latest` albo przypięty `windows-2022/2025` po sprawdzeniu .NET 10 support.
- Target: `win-x64`.
- Standard GitHub-hosted runner only.

### Packaging/updater

- `dotnet publish` z `-p:PublishSingleFile=false`.
- `vpk pack` dla jednego kanału na run:
  - `release`,
  - `beta`.
- Zachować strukturę backend-compatible z `susmodder.app/releases/{release|beta}/`.
- Weryfikować `RELEASES`, `RELEASES-{channel}` i manifesty.

### Signing/provenance

- Pipeline musi mieć punkt integracji dla SignPath jako preferred Authenticode path.
- Pipeline może mieć opcjonalny job Cosign dla provenance.
- Release output musi jasno rozróżniać:
  - Authenticode signed,
  - Sigstore provenance only,
  - unsigned.

### Telemetry/privacy

- CI logs nie mogą wypisywać tokenów, Authorization headers, SSH key, API tokenów, SignPath tokenów.
- Nie uploadować runtime user DB/logów z lokalnego środowiska.
- Support bundles/E2E logs tylko scrubbed, jeśli kiedyś trafią do CI.

### AV constraints

- CI-built artifacts mają być powtarzalne i identyfikowalne commit/tag -> artifact.
- Nie mieszać lokalnie budowanych plików z CI release.
- VirusTotal scan jako manualny gate po pobraniu finalnego artefaktu.
- Jeżeli workflow wrzuca artifacts do GitHub Release draft, skanować dokładnie te pliki.

---

## 10. Proposed workflow architecture

### 10.1 `ci.yml` — lightweight validation

Trigger:

- `pull_request`,
- push do `develop`/`main`.

Jobs:

- checkout,
- setup .NET 10 SDK,
- `dotnet restore SUSModder.sln`,
- `dotnet build SUSModder.sln -c Release --no-restore`,
- `dotnet test SUSModder.Core.Tests -c Release --no-build` where applicable,
- optional JSON/i18n validation.

Storage policy:

- no release artifacts,
- test logs only on failure,
- `retention-days: 1`.

### 10.2 `release-candidate.yml` — manual/tag release build

Trigger:

- `workflow_dispatch` first,
- later tags `v*.*.*` after stable.

Inputs:

- `version`,
- `channel` = `release|beta`,
- `publishTarget` = `none|github-draft|server`,
- `signingMode` = `none|signpath|cosign|both`.

Jobs:

1. Build/test gate.
2. Publish unpacked app.
3. Generate `version.json`.
4. Velopack pack.
5. Verify package shape and hashes.
6. Optional SignPath submit/sign/verify.
7. Optional Cosign sign-blob/bundle.
8. Upload to draft release or server.
9. Upload temporary workflow artifact only if needed, retention 1 day.

### 10.3 `cleanup-actions-artifacts.yml` — manual cleanup helper

Trigger:

- `workflow_dispatch` only.

Purpose:

- list old workflow artifacts,
- optionally delete artifacts older than N days,
- print current storage guidance.

Caution:

- GitHub notes storage/billing updates may lag; deletion frees current storage but does not necessarily reduce accrued usage immediately.

---

## 11. Verification plan

### Local dry-run before workflows

- Keep existing local script as reference until CI is proven:
  - `SKRYPTY/Build/build-dual-channel.ps1 -Version <version> -SkipBeta`.
- Compare output names/hashes between local and CI.

### CI verification

- First `ci.yml` run succeeds without artifacts.
- Release candidate run for beta channel succeeds with retention 1 day.
- Artifact storage after run stays below planned budget.
- `PublishSingleFile=false` verified in output.
- `Get-AuthenticodeSignature` result captured.
- Velopack package install smoke test passes manually.

### Signing/provenance verification

For SignPath:

- signed EXE verifies with Windows Authenticode tooling,
- publisher is expected SignPath Foundation / OSS policy identity,
- signed Velopack artifacts still install/update.

For Sigstore:

- `cosign verify-blob` passes using pinned workflow identity,
- bundle files are published and documented,
- release notes do not claim Windows Authenticode if only Sigstore is present.

### Storage verification

- Check GitHub Actions storage before/after release run.
- Confirm retention-days is 1 for large artifacts.
- Confirm no workflow uploads `publish/` directory by mistake.
- Confirm GitHub Packages is not accidentally used.

---

## 12. Suggested implementation order

### Phase 0 — storage cleanup and safety rails (P0)

1. Inspect current GitHub Actions/Packages storage usage and account-level minutes usage, including private repositories.
2. Delete stale artifacts/packages if possible.
3. Add repo rule: no artifact upload in PR workflows except failure logs.
4. Decide whether release assets on GitHub or `susmodder.app` are final storage.

Parallelizable: yes, can be done before workflow implementation.

### Phase 1 — lightweight CI (P0)

1. Create `.github/workflows/ci.yml`.
2. Build/test only, no publish artifacts.
3. Add JSON/i18n validation if cheap.
4. Confirm storage remains flat.

Parallelizable: independent from SignPath.

### Phase 2 — manual release-candidate workflow without signing (P0/P1)

1. Create `.github/workflows/release-candidate.yml` with `workflow_dispatch`.
2. Build one channel per run.
3. Use `PublishSingleFile=false`.
4. Package Velopack.
5. Upload minimal artifact with `retention-days: 1` or publish to draft release.

Parallelizable: after Phase 1.

### Phase 3 — SignPath feasibility integration (P1)

1. Apply/verify OSSign eligibility.
2. Create SignPath artifact configuration.
3. Add SignPath submit-signing-request step behind `signingMode=signpath`.
4. Verify signed output.

Parallelizable: SignPath account/policy setup can happen while Phase 2 is built.

### Phase 4 — Sigstore/Cosign provenance (P1/P2)

1. Add workflow permissions `id-token: write` only to release workflow/job.
2. Install Cosign.
3. Generate bundle signatures for release artifacts.
4. Publish verify commands in release notes.

Parallelizable: can happen before or after SignPath; lower priority than Authenticode.

### Phase 5 — deployment to `susmodder.app` (P1/P2)

1. Decide secure upload method: SSH/SFTP/rsync/GitHub Release only/manual server pull.
2. Add secrets only after workflow is stable.
3. Upload to channel folders.
4. Verify backend `/api/releases?channel=...` sees new manifest.

Parallelizable: server upload can remain manual initially to reduce risk.

---

## 13. Open questions

- Czy repo jest publiczne i ma OSI-approved license zgodną z SignPath Foundation rules?
- Czy obecne 0,5 GB zużycia to Actions artifacts, Packages, czy mieszany limit i czy pochodzi z SUSModder czy innych repozytoriów?
- Czy GitHub Release assets będą wystarczające jako final storage, czy `susmodder.app` musi być automatycznie aktualizowany?
- Czy release i beta mają być budowane w jednym runie, czy zawsze oddzielnie dla storage safety?
- Jak SignPath ma podpisać Velopack output: przed `vpk pack`, po `vpk pack`, czy przez artifact configuration obejmujące Setup/Update/app EXE?
- Czy bootstrapper ma osobne solution/workflow (`SUSModder.Bootstrapper.sln`) i czy musi być częścią MVP pipeline?
- Czy E2E z realnym Steam/Epic ma zostać manualne, czy osobny self-hosted/manual workflow?

---

## 14. Definition of done

- Istnieje lekki `ci.yml`, który nie generuje dużych artefaktów.
- Istnieje manualny `release-candidate.yml`, który buduje jeden kanał i generuje Velopack output z `PublishSingleFile=false`.
- Duże artifacts mają `retention-days: 1` albo są publikowane od razu poza Actions artifacts.
- Storage GitHub Actions po testowym runie mieści się w budżecie i nie rośnie stale.
- Pipeline ma przygotowany punkt integracji dla SignPath/OSSign.
- Opcjonalny Sigstore/Cosign jest opisany jako provenance, nie jako Authenticode replacement.
- Release output zawiera jasny signing/provenance status.
- Plan jest zlinkowany z AV false-positive hardening planem.

---

## 15. Sources used

- `mcp-rag` lookup dla istniejących release/Velopack/CI docs.
- Local files/docs:
  - `DOC/PLAN/2026-06-28-av-false-positive-release-hardening-plan.md`
  - `SKRYPTY/Build/build-dual-channel.ps1`
  - `SKRYPTY/Build/build-release-velopack.ps1`
  - `SKRYPTY/Build/README.md`
  - `DOC/PUBLISH_VELOPACK/RELEASE_GUIDE.md`
  - `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`
- GitHub Free pricing screenshot/context from user: 2 000 CI/CD minutes/month and 500 MB Packages storage for public repositories.
- SignPath/Sigstore public docs/search from AV hardening planning pass.




