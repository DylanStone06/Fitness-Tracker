# Plan: Phase 7, step 4, other activities (FIT-4)

Written 4 November 2026, before any code. FIT-4: "Other activities (walk, cycle, swim, mobility, custom): duration and
optional intensity." The owner chose Q34-Q36 the same day, each the recommended option.

## What it adds, as the user sees it

An "Other exercise" session's page, once it has started, logs what it was, as a run's page does: the **type** (walk,
cycle, swim, mobility, or a name of your own), the **time it took** beside the time planned, an optional **distance**
(km or miles) and **how hard it felt** (1-10), each optional. Saved, a card headed by the type shows the figures as
tiles (Time, Distance, Effort), the form folded under "Change the details". Notes work as on every session.

## Decisions (the owner's, 4 November 2026)

| # | Question | Decision |
|---|---|---|
| Q34 | Which types? | Walk, cycle, swim, mobility, and custom with a name of up to 40 characters, as a run's. |
| Q35 | How is intensity recorded? | On the same 1-10 "How hard it felt" scale as runs (Q29), so every session's effort can be compared. |
| Q36 | A distance? | Optional, for any of them (meant for walks, cycles and swims), in km or miles as for runs, stored in whole metres. Shown on its own: no speed or swim pace yet (FIT-7). |

## Data

`SessionLog` gains `OtherType` (walk, cycle, swim, mobility, custom) and `CustomOtherType` (the name, up to 40
characters), nullable. Time, distance and effort reuse the run's columns (`ActualSeconds`, `DistanceMetres`,
`Effort`), which the data step for runs planned for. Marking it skipped removes them (Q28); notes stay. One migration,
`AddOtherActivityLogs`.

## The page and the rules

- The time, distance and effort boxes, and their checks, are the run's: they move into one shared partial and one
  shared set of checks, so a run and another activity can't drift apart. Only the type list differs.
- The heading is the type ("Walk", "Mobility", a custom name, or "Your session" with none); tiles as for runs, without
  pace.
- Only an Other exercise session saved as done reads these boxes; a run's page and a gym page ignore them, as they
  ignore each other's (Q20).

## Tests (first)

Saved with its details in metres and seconds; shown with its tiles; optional (nothing typed just marks it done);
custom needs a name; the shared checks (time, distance, effort) still refuse what they refused for runs; skipping
removes the details and keeps notes; a run and a gym page don't show these boxes and ignore them posted; not before
it has started; another user's is "not found" (T19); an old tab can't overwrite (§9).

## Commits, in order

1. This plan and Q34-Q36 in the requirements.
2. The two columns and the migration.
3. The shared detail boxes and checks (runs unchanged: their tests must still pass), then the other activity's form
   and summary, test first.
4. The browser check, then the docs.
