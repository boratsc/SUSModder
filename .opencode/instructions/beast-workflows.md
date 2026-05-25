# Lean Agent Routing

This project uses a small practical agent roster. Prefer the agents that opencode actually routes to today instead of maintaining a large speculative set.

## Core Roster

- `explore`: repository exploration, file lookup, narrow codebase questions
- `librarian`: documentation lookup, README/plans, summaries, source references
- `sus-free-doc-scout`: low-cost broad documentation scans, stale-doc checks, and large-context markdown reading
- `sus-planner`: planning, architecture-aligned plan writing, and docs-first concept changes
- `sus-builder`: focused implementation, shipping verified changes
- `sus-core-backend`: SUSModder.Core business logic, game integration, mod operations
- `sus-ui`: Avalonia UI, views, view models, desktop presentation
- `sisyphus-junior`: general-purpose small implementation and glue tasks

## Review Roster

- `sus-free-light-reviewer`: lightweight free-model pass for small diffs and basic sanity
- `sus-quality-reviewer`: everyday medium change review (Qwen3.6 Plus Free)
- `sus-senior-quality-reviewer`: high-risk architecture/security/packaging review (GLM-5.1)
- `sus-security-auditor`: provider credentials, mod install safety, telemetry, file write safety
- `sus-i18n-copy-checker`: PL/EN copy, locale keys, hardcoded text, placeholder parity
- `sus-free-second-opinion`: non-critical pass after another reviewer (Nemotron 3 Super Free)
- `sus-free-plan-checker`: lightweight plan sanity before implementation
- `apollo`: premium escalation for very hard reasoning or final high-stakes review (DeepSeek V4 Pro)

## Routing Rules

- Small or trivial: `sisyphus-junior` or `quick` category
- Feature work: `sus-builder` or `deep` category
- .NET/Core work: `sus-core-backend` subagent
- Avalonia UI work: `sus-ui` subagent
- Review after implementation: `sus-quality-reviewer` (or `sus-senior-quality-reviewer` for high-risk changes)
- Planning: `sus-planner` or `prometheus`
- Free/pre-check: `sus-free-*` agents for cheap passes before spending stronger models
- Security: `sus-security-auditor`
- Premium/Pro: `apollo` or `pro` category
