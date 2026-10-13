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

## Plugins evaluated (13 October 2026)

Plugins are installed for the user, so they apply to **every** project on this PC, including GravelMaster.

| Plugin | Publisher | What it can reach | Verdict | Why |
|---|---|---|---|---|
| **OmniRoute** | Community (diegosouzapw). Not a plugin: an AI gateway | Every prompt, file Claude reads and reply goes through a server you run, then on to other model providers (GLM, Kimi, DeepSeek, or Claude via an OAuth route) | **Rejected** | Sends code and conversations to third-party providers with no stated retention policy. If set globally it would also carry GravelMaster sessions. Using a Claude subscription through a proxy may break Anthropic's terms. Weaker models are a poor fit for correctness-critical scheduling code. |
| **microsoft-docs** | Microsoft (partner) | Remote: sends search queries (not code) to Microsoft Learn; no login | **Recommended** | Current official docs for .NET 10, ASP.NET Core MVC, EF Core and Identity: exactly this stack |
| **security-guidance** | Anthropic | Hooks on edits, prompts and end of turn: local pattern checks plus an AI review of changes | **Recommended** | Flags injection, XSS and hardcoded secrets as code is written. Matters from Phase 2 (accounts, personal data). Costs some tokens each turn. |
| context7 | Upstash (partner) | Remote; needs a Context7 login | Later (Phases 3 and 5) | Current docs for NodaTime and FullCalendar. Web fetch covers it until then. |
| frontend-design | Anthropic | Instructions only | Later (Phase 5) | UI guidance when the calendar is built |
| csharp-lsp | Anthropic (official marketplace) | Runs the `csharp-ls` language server locally | Later (decide in Phase 2) | Live C# errors and go-to-definition, but needs `dotnet` and `csharp-ls` on PATH, and Windows problems have been reported. `ft-dev check` gives the same errors at build time. |
| playwright | Microsoft (partner) | Drives a browser through Node | No | Needs Node. The built-in browser covers manual checks; automated browser tests, if wanted, would use the .NET Playwright package. |
| Superpowers | Jesse Vincent (partner) | A start-of-session hook in every project | No, for now | Good TDD and debugging skills, but its workflow (subagents editing in worktrees) clashes with ours, and its hook applies to every project |
| commit-commands | Anthropic | Runs git, including push | No | We never push, and we have our own commit message format |
| code-simplifier | Anthropic | One agent | No | Already included in pr-review-toolkit |
| Frugal, Wave Planning | Community | Hooks and agents in every session | No | Model-routing plugins: same aim as OmniRoute, within Claude. Model choice is already in the app's model picker. |

## Adding a skill, agent or plugin later

Before suggesting one, state plainly:
- who publishes it, and whether its source can be read;
- what it can reach: instructions only (safest), commands that run on this PC, hooks, or connectors that send data
  outside;
- whether it needs logins or keys (the owner enters those, never Claude).

Third-party skills are read in full, then copied in with their licence, never installed with an installer that runs
someone else's code. Each one is listed in the table above.
