# SUSModder Retrieval Policy

When working inside the SUSModder project, prefer `mcp-rag` as the first retrieval layer for repository exploration.

Rules:

- Treat `mcp-rag` as the default first step for codebase discovery, semantic lookup, architecture questions, cross-file impact analysis, and "where is X implemented?" tasks.
- Use `mcp-rag` before broad filesystem search when the task is about understanding structure, finding relevant files, locating symbols by meaning, or summarizing existing behavior.
- After `mcp-rag` returns candidate files, use direct file reads and targeted `grep`/`glob` only to verify exact code, confirm line-level details, inspect neighboring context, or make edits.
- If `mcp-rag` returns weak, stale, or incomplete results, fall back to direct repository search and say that the lookup was completed via filesystem tools instead.
- Do not use `mcp-obsidian` for code lookup unless the task is explicitly about notes, docs outside the repo, or knowledge-base material.
- Before implementing a feature, bug fix, or refactor that touches product behavior or architecture, check the relevant existing code and patterns first.
- If the task requires changing the architecture or product behavior significantly, do not implement immediately. First do planning, then update plans, then implement.

Preferred workflow:

1. Query `mcp-rag` for SUSModder entities, flows, symbols, or behavior.
2. Understand existing patterns before deciding on implementation direction.
3. Narrow to concrete files in this repo.
4. Verify with local reads/search.
5. If the work changes the agreed concept: update plans first.
6. Edit only after verification and alignment.
