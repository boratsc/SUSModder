# Plan: AV false-positive reduction, release build hardening i SHA256 verification

**Data:** 2026-06-28  
**Status:** Plan do implementacji przed kolejnym releasem  
**Priorytet:** P0 przed następnym publicznym artefaktem  
**Zakres:** SUSModder desktop Avalonia, `SUSModder.Core`, skrypty release, i18n PL/EN, backend-compatible z `susmodder.app`  
**Kontekst:** VirusTotal zgłosił pojedyncze detekcje false positive dla ZIP i EXE: Sangfor Engine Zero (`Trojan.Win32.Save.a`) oraz Trapmine (`Malicious.moderate.ml.score`). Obecny release flow jest unsigned, a aplikacja wykonuje typowe dla mod managera operacje: download, extract, copy/replace plików gry oraz uruchamianie zewnętrznych narzędzi. Dodatkowo zidentyfikowano darmowe ścieżki podpisywania dla OSS: SignPath Foundation/OSSign oraz Sigstore/Cosign.

---

## 1. Goal

Zmniejszyć ryzyko kolejnych false positive i poprawić bezpieczeństwo supply-chain przed kolejnym releasem przez:

- ujednolicenie publicznych buildów na `PublishSingleFile=false`,
- usunięcie sprzeczności między `.csproj` i helperami buildowymi,
- dodanie weryfikacji SHA256 dla `legendary.exe`, Epic full-mod ZIP oraz DLL modów,
- dodanie lokalizowanej informacji dla użytkownika o false positive / bezpieczeństwie pobrań,
- dodanie release-gate checks dla podpisu/unsigned statusu, single-file i zawartości paczek,
- sprawdzenie darmowego podpisywania OSS: SignPath Foundation/OSSign jako preferowane Authenticode oraz Sigstore/Cosign jako dodatkowa warstwa provenance.

## 2. Non-goals

- Nie kupujemy teraz płatnego certyfikatu code signing — zamiast tego weryfikujemy darmowe opcje OSS.
- Nie próbujemy „obchodzić” AV ani ukrywać zachowania aplikacji.
- Nie zmieniamy backend API poza wykorzystaniem istniejących pól/hash headers, jeśli są dostępne.
- Nie migrujemy dystrybucji na Microsoft Store/MSIX w tym pakiecie prac.
- Nie traktujemy Sigstore/Cosign jako pełnego zamiennika Windows Authenticode/SmartScreen, dopóki nie potwierdzimy realnego wsparcia przez Windows/AV dla naszych artefaktów.
- Nie usuwamy obsługi Epic/Legendary ani Steam/DepotDownloader.
- Nie zmieniamy zasad telemetry/privacy poza copy i ewentualnym doprecyzowaniem komunikatu.

---

## 3. Existing evidence / files to inspect

### Release/build

- `SUSModder/SUSModder.csproj`
  - Release defaults currently include `PublishSingleFile=true` and `IncludeNativeLibrariesForSelfExtract=true`.
- `SKRYPTY/Build/build-dual-channel.ps1`
  - canonical flow already passes `-p:PublishSingleFile=false`.
  - currently packs with Velopack without `--signTemplate`.
- `SKRYPTY/Build/build-release-velopack.ps1`
  - single-channel helper still uses `-p:PublishSingleFile=true`.
- `SKRYPTY/Build/README.md`
  - documents current unsigned flow because certificate expired.
- External signing options to evaluate:
  - SignPath Foundation/OSSign: free OSS program, certificate issued to SignPath Foundation, signing via trusted CI/build provenance and manual approval.
  - Sigstore/Cosign: keyless OIDC signing and Rekor transparency log; useful for provenance, but not confirmed as Windows Authenticode/SmartScreen replacement.

### Core download/install flows

- `SUSModder.Core/GameIntegration/ModManager.cs`
  - Steam full mod flow uses `ModDownloadUrlBuilder.ResolveWithHashAsync(...)` and verifies SHA256 in `DownloadFileWithMemoryManagementAsync(...)` when expected hash/header is available.
  - DLL mod flow uses `ResolveAsync(...)` + raw `HttpClient` download without equivalent SHA256 verification.
- `SUSModder.Core/GameIntegration/EpicVersionManager.cs`
  - Epic full mod flow uses `ResolveAsync(...)` + `DownloadFileAsync(...)` without equivalent SHA256 verification.
  - `DownloadLegendaryAsync()` downloads `legendary_windows_x86_64.exe` without pinned SHA256 verification.
