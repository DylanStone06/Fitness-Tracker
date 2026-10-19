# Skills and agents

How Claude Code is set up for this project, and why. Only the main session edits files; every agent is read-only
and reports back, so two agents can never change the same file.

## Project skills (`.claude/skills/`)

All are copied unchanged, each with its licence, and are instructions only: no scripts, hooks, `allowed-tools` or
network calls. The first four came from `GravelMasterMVC\.claude\skills`. The last two came from
[obra/superpowers](https://github.com/obra/superpowers) at commit `bb92a777` (10 October 2026), without the plugin
or its start-of-session hook. All were copied on 13 October 2026.

| Skill | From | Licence | Use it for | Project overrides (CLAUDE.md wins) |
|---|---|---|---|---|
| `dotnet-patterns` | Affaan Mustafa, Everything Claude Code | MIT | C# conventions, DI, async, options, results | Use MVC controllers, not Minimal APIs. Apply it to new code; don't rewrite working code just for style. |
| `csharp-testing` | Affaan Mustafa, Everything Claude Code | MIT | xUnit structure, naming, builders, integration tests | Use xUnit `Assert`, not FluentAssertions. Use in-memory SQLite, not EF's in-memory provider. No Testcontainers (no Docker). |
| `accessibility` | Affaan Mustafa, Everything Claude Code | MIT | WCAG 2.2 AA for every screen | Web sections only. |
| `break-ui` | Emil Kowalski | MIT | Worst-case data for calendar and list screens | Fixtures use made-up people and `example.com`. The worst-case toggle is development-only. |
| `test-driven-development` | Jesse Vincent, Superpowers | MIT | Red-green-refactor for domain logic, services, controllers and bug fixes; `writing-good-tests.md` for test quality | Examples are TypeScript: use xUnit and `dev\ft-dev.ps1 test`. "Delete code written before the test" applies only to the current change. Exempt: scaffolding, EF migrations, Razor views, CSS, vendored libraries. Its link to `superpowers:writing-skills` points at a skill we don't have. |
| `verification-before-completion` | Jesse Vincent, Superpowers | MIT | No "done", "fixed" or "passing" without fresh evidence from this turn | None. It matches `CLAUDE.md` (we never push, so its push wording doesn't apply). |

**Not copied:**
- `gravelmaster`: house rules for the shop. Its useful habits are in `CLAUDE.md` instead.
- `review-animations`: this app needs very little motion.
- `emil-design-eng`: it may be added for a polish pass after the MVP.

## Project additions (Phase 3)

Written for this project on 14 October 2026, once the time and recurrence rules existed in code, so they describe
real behaviour, not guesses. Both are instructions only. The skill marks the parts not built yet (footprints,
constraints, scoring, sleep) and says to read the requirements rather than guess; it grows as each phase lands.

| Item | Type | Purpose | Inputs → outputs | Tools | How its work is checked |
|---|---|---|---|---|---|
| `scheduling-rules` | Skill (`.claude/skills/scheduling-rules/`) | Reference for the time, recurrence and template-cycle rules as built (footprints, constraints and scoring added as they are built) and the T-scenario catalogue | Loaded when touching `Domain/Time`, `Domain/Recurrence`, `Domain/Templates`, and later `Domain/Scheduling` or sleep code | None | Every scenario it marks as covered names a passing test |
| `scheduling-reviewer` | Agent (`.claude/agents/scheduling-reviewer.md`) | Independent review of recurrence, DST, travel, sleep and planner changes; proposes missing edge-case tests | The changes → findings by severity with a failing-test sketch for each | `Read`, `Grep`, `Glob` only; preloads `scheduling-rules` (and is told to read it) | Each accepted finding becomes a failing test, then a fix |

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
2. The main session writes a failing test, watches it fail, then writes the code (`test-driven-development`).
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
| Superpowers | Jesse Vincent (partner) | A bash start-of-session hook in every project, which injects a skill telling Claude to check for skills before every reply | **Plugin: no. Two skills copied** (above) | The hook needs bash, which this PC doesn't have. Its forcefully worded bootstrap would add process to every reply in every project, including GravelMaster (where it conflicts with committing straight to `master`). It also duplicates our plan/review workflow. Its `test-driven-development` and `verification-before-completion` skills are self-contained, so they were copied without the plugin. |
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
