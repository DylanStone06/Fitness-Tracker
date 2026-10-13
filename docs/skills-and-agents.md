# Skills and agents

How Claude Code is set up for this project, and why. Only the main session edits files; every agent is read-only
and reports back, so two agents can never change the same file.

## Project skills (`.claude/skills/`)

Copied unchanged from `GravelMasterMVC\.claude\skills` on 13 October 2026, each with its licence. They are instructions
only: no scripts, hooks, `allowed-tools` or network calls.

| Skill | From | Licence | Use it for | Project overrides (CLAUDE.md wins) |
|---|---|---|---|---|
| `dotnet-patterns` | Affaan Mustafa, Everything Claude Code | MIT | C# conventions, DI, async, options, results | Use MVC controllers, not Minimal APIs. Apply it to new code; don't rewrite working code just for style. |
| `csharp-testing` | Affaan Mustafa, Everything Claude Code | MIT | xUnit structure, naming, builders, integration tests | Use xUnit `Assert`, not FluentAssertions. Use in-memory SQLite, not EF's in-memory provider. No Testcontainers (no Docker). |
| `accessibility` | Affaan Mustafa, Everything Claude Code | MIT | WCAG 2.2 AA for every screen | Web sections only. |
| `break-ui` | Emil Kowalski | MIT | Worst-case data for calendar and list screens | Fixtures use made-up people and `example.com`. The worst-case toggle is development-only. |

**Not copied:**
- `gravelmaster`: house rules for the shop. Its useful habits are in `CLAUDE.md` instead.
- `review-animations`: this app needs very little motion.
- `emil-design-eng`: it may be added for a polish pass after the MVP.

## Planned project additions (Phase 3)

These are created once the scheduling rules exist in code, so they describe real behaviour, not guesses.

| Item | Type | Purpose | Inputs → outputs | Tools | How its work is checked |
|---|---|---|---|---|---|
| `scheduling-rules` | Skill | Reference for footprints, hard and soft constraints, scoring weights, time rules and the T-scenario catalogue | Loaded when touching `Domain/Scheduling`, `Domain/Recurrence`, `Domain/Time` or sleep code | None | Every scenario in it exists as a passing test |
| `scheduling-reviewer` | Agent (`.claude/agents/`) | Independent review of recurrence, DST, travel, sleep and planner changes; proposes missing edge-case tests | The changes → findings by severity with a failing-test sketch for each | `Read`, `Grep`, `Glob` only; preloads `scheduling-rules` | Each accepted finding becomes a failing test, then a fix |

## Already available (user-level plugins and built-ins)

| Need | Use | When |
|---|---|---|
| Architecture for a phase | `Plan` agent or `feature-dev:code-architect` | Start of Phases 6 and 9, or any design with real trade-offs |
| Review every change | `/code-review` | Before each commit with logic in it |
| Milestone review | `pr-review-toolkit`: `code-reviewer`, `silent-failure-hunter`, `pr-test-analyzer`, `type-design-analyzer` | End of each phase |
| Security | `/security-review` per sensitive change; `claude-security` full scan (uses a lot of tokens, so ask first) | Auth and data changes; Phase 9 |
| Simplify | `/simplify` | After a large change |
| Run and see the app | `/run`, built-in browser | UI phases |
| Charts | `dataviz` skill | After the MVP (analytics) |
| AI planning | `claude-api` skill | After the MVP (AI assistance) |

## The loop for each change

1. Agree the change and how we'll know it works.
2. The main session writes the tests and code.
3. Run `dev\ft-dev.ps1 check`.
4. Review: `/code-review`, plus `scheduling-reviewer`, `break-ui` or `accessibility` where they apply.
5. Commit locally (one commit per change). The owner pushes from GitHub Desktop.

## Adding a skill, agent or plugin later

Before suggesting one, state plainly:
- who publishes it, and whether its source can be read;
- what it can reach: instructions only (safest), commands that run on this PC, hooks, or connectors that send data
  outside;
- whether it needs logins or keys (the owner enters those, never Claude).

Third-party skills are read in full, then copied in with their licence, never installed with an installer that runs
someone else's code. Each one is listed in the table above.
