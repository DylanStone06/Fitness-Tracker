# Plan: Phase 7, step 2, the run log (FIT-3)

Written 3 November 2026, before any code, at the owner's request that each step is planned in detail, built carefully
to keep errors down, and looks clean. The decisions below are defaults (Q26-Q29 in the requirements), easy to change.

**Status: built and checked, 3-4 November 2026**, in the commits listed below. The browser check found one fault, an
absurdly slow pace ("2399:58:20") breaking inside a number on a phone, fixed by letting a figure break only after a
colon. The planned distance limits became 0.01 to 1,000 in km or miles.

## What it adds, as the user sees it

A run's session page (Run category only; Gym and Other exercise get theirs in steps 3 and 4) lets you log what you did
once the run has started:

- the **type of run**: easy, long, tempo, intervals, recovery, or a name of your own;
- the **time it took**, in hours, minutes and seconds, next to the time planned for it;
- the **distance**, in km or miles as chosen in Preferences;
- **how hard it felt**, 1 to 10.

Every detail is optional: "Save as done" with nothing filled in just marks it done. Once saved, the page shows the run
as a row of figures: distance, time, **pace** (worked out, never stored: "5:30 per km") and effort. The form to change
them is folded away under "Change the details" (P7: simple first, detail on demand).

## Decisions (defaults, recorded as Q26-Q29)

| # | Question | Default |
|---|---|---|
| Q26 | What does a run's log hold? | The four details above, all optional. The planned time is the session's own length, shown beside the time taken, never typed. A custom type has a name of up to 40 characters. |
| Q27 | How are time and distance typed and shown? | Time in three boxes (hours, minutes, seconds; up to 24 hours), shown as a stopwatch shows it ("28:36", "1:05:20"). Distance as a number with a decimal point ("5.2"), stored to the nearest metre, shown with up to 2 decimals ("5.2 km", "3.11 miles", "1 mile"). Pace per km or per mile ("5:30 per km", "8:51 per mile"). |
| Q28 | What happens to a run's details when it's marked skipped, or back to planned? | They are removed: a skipped run has no distance. The page says so beside those buttons before they're pressed. |
| Q29 | What do the effort numbers mean? | 1 very easy, 2-3 easy, 4-6 moderate, 7-8 hard, 9 very hard, 10 all out (a common perceived-effort scale), shown as "6 of 10, moderate". |

## Data

`SessionLog` (one row per marked session) gains nullable columns, so a mark without details stays as it is:

| Column | Meaning | Why this shape |
|---|---|---|
| `ActualSeconds` | Time taken | §8 and CLAUDE.md rule 6: seconds. Shared with FIT-4 later. |
| `DistanceMetres` | Distance | Q25: whole metres (1 m is far finer than anyone types). |
| `RunType` | Easy, Long, Tempo, Intervals, Recovery, Custom | An enum, so later totals by type are simple. |
| `CustomRunType` | The name, only with Custom | Up to 40 characters. |
| `Effort` | 1-10 | Shared with FIT-4's "intensity" later. |

One migration, `AddRunLogs`. No actual start time is recorded: Q23 keeps the calendar's planned time, and FIT-3 asks
only for durations.

## Rules (in `FitnessTracker.Domain/Workouts`, test first)

- `DistanceUnit` moves from the web project into the domain (it was only a preference until now), as `ClockFormat`
  did. Stored as the same number, so no migration.
- `Distances`: metres from an amount in km or miles (rounded to the nearest metre), the amount back, the text shown
  ("5.2 km", "1 mile"), and **the text to put back in the form box**: the shortest that gives back exactly the same
  metres ("3.107" for 5000 m in miles). Without that, opening and re-saving an unchanged run would shift it by a metre
  each time (3.11 miles is 5005 m, not 5000).
- `Pace`: time per km or per mile, rounded to the whole second **before** it is written, so 5:59.7 shows as "6:00",
  never "5:60". None without both a time and a distance.
- `StopwatchText`: "0:45", "28:36", "1:05:20".

## The page

Same column as now. From the top:

1. The title (read right to left when it's Arabic or Hebrew), when it is, and its status (as now).
2. **When done, with details**: a card headed by the type ("Easy run", or the custom name) holding a row of figure
   tiles, two across on a phone and four on wider screens: Distance, Time, Pace, Effort. Only the figures given are
   shown. Tiles are the page's own background inside the white (dark: raised) card, so every colour pair is one
   already measured in `docs/design.md`; figures in tabular numbers.
3. **The form** (only once the run has started): a card, "Log this run" (or, when done, folded under "Change the
   details", opened again if something needs fixing). Type of run (a list; the name box appears only when "Custom" is
   chosen, through CSS `:has()`, as the opening-hours days do); time taken (three boxes, "Planned: 45 min" above
   them); distance (labelled with the unit, linking to Preferences to change it); how hard it felt (a list).
4. The buttons: "Save as done" (or "Save changes"), "Mark as skipped", "Back to planned", with the note from Q28 when
   there are details to lose.

Errors follow the site's pattern: a summary at the top linking to each box, each box's own message, `aria-invalid`.
Boxes only "Save as done" uses are ignored by the other buttons, even if what's in them can't be read (as the
occurrence page does with `MoveBoxes`).

## Two tabs (requirements §9), fixed first

Step 1 left a gap: a session page left open in one tab could mark a session skipped after another tab had marked it
done, silently. With details that would lose a run. So the first commit gives the page a hidden version: a save made
from an out-of-date page is refused with "This session was changed in another tab or window after you opened this
page", shown as it is now. The database checks the version too (it is a concurrency token), for two saves at the same
moment.

## Edge cases to test

- Distance: "abc", "0", "0.0001" (rounds to 0 m), "-1", "5,2" (a comma), above 1000, and blank; miles both ways.
- Time: 60 minutes, 25 hours, all zeros, one box only ("45" minutes), blank.
- Custom with no name; a name with another type chosen (ignored, since its box is hidden); a 41-character name.
- Effort 0 or 11 posted by hand.
- Skipping with unreadable details in the boxes: skipped, no errors, details removed.
- A run not started yet: no form, can't save details by posting them anyway.
- A gym session: no run form, and run details posted by hand are ignored.
- Another user's session: not found, nothing stored (T19).
- Re-saving an unchanged run in miles keeps the same metres.
- Worst cases on screen (break-ui): a 40-character unbroken custom name, 1000 km, 24:00:00, a very slow pace, at
  320 px and on a wide screen, in light and dark.

## Commits, in order

1. This plan (and the roadmap pointing at it).
2. The page refuses a save from an out-of-date tab (§9), test first.
3. `DistanceUnit` into the domain; `Distances`, test first.
4. `Pace` and `StopwatchText`, test first.
5. `SessionLog`'s new columns and the `AddRunLogs` migration.
6. The run form and summary on the session page, test first, with its styles.
7. The browser check (worst-case data, phone and desktop, both themes), fixes from it, then the docs (requirements
   Q26-Q29, roadmap, known limitations, design).

Each is checked with `dev\ft-dev.ps1 check` before it is committed.

## Not in this step

- Notes on a session: step 3, for every kind (FIT-2 asks for them).
- Distances on the home page and calendar, and weekly running distance goals (GOL-1, later).
- A live pace preview while typing (it needs script; the page works without it).
- Heart rate, routes, splits and charts (FIT-7, after the MVP).