- `SUSModder.Core/GameIntegration/Steam/DepotDownloaderRunner.cs`
  - good pattern: pinned version + hardcoded SHA256 constants + verify before extracting and executing.
- `SUSModder.Core/Utilities/ModDownloadUrlBuilder.cs`
  - source for resolved CDN/API download URL and expected hash model; inspect before extending Epic/DLL flows.

### i18n / UI

- `SUSModder/Localization/pl.json`
- `SUSModder/Localization/en.json`
- existing AV copy under `AntivirusWarning` and VirusTotal/modpack strings.
- likely UI surface: settings/about/security card, existing antivirus warning card, or release notes/FAQ link.

---

## 4. User workflow

### Release/download workflow after changes

1. User downloads installer/Velopack artifact from `susmodder.app`.
2. App is still unsigned until certificate is renewed, so SmartScreen/AV warnings may still happen.
3. App UI provides clear PL/EN explanation:
   - why mod managers can trigger false positive,
   - that SUSModder verifies downloaded mod artifacts with SHA256 when available,
   - that the app downloads only from configured SUSModder/GitHub tool sources,
   - where to view project/release integrity information.
4. During installation/update of mods:
   - Steam full mod download hash verification remains in place,
   - Epic full mod downloads verify SHA256 before extraction,
   - DLL mod downloads verify SHA256 before copy into selected mods,
   - Legendary download verifies a pinned SHA256 before first execution.
5. On hash mismatch:
   - app deletes partial/suspect file,
   - installation is blocked,
   - UI shows a localized error with stable error code and technical fallback.

### Developer/release workflow after changes

1. Run canonical build script.
2. Build fails if release accidentally uses single-file mode.
3. Build/release checklist confirms:
   - `PublishSingleFile=false`,
   - generated package has expected unpacked layout,
   - all known downloadable executables have pinned hash or are signed by upstream where feasible,
   - unsigned status is explicit and documented.
4. VirusTotal scan is done on final artifacts and links are recorded in release notes/checklist.

---

## 5. Core business logic responsibilities

### 5.1 SHA256 verification parity

Implement one consistent download contract for all mod artifacts:

- Prefer `ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, platform)` where artifact metadata can include expected SHA256.
- Reuse or extract the existing verification pattern from `ModManager.DownloadFileWithMemoryManagementAsync(...)`.
- Ensure `expectedSha256` may come from:
  - API/catalog metadata,
  - `X-SUSModder-SHA256` response header,
  - pinned constants for third-party tools.

Required behavior:

- hash match: continue install,
- hash missing: continue only if product decision allows; log warning and surface weaker security status if needed,
- hash mismatch: delete file, fail install, do not extract or execute.

### 5.2 Legendary hardening

Use `DepotDownloaderRunner` as pattern:

- pin Legendary version currently downloaded in `EpicVersionManager.DownloadLegendaryAsync()`,
- add constant `LegendaryWindowsX64Sha256`,
- download to `.tmp.<guid>`,
- verify SHA256,
- move into final `legendary.exe` only after verification,
- never execute unverified downloaded executable.

Open decision:

- verify exact SHA256 for `https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.41/legendary_windows_x86_64.exe` before implementation and record it in code comment/release plan.

### 5.3 Epic full mod hardening

In `EpicVersionManager.ModifyEpicAsync(...)`:

- switch from `ResolveAsync(...)` to hash-aware resolution if available,
- pass expected hash into download helper,
- verify before `SharpCompressExtractor.ExtractAsync(...)`,
- on mismatch return failure before deleting/recreating final game directory if possible.

Important sequencing:

- download + verify into temp first,
- extract into temp,
- only then replace target `AmongUs` folder.

### 5.4 DLL mod hardening

In `ModManager.InstallDllModAsync(...)`:

- use hash-aware resolution for DLL artifacts,
- verify downloaded `mod.dll` before copying into any selected full mod,
- on mismatch block all selected target writes.

### 5.5 Error model

Core should expose stable, localizable failure reasons where possible:

- `download_hash_mismatch`,
- `download_hash_missing` if treated as warning/error,
- `tool_hash_mismatch`,
- `tool_download_failed`,
- `artifact_verification_failed`.

Avoid only Polish/English raw strings from Core for new user-facing failures. Technical fallback can remain for logs.

---

## 6. UI / Avalonia responsibilities

