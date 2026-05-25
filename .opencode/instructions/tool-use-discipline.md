# Tool Use Discipline

Use MCP servers and skills as part of the normal workflow, not as a last resort.

## Start-Of-Task Checklist

Before planning, reviewing, or editing, do a quick source selection:

- Repository structure, symbols, behavior, cross-file impact: use `mcp-rag` first, then verify exact files locally.
- SUSModder product direction: read the relevant plans or docs; use `mcp-rag` when you need semantic lookup across the repo.
- Large documentation scans: delegate the first read to `sus-free-doc-scout` when many markdown files may matter; use its summary as input, not as the final decision.
- External notes or project knowledge outside the repo: use `mcp-obsidian`.
- .NET, C#, MSBuild, platform APIs: use `microsoft-learn` for current docs.
- NuGet packages, versions, compatibility, vulnerabilities: use `nuget`.

If the appropriate MCP server or skill is unavailable, stale, or inconclusive, say that briefly in the working notes or final answer and use direct repo files or official docs as fallback.

## Required Habits

- Do not answer architecture, framework, package, or cross-file questions from memory when an MCP/source lookup is available.
- Do not edit .NET/MSBuild-sensitive code until you have checked project patterns and, when behavior is uncertain, Microsoft Learn or NuGet.
- During review, verify claims against files, POC docs, MCP results, or official docs before reporting them as findings.
- Prefer narrow lookups over broad browsing. Ask MCP/source tools one concrete question at a time.
- For documentation-heavy work, separate roles: `sus-free-doc-scout` gathers and summarizes broad context, `sus-planner` makes architecture/product/roadmap decisions, and `sisyphus-junior` handles only simple mechanical doc edits.

## Response Contract

For non-trivial work, include a short `Sources used` note in the final response or review summary. Mention source names such as `mcp-rag`, `microsoft-learn`, `nuget`, or local file paths.

For tiny mechanical edits, it is enough to use the sources silently if the answer would become noisy.
