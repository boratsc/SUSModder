# MCP Routing Policy

Agents must treat MCP servers as first-class context sources, not optional extras. Before answering from memory, check whether one of the project MCP servers is the better source. If a relevant server exists, use it or explicitly note why it could not be used.

## Available Sources

- `mcp-rag`: first choice for SUSModder codebase discovery, semantic lookup, architecture questions, cross-file impact analysis, and "where is X implemented?" work.
- `mcp-obsidian`: first choice for external notes, personal knowledge base material, planning notes outside the repo, and product/roadmap context that lives in Obsidian.
- `microsoft-learn`: first choice for current Microsoft documentation, especially .NET, MSBuild, AOT, trimming, SDK/runtime behavior, C#, and platform APIs.
- `nuget`: first choice for NuGet package metadata, dependency updates, package compatibility, known vulnerabilities, and package-version questions.
- `context7`: use when available for up-to-date third-party library documentation, especially .NET libraries and general programming topics.

## Default Habits

- For repository understanding: use `mcp-rag` first, then verify with direct file reads.
- For project/product planning: check `DOC/POC` and `DOC/PLAN`; use `mcp-obsidian` when the relevant source is outside the repo.
- For .NET/MSBuild/AOT/NuGet work: consult `microsoft-learn` or `nuget` before relying on memory.
- For frontend/Avalonia work: consult `microsoft-learn` for Avalonia documentation, or `context7` for third-party .NET libraries.
- If an MCP lookup is stale, unavailable, or inconclusive, fall back to local files or web/CLI sources and say which fallback was used.
- For non-trivial work, mention the important MCP/source lookups in the final response or review summary under a short `Sources used` note.

## Agent-Specific Bias

- `explore`: prefer `mcp-rag`; only use docs MCPs when code lookup turns into framework/package questions.
- `librarian`: prefer `mcp-obsidian`, `microsoft-learn`, `nuget`, `context7`, and repo docs; cite concrete paths or source names.
- `sisyphus-junior`: before edits, use the relevant MCP source for the area being changed; keep lookups narrow and implementation scoped.
- `momus`: during review, use MCPs to verify facts, docs claims, package behavior, and architectural alignment rather than reviewing from memory alone.
- `apollo`: use all relevant MCP sources before spending premium reasoning on uncertain assumptions.
