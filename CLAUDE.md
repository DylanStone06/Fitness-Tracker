# Fitness Tracker

A constraint-aware weekly planner that fits fitness around work, commuting, sleep, travel and recovery, and says
plainly when a week's goals won't fit. Personal project (github.com/DylanStone06/Fitness-Tracker); MIT licence.

| What | Where |
|---|---|
| Requirements, IDs (ACC-, CAL-, SCH-…) and test scenarios (T1–T24) | `docs/requirements.md` |
| Stack and the reasons for it | `docs/decisions/0001-stack.md` |
| Phases, current status, definition of done | `docs/roadmap.md` |
| How the parts fit, and the data-isolation rules for new code | `docs/architecture.md` |
| What's missing or imperfect, and when it's planned | `docs/known-limitations.md` |
| Skills, agents and the review loop | `docs/skills-and-agents.md` |

The person asking wants plain English. Explain what you're doing and why, say what was and wasn't checked, and never
hide a problem to make work look finished.

## Stack

ASP.NET Core MVC on .NET 10, EF Core + SQLite, ASP.NET Core Identity, NodaTime, FullCalendar v6 (self-hosted, no npm),
xUnit. Don't add a framework, database, package or front-end library without saying why and getting a yes.

## Tools on this PC (none are on PATH)

- dotnet: `%LOCALAPPDATA%\GravelMasterDev\dotnet\dotnet.exe` (SDK 10.0.401)
- git: `%LOCALAPPDATA%\GitHubDesktop\app-*\resources\app\git\cmd\git.exe`
- `dev\ft-dev.ps1` finds dotnet and runs `build`, `test [-Filter text]`, `run`, `format`, `check`. Git is still
  called by its full path.
- Defender (an IT-managed rule) blocks newly built `.exe` files, so everything runs through `dotnet.exe`
  (`UseAppHost=false`). That is also why tests use xUnit v2 with VSTest: xUnit v3 always starts its own `.exe`.
- No Node, Docker or SQL Server. In Windows PowerShell 5.1, pass commit messages with `git commit -F <file>`.

## Architecture rules

1. `FitnessTracker.Domain` references nothing from ASP.NET Core or EF Core. All scheduling, recurrence, footprint,
   sleep and workout maths lives there. Controllers and views never contain scheduling rules.
2. **All time maths uses NodaTime.** Series are stored as clock time plus an IANA timezone; logged times as exact
   moments in UTC. Intervals are half-open `[start, end)`. Follow requirements §8 for midnight and clock changes.
3. **Every save path goes through the one validator** (SCH-1): forms, drag and drop, accepted suggestions, imports and
   later AI. Hard constraints are never silently broken (requirements §7.2).
4. **Never mark an activity completed because its time has passed** (FIT-1).
5. Every query is limited to the signed-in user; another user's data returns "not found". Every new data endpoint
   gets an access-control test (T19).
6. Store metric units (metres, seconds, kilograms); convert only for display.

## Testing

- Never say tests pass unless they were run in this session and passed. Report failures with their output
  (`verification-before-completion`).
- **Test first** (`test-driven-development`) for domain logic, services, controllers and bug fixes: write the test,
  watch it fail for the right reason, then write the code. Its "delete code written before the test" rule applies
  only to code written in the current change; never delete committed code without asking. Exempt (its "generated
  code / configuration" exception): `dotnet new` scaffolding, EF migrations, Razor views, CSS, vendored libraries.
  Read its `npm test` examples as `dev\ft-dev.ps1 test`, or `dev\ft-dev.ps1 test -Filter <name>` for a single test
  (a filter that matches nothing fails rather than passing).
- Run `dev\ft-dev.ps1 check` before every commit.
- Use xUnit's `Assert` (not FluentAssertions). Use in-memory SQLite for database tests (not EF's in-memory provider).
  No Testcontainers.
- Name or comment tests with the scenario ID they cover (e.g. `// T4`).
- Test data uses made-up people and `example.com` addresses only.

## Git

- One branch per phase (`phase-N-short-name`); **one commit per change**; stage only the files you changed.
- Commit message: a plain-English subject and body, an honest "What was checked:" line, and a numbered
  "To see the change:" list.
- No `Co-Authored-By` or any other AI attribution. **Never push, merge or delete branches**: the owner does that from
  GitHub Desktop.

## Privacy and safety

- No real personal data, secrets or connection strings in committed files. Development secrets go in .NET
  user-secrets.
- No new external service, connector, MCP server, plugin, analytics or AI provider without the owner's explicit yes
  for that specific one.
- Ask before downloading anything outside normal NuGet restores (e.g. FullCalendar files), naming the file, source
  and size.

## Skills

`.claude/skills/` holds `dotnet-patterns`, `csharp-testing`, `accessibility`, `break-ui`, `test-driven-development`
and `verification-before-completion` (all third-party, MIT, copied unchanged). **When a skill disagrees with this
file, this file wins.** The overrides are listed in `docs/skills-and-agents.md`.
