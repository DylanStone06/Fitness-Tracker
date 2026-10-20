# ADR 0001: Technology stack

| | |
|---|---|
| Status | **Accepted**, 13 October 2026 |
| Decided by | Dylan Stone, on the Phase 1 recommendation |

## Context

- The repository was empty: a one-line README, an MIT licence and GitHub's Node `.gitignore` template, chosen when
  the repo was created rather than as a deliberate stack decision.
- The owner works daily in ASP.NET Core MVC on .NET 10. They said the app "doesn't have to be MVC": choose what fits
  best.
- This PC has the .NET SDK 10.0.401 (installed for the user only, not on PATH). It has no Node, Docker or SQL Server
  LocalDB. Git is GitHub Desktop's bundled copy.
- What the app needs most: correct time maths (midnight, clock changes, timezones), a well-tested rules engine, an
  interactive week calendar that works on a phone, and private-by-default data.

## Decision

| Concern | Choice |
|---|---|
| Runtime | **.NET 10** (long-term support release) |
| Web | **ASP.NET Core MVC** with Razor views, plus small JSON endpoints for the calendar |
| Calendar UI | **FullCalendar v6**, standard (MIT-licensed) features only, self-hosted at a pinned version |
| Browser scripts | Plain JavaScript modules; **no npm or bundler** |
| Data | **EF Core + SQLite** |
| Accounts | **ASP.NET Core Identity** with cookie sign-in |
| Dates and times | **NodaTime** |
| Recurrence | RRULE text (the iCalendar standard), with our own expander for the subset we support |
| IDs | GUIDs (version 7, which sort by creation time) |
| Tests | **xUnit** with its built-in asserts; `WebApplicationFactory` with in-memory SQLite for integration tests |
| Code quality | `.editorconfig` + `dotnet format`; nullable reference types on; warnings treated as errors |

### Project layout

```
src/FitnessTracker.Domain/         plain C# + NodaTime: time, recurrence, footprints, scheduling engine, workout maths
src/FitnessTracker.Web/            MVC controllers, views, services, Data/ (DbContext, migrations, Identity)
tests/FitnessTracker.Domain.Tests/
tests/FitnessTracker.Web.Tests/
dev/ft-dev.ps1                     build, test, run, format, check (finds the per-user dotnet and git)
```

The one boundary that matters: **`FitnessTracker.Domain` references nothing from ASP.NET Core or EF Core.** The
scheduling rules can then be tested on their own, quickly, and reused by every way of saving (forms, drag and drop,
suggestions, later AI).

## Alternatives considered

| Option | Why not |
|---|---|
| **Razor Pages** | Just as capable. MVC was chosen because the owner uses it daily and it suits the calendar's JSON endpoints. |
| **Blazor Server** | C# everywhere is attractive, but it keeps a live connection open per user, and FullCalendar would need a JavaScript-interop wrapper. More moving parts for the same result. |
| **Blazor WebAssembly** | A large first download, plus a separate API to build and secure. |
| **Node / TypeScript** (e.g. Next.js) | Node isn't installed, none of the reviewed skills apply, and the owner's experience is .NET. |
| **React SPA + API** | Two applications to build, deploy and secure. Overkill for a single-user-first MVP. |
| **SQL Server LocalDB / PostgreSQL** | Neither is installed, and PostgreSQL would need Docker or an install. SQLite is enough until hosting is decided. |
| **`DateTime` / `TimeZoneInfo` only** | Easy to mix up clock times and exact moments. Results depend on the operating system's timezone data, so DST tests can differ between machines. |
| **Ical.Net for recurrence** | Full RFC 5545 support we don't need yet. The MVP needs daily, weekly and every-N-weeks rules, which are small enough to own and test fully. Storing standard RRULE text keeps the door open to adopting it later. (Phase 4 note: the stored text keeps two app meanings that differ from RFC 5545, the first date and UNTIL, so adopting it would mean converting stored rules, not only swapping the code. See `RecurrenceRuleText`.) |

## Consequences

**Good**
- Builds on this PC today with nothing new to install.
- The copied `dotnet-patterns`, `csharp-testing` and `accessibility` skills apply directly.
- NodaTime ships its own timezone database, so DST tests give the same answer on every machine.
- One deployable app; the domain rules are isolated and fast to test.

**Costs and limits**
- SQLite allows one writer at a time. That's fine for one user. Moving to PostgreSQL or SQL Server later means a new
  migration baseline, because EF Core migrations are specific to the database.
- EF Core's SQLite provider can't compare or sort `DateTimeOffset` values in SQL, so exact moments are stored as UTC
  ticks through NodaTime value converters.
- Without npm, front-end libraries are downloaded by hand (with approval), pinned, and kept with their licences.
- Our own recurrence expander must be thoroughly tested (requirements T4–T8).

## FullCalendar, as added (Phase 5, 20 October 2026)

The owner approved the download on 20 October 2026.

| | |
|---|---|
| Version | Standard Bundle **6.1.21**, published 18 June 2026. 6.1.22 (16 October 2026) was skipped as only four days old; move up once it has been out a few weeks. |
| Licence | MIT (`wwwroot/lib/fullcalendar/LICENSE.md`); standard features only, no premium plugins |
| Source | npm package `fullcalendar`, through jsDelivr: `https://cdn.jsdelivr.net/npm/fullcalendar@6.1.21/` |
| Files | `index.global.min.js` (283,987 bytes, SHA-256 `TnL/Fpr4V7u4/uYmNklJ1XBCSwDfNsAb0IepWANxUV4=`) and `LICENSE.md` (1,066 bytes, `TAsNribJnjsRvnC5kKcDvk3rdeIYhBBFguDiF3htMgk=`), both checked against jsDelivr's published checksums |
| Kept as shipped | `.gitattributes` stores `wwwroot/lib/**` byte for byte, so the checksums still match |

FullCalendar 6 adds its styles to the page with `<style>` elements, which the Content-Security-Policy (no
`unsafe-inline`) blocks. The calendar page needs a per-request nonce for them, which FullCalendar reads from a
`<meta name="csp-nonce">` element; that is built with the page.

## To verify before adding (Phase 2, and Phase 5 for FullCalendar)

- The current stable version and licence of each package: NodaTime, EF Core SQLite 10.x, ASP.NET Core Identity EF
  stores, xUnit (v2 or v3, whichever `dotnet new` and the test runner support cleanly), and FullCalendar 6.x.
- Which assertion style to use. FluentAssertions 8+ is no longer free for commercial use, so the default is xUnit's own
  `Assert`. Re-check before adding any assertion library.
- What `dotnet new` generates with SDK 10.0.401.