- Show localized hash-verification failures in install/update dialogs.
- Add or extend a security/AV explanation surface:
  - existing antivirus warning card if appropriate,
  - settings/about section,
  - release notes link/card.
- Copy should be factual, not defensive:
  - “Niektóre antywirusy mogą oznaczać mod manager jako podejrzany, bo pobiera i rozpakowuje pliki gry.”
  - “SUSModder weryfikuje integralność pobranych paczek SHA256, gdy backend dostarcza hash.”
  - “Aplikacja jest obecnie niepodpisana; podpis wróci w przyszłości po odnowieniu certyfikatu.”
- Avoid modal spam. Security info should be available and shown contextually on relevant failures/warnings.

---

## 7. Language / i18n impact

MVP locales: `pl`, `en`. Fallback: `pl`.

Required keys in both locale files:

- security/AV explanation title/body,
- unsigned-build note,
- SHA256 verification success/failed/missing labels if user-visible,
- hash mismatch error for mod ZIP,
- hash mismatch error for DLL,
- hash mismatch error for Legendary/third-party tool,
- optional “open VirusTotal report” / “learn more” labels if surfaced.

Rules:

- No new hardcoded user-facing strings in `.axaml`, ViewModels, or Core responses.
- Placeholders must match between PL and EN, e.g. `{fileName}`, `{expectedHash}`, `{actualHash}` if shown.
- Do not show full hashes in primary UX unless useful; prefer details expander/logs.
- Future locales must be addable by adding locale JSON entries only.
- Product/tool names remain untranslated: `SUSModder`, `Among Us`, `Steam`, `Epic Games`, `BepInEx`, `legendary`, `DepotDownloader`, `VirusTotal`, `SHA256`.

Suggested copy direction:

### PL

- Title: `Bezpieczeństwo pobierania i antywirusy`
- Body: `SUSModder pobiera i rozpakowuje mody do Among Us, dlatego pojedyncze silniki antywirusowe mogą czasem zgłosić fałszywy alarm. Przed instalacją aplikacja weryfikuje integralność obsługiwanych paczek za pomocą SHA256. Obecne wydania są niepodpisane do czasu odnowienia certyfikatu code signing.`

### EN

- Title: `Download security and antivirus warnings`
- Body: `SUSModder downloads and extracts Among Us mods, so individual antivirus engines may sometimes report a false positive. Before installation, the app verifies supported packages with SHA256 integrity checks. Current builds are unsigned until the code signing certificate is renewed.`

---

## 8. Config and migration implications

- No SQLite schema migration required.
- No runtime writes to `appsettings.json`.
- No new user setting required for MVP.
- Build configuration changes are repo/build-time only:
  - `.csproj` Release defaults should align with Velopack: `PublishSingleFile=false`,
  - scripts should explicitly pass `PublishSingleFile=false`,
  - legacy single-file helper should be renamed/archived or changed to non-single-file.
- If backend metadata lacks SHA256 for some artifacts, document gap in release checklist and decide whether to block or warn.

---

## 9. Platform, packaging, updater, telemetry, privacy, and AV constraints

### Platform

- Windows x64 public release remains target.
- Steam/Epic flows must remain supported.
- Third-party tool execution remains necessary but must be hash-verified before first run.

### Packaging/updater

- Velopack requires unpacked output for delta updates; `PublishSingleFile=false` is the expected release shape.
- Keep canonical release outputs under `releases-release/` and `releases-beta/`.
- ZIP/portable artifacts should be avoided unless explicitly required; if produced, they must be scanned and include the same verified/unpacked layout.

### Signing status and free OSS signing options

Paid certificate renewal is deferred, but signing should no longer be treated as blocked by budget only. Evaluate and, if accepted, implement one of the free OSS paths:

#### Preferred path: SignPath Foundation / OSSign

- Goal: real Windows Authenticode/code-signing signature without purchasing a private certificate.
- Model: build artifacts are produced by GitHub Actions, uploaded as workflow artifacts, submitted to SignPath, signed by SignPath Foundation's certificate, then returned to the release workflow.
- Expected AV/SmartScreen impact: stronger than unsigned builds because PE files carry a trusted CA-backed Authenticode signature and origin is tied to CI provenance.
- Requirements to verify for SUSModder:
  - public OSS repository and OSI-approved license,
  - no proprietary/closed-source components in the signed artifact except allowed system libraries/upstream binaries under policy,
  - active maintenance and documented download page,
  - builds from GitHub-hosted CI runners,
  - MFA for maintainers,
  - release/signing approval process,
  - artifact configuration covering Velopack output and every PE file requiring signature.
