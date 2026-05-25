# SUSModder i18n / language discipline

This project is multilingual from the start. Treat language support as a first-class requirement in every feature, not as polish at the end.

## Product baseline

- MVP locales: **Polish (`pl`)** and **English (`en`)**.
- Fallback locale: **Polish (`pl`)**.
- First Launch Wizard step 1 must include language selection next to Steam/Epic platform choice.
- Future community languages must be addable through locale resources/metadata, not by changing component logic.
- The POC language is Polish, but the app UI must ship with PL and EN user-facing copy.

## Required habit for every agent

For every non-trivial plan, implementation, or review, explicitly check the language impact:

- Are all new user-facing strings represented as i18n keys?
- Do PL and EN both exist for each key?
- Do placeholders match across locales (`{modName}`, `{version}`, `{count}`)?
- Does pluralization use ICU MessageFormat when counts are shown?
- Are error surfaces based on stable error codes that the UI can localize?
- Does the feature affect the first-launch language/platform step, Settings language switcher, telemetry `language`, updater copy, or privacy copy?
- Can a future locale be added by adding a locale file/metadata only?

If the answer is "not applicable", say why briefly in the plan or review.

## Implementation rules

- No hardcoded user-facing copy in Views (`.axaml`), ViewModels, or Core responses except temporary scaffolding explicitly marked and tracked.
- Names such as `SUSModder`, `Among Us`, `Steam`, `Epic Games`, `BepInEx`, `UMU`, `DepotDownloader`, and `legendary` remain product/tool names and do not need translation.
- Dynamic mod names, author names, versions, file paths, and backend-provided catalog content are data, not translation keys.
- Core should prefer stable `errorCode` + technical fallback message; frontend maps known codes to localized strings.
- Telemetry may send only canonical app locale (`pl`/`en`), not raw system locale.
- Do not introduce runtime translation downloads in MVP; bundle locale resources with the app to preserve offline behavior and AV/reputation simplicity.

## Review rules

- UI changes should be checked by `sus-i18n-copy-checker` or a reviewer using this checklist.
- Architecture/config/IPC changes involving locale persistence, fallback, telemetry language, or i18n framework changes should be reviewed by `sus-senior-quality-reviewer`.
- Missing translations, placeholder mismatches, English-only copy, Polish-only copy, or unlocalized user-facing Core errors are review findings.

## Source of truth

- Detailed implementation plan: `DOC/PLAN/2026-04-26-i18n-pl-en-wizard.md`.
- Conceptual frontend/i18n POC: `DOC/POC/SUSModder-3.0/09-frontend.md`.
- Architecture/config/IPC context: `DOC/POC/SUSModder-3.0/03-architektura.md`.
- Telemetry language field: `DOC/POC/SUSModder-3.0/12-telemetria.md`.