- Files to sign/gate: `SUSModder.exe`, Velopack `Update.exe`, setup/bootstrapper EXE, and any bundled EXE if present.

#### Supplemental path: Sigstore / Cosign

- Goal: keyless provenance for release artifacts using GitHub Actions OIDC, Fulcio certificate and Rekor transparency log.
- Use case: publish `.sigstore`/bundle signatures for nupkg/ZIP/installer artifacts and document verification commands.
- Caveat: Sigstore blob signatures are not automatically equivalent to Windows Authenticode signatures. Do not assume they remove SmartScreen warnings or AV ML detections for EXE files.
- Decision rule: use Sigstore as an additional supply-chain transparency layer even if SignPath becomes the Authenticode solution.

Release notes/checklist should state one of:

- `Signed with SignPath Foundation/OSSign` after successful integration, or
- `Unsigned Authenticode; Sigstore provenance available` if only Cosign is implemented, or
- `Unsigned; signing pending` if neither path is ready.

### Telemetry/privacy

- No new telemetry event required.
- Do not send raw hashes, file paths, usernames, Epic/Steam credentials, or modpack private data.
- Existing telemetry locale should remain canonical `pl`/`en` only.
- Logs may include expected/actual SHA256 for troubleshooting but must not include tokens/auth headers.

### AV constraints

- Avoid single-file/self-extract for public release.
- Avoid downloading executable files without pinned hash verification.
- Avoid extracting into ambiguous temp locations when a controlled app temp path is available.
- Keep temp cleanup, but do not “delete evidence” in a malware-like way; logs should clearly explain operations.
- Submit final artifacts to VirusTotal and vendors when a false positive appears.

---

## 10. Verification plan

### Build verification

- `dotnet build SUSModder.sln -c Release`
- canonical release script dry/local run:
  - `SKRYPTY/Build/build-dual-channel.ps1 -Version <next> -SkipBeta`
- Verify publish output is not single-file:
  - output contains managed DLLs next to `SUSModder.exe`,
  - `SUSModder.exe` size is not suspiciously large compared to single-file output,
  - no `.net` self-extract expectation from release artifact.
- Inspect generated nupkg/portable ZIP contents.
- Check `Get-AuthenticodeSignature` output and record unsigned status as expected until certificate renewal.

### Core verification

- Unit tests for hash verification helper:
  - match,
  - mismatch deletes temp/fails,
  - missing hash behavior,
  - malformed hash behavior.
- Unit tests for Legendary downloader with mocked HTTP/file IO if feasible.
- Unit tests or integration tests for Epic/DLL hash-aware resolution.
- Existing tests:
  - `dotnet test SUSModder.Core.Tests`
  - relevant E2E smoke tests for API download SHA256/extract.

### Manual QA

- Steam full mod install still works.
- Epic full mod install still works.
- DLL mod install into selected full mods still works.
- Simulated hash mismatch blocks before extraction/copy.
- First-run Legendary download verifies and then executes.
- PL/EN UI copy displays correctly and language switch still works.

### AV/release verification

- Scan final `SUSModder.exe`, Setup/Update EXE, nupkg/ZIP if produced.
- Record VirusTotal links in release notes/internal checklist.
- If 1–2 engines flag only heuristic/ML detections, submit false-positive reports before broad announcement.

---

## 11. Suggested implementation order


### Phase 0 — Free OSS signing feasibility (P0, can start now)

1. Confirm SUSModder repository/license eligibility for SignPath Foundation/OSSign.
2. Identify artifact shape to submit: Velopack setup/update package, app EXE, `Update.exe`, optional ZIP/nupkg.
3. Create SignPath project/signing policy proposal and required GitHub Actions workflow design.
4. Evaluate Sigstore/Cosign as supplemental release artifact provenance and document verify commands.
5. Decide release gate wording: signed by SignPath, Sigstore-only provenance, or unsigned pending approval.

Parallelizable: yes, independent from Core hash work and build shape hardening.

### Phase 1 — Build shape hardening (P0, can start now)

1. Change Release defaults in `SUSModder.csproj` to `PublishSingleFile=false` or split public release property from explicit dev single-file publish.
2. Change `build-release-velopack.ps1` to `PublishSingleFile=false` or mark/archive it so it cannot accidentally produce public single-file release.
3. Add build-script validation that fails if public publish output appears single-file.
4. Update `SKRYPTY/Build/README.md` with explicit unsigned + non-single-file release rule.

Parallelizable: yes, independent from Core hash work.

### Phase 2 — Shared hash verification helper (P0)

1. Extract/reuse SHA256 verification from `ModManager.DownloadFileWithMemoryManagementAsync(...)` into a reusable Core utility/service if appropriate.
2. Add tests for match/mismatch/missing hash.

Parallelizable: partly; should land before Epic/DLL/Legendary integration.

### Phase 3 — Epic and DLL hash verification (P0)

1. Update Epic full mod download to use hash-aware resolution and verify before extraction/target replacement.
2. Update DLL mod download to verify before copying to any selected full mod.
3. Add localized errors / stable error codes.

Parallelizable: Epic and DLL flows can be implemented separately after shared helper decision.

### Phase 4 — Legendary pinning (P0/P1)

1. Confirm SHA256 of the exact Legendary binary version.
2. Add pinned constant and temp-download-verify-move flow.
3. Add test or manual verification checklist.

Parallelizable: yes, after helper decision or using DepotDownloader pattern directly.

### Phase 5 — i18n/UI copy (P1)

1. Add PL/EN locale keys.
2. Add/extend UI surface for security/AV explanation.
3. Map new hash/tool failure codes to localized messages.
4. Run i18n placeholder parity review.

Parallelizable: copy keys can start early; final error mapping depends on Core error codes.

### Phase 6 — Release gate update (P0)

1. Link this plan from 3.0 stable E2E release gate or add checklist items there.
2. Add manual VirusTotal/vendor submission procedure to release checklist.
3. Re-run build/test/scan before next release.

---

## 12. Open questions

- Does backend API v2 always provide SHA256 for Steam, Epic and DLL artifacts, or only some variants?
- Should missing SHA256 block installation or warn-and-continue for legacy catalog entries?
- What is the exact SHA256 for the currently pinned Legendary 0.20.41 Windows binary?
- Where should the security explanation live: Settings/About, existing antivirus warning card, or release notes dialog?
- Should portable ZIP continue to be published at all, or only Velopack installer/packages?
- Does SUSModder meet all SignPath Foundation/OSSign conditions, especially license, no proprietary components, and CI-only verifiable builds?
- Can SignPath sign the exact Velopack artifact layout we publish, including Update.exe/Setup/app EXE?
- Should Sigstore/Cosign be implemented even if SignPath is accepted, as extra provenance for nupkg/ZIP artifacts?
- How should release notes distinguish Authenticode signing from Sigstore provenance without misleading users?
- If free OSS signing is rejected or delayed, should we later use OV certificate again or evaluate Azure Trusted Signing / Microsoft Store path?

---

## 13. Definition of done

- Public release build path cannot accidentally produce single-file/self-extract artifact.
- Epic mod ZIP, DLL mod download, DepotDownloader and Legendary have hash verification before extraction/execution/copy.
- Hash mismatch blocks install and deletes suspect artifact.
- PL/EN localized copy exists for new user-facing security/error strings.
- Release checklist records actual signing/provenance status: SignPath Authenticode, Sigstore-only, or unsigned pending; plus VirusTotal scan links and false-positive submission procedure.
- `dotnet build` and relevant unit/E2E tests pass.
- SignPath/OSSign feasibility is decided and documented; if accepted, GitHub Actions signing workflow is planned before the next public release.

---

## 14. Sources used

- `mcp-rag` repo lookup for release/Velopack/signing/download flows.
- `microsoft-learn` for .NET single-file extraction and Windows code signing/SmartScreen behavior.
- Web search / public docs for SignPath Foundation OSS conditions, SignPath GitHub Actions trusted build system, and Sigstore/Cosign keyless signing caveats.
- Local files:
  - `SUSModder/SUSModder.csproj`
  - `SKRYPTY/Build/README.md`
  - `SKRYPTY/Build/build-dual-channel.ps1`
  - `SKRYPTY/Build/build-release-velopack.ps1`
  - `SUSModder.Core/GameIntegration/ModManager.cs`
  - `SUSModder.Core/GameIntegration/EpicVersionManager.cs`
  - `SUSModder.Core/GameIntegration/Steam/DepotDownloaderRunner.cs`
  - `SUSModder.Core/GameIntegration/SteamVanillaProvider.cs`
  - `SUSModder/Localization/pl.json`
  - `SUSModder/Localization/en.json`





