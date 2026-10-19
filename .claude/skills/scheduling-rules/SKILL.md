---
name: scheduling-rules
description: Use when writing, changing or reviewing anything in FitnessTracker.Domain's Time, Recurrence or Templates folders, or later its Scheduling and sleep code - the time, recurrence and cycle rules as actually built, the decisions behind them, how to test them, and the T1-T24 scenario catalogue with the test that covers each one
---

# Scheduling rules (as built)

Written for this project (Fitness Tracker, MIT). It describes **only behaviour that exists in code with a passing
test**. Anything marked "not built" is described in `docs/requirements.md` §7 and §8; read that and don't assume
how it will work. When code and this file disagree, the tests decide; then fix this file.

## Time (`Domain/Time`, requirements §8)

| Rule | Where | Tested by |
|---|---|---|
| A clock time becomes an exact moment with NodaTime's **lenient resolver**: a time the clocks skip moves forward by the gap (01:30 becomes 02:30); a time that happens twice means the **first** one. | `ClockTimes.ToInstant` | `ClockTimesTests` |
| Spans are NodaTime `Interval`s, half-open `[start, end)`. Their length is **elapsed time**: 23:00 to 07:00 is 9 h on 24-25 Oct 2026, 7 h on 27-28 Mar 2027. A span that would **end before it starts** once the clocks change (01:45 to 02:00 on 28 Mar 2027) is refused with an `ArgumentException` that says why. | `ClockTimes.ToInterval` | `SleepFrom2300To0700_LastsTheTimeThatReallyPassed`, `ASpanThatWouldEndBeforeItStarts_*` |
| An activity **belongs to the day it starts**; its minutes count towards each day for the part on that day. Days run midnight to midnight, so a clock-change day is 23 or 25 hours. Ending exactly at midnight puts nothing on the next day. | `CalendarDays.DayOf`, `CalendarDays.Split` | `CalendarDaysTests` |
| Weeks start on a chosen day (Monday by default). `IsoDayOfWeek.None` is refused everywhere. | `CalendarWeeks` | via `SeriesTests`, `WeekCycleTests` |

## Recurrence (`Domain/Recurrence`, CAL-3, CAL-4)

- A **series** is a first date + a clock time + a length (elapsed) + a timezone + a rule. Every occurrence starts at
  the series' clock time in its zone, so 07:00 stays 07:00 across clock changes.
- A **rule** is daily or weekly-on-days, every N days or weeks, ending never, on a date (inclusive) or after a count.
  Rules that can't work (every 0, no days, count 0) are refused when made. A rule can't change once made.
- Decisions that differ from, or pick a side in, iCalendar:
  - The **first date only counts if the rule falls on it** (iCalendar always counts DTSTART).
  - A **count is counted from the first date**, not from the dates asked for.
  - The rule **keeps its own week start** (WKST), so a later week-start preference change doesn't move fortnightly
    series to other weeks.
  - A **cancelled occurrence still counts** towards "ends after N".
- `OccurrencesBetween(from, to)` returns occurrences that **start** on those dates, in time order. One starting the
  evening before `from` is not included: to work out a day's load, ask for the day before too.
- **One-off changes** (cancelled, or moved/resized) are keyed by the **date the rule put the occurrence on**, never by
  its time, so editing the series' time keeps track of them. A moved occurrence shows on the day it now starts.
- **Editing the series** keeps changes for dates the edited series still falls on, removes the rest, and reports
  both counts (`SeriesEdit`), so the app never loses a change silently (P5). Restoring, or moving an occurrence back
  to its usual **moment** and length, removes its change (compared as moments: a 01:30 series happens at 02:30 on
  the night the clocks go forward).
- A move is stored as a **clock time**, so a drop in the second 01:00-01:59 on the night the clocks go back resolves
  to the first one (§8). The Phase 5 calendar must handle that (warn, or store the offset).
- `Series.InZone` keeps clock times, one-off changes included ("gym at 7am wherever I am").

## Template cycles (`Domain/Templates`, CAL-9)

`WeekCycle`: 1-4 weeks; week A is the week of the anchor date (requirements Q3: the week the template is first
applied); the cycle runs forwards and backwards (`%` is wrapped, because C# keeps its sign).

## Footprints (`Domain/Footprints`, §7.1, H8, TRV-2)

- `Footprint.Around(activity, buffers, journeys)`: preparation, travel out, activity, changing, travel back, in that
  order, built from the activity's **exact moments** with elapsed lengths, so a footprint is the same length on a
  clock-change night. Parts of zero length are left out of `Segments`; `Activity` is the activity alone (what opening
  hours apply to); `Whole` is what conflicts are checked on.
- H8 (`Footprint.ProblemsWith`): the activity is longer than zero; each buffer and journey is zero or more (each
  negative one is its own problem, so a form can say which field); the whole is at most **24 hours of elapsed
  time**. A negative part counts as zero towards the 24 hours. So midnight to midnight on the day the clocks go back
  (25 h) is refused (pinned by a test; listed in known limitations for the owner).
- Travel (`TravelTimes.JourneysFor`): **none** when the activity is at base or has no location; otherwise the leg
  base to activity and the leg back, which can differ. A missing leg gives no journeys and names the missing legs:
  it is **never zero minutes** (P1). The caller decides what base is (home now; a template's base in Phase 6, TRV-4).
- To put a footprint on a day, use `CalendarDays.DayOf(footprint.Activity, zone)`, not `Whole.Start`: an activity at
  00:10 with 25 min travel belongs to the day it starts, not the day before.

## Not built yet: read the requirements, don't guess

| Area | Requirements | Phase |
|---|---|---|
| Commutes as their own blocks; drawing footprints on the calendar | TRV-3, CAL-5 | 4, 5 |
| The one validator and hard constraints H1-H8 (overlap, sleep minimum, opening hours, rest gaps...) | SCH-1, §7.2 | 6 (some checks in 4) |
| Scoring, "Plan my week", explanations | §7.3, §7.4, SCH-2 to SCH-7 | 6 |
| Sleep opportunity and sleep log | SLP-1 to SLP-5 | 8 |
| Storing rules as RRULE text | ADR 0001 | with storage (4) |

## Writing tests for this code

- Use real zones from `DateTimeZoneProviders.Tzdb` (Europe/London; America/New_York for a second zone).
- Write expected moments **in UTC by hand** (`Instant.FromUtc`), never by converting with the code under test.
  BST = UTC+1, GMT = UTC+0, New York in October = UTC-4.
- Useful dates: October 2026 starts on a **Thursday**; Monday 5 October 2026 starts a week; clocks go **back** at
  02:00 BST on **Sunday 25 October 2026** and **forward** at 01:00 GMT on **Sunday 28 March 2027**.
- Check every expected date against a calendar by hand before the first run.
- Name or comment each test with its scenario ID (`// T7`).

## Review checklist

1. Clock times (`LocalDateTime`/`LocalTime`) and exact moments (`Instant`) are never mixed; nothing uses `DateTime`.
   "Is it the same time?" compares moments, because two clock times can be the same moment on a clock-change night.
2. Intervals are half-open: touching is not overlapping; ending at midnight isn't on the next day.
3. Durations are elapsed, measured between exact moments; days are measured midnight to midnight, never as 24 h.
4. One-off changes are keyed by the rule's date; series edits report kept and removed changes.
5. Week maths goes through `CalendarWeeks`; negative week numbers are wrapped, not left negative.
6. Every new rule has a test with hand-derived expectations, including a clock-change night.

## Scenario catalogue (requirements §10)

| # | Scenario | Status | Test |
|---|---|---|---|
| T1 | Gym footprint 120 / 130 min | **Covered** | `FootprintTests.AGymSession_WithTravelBothWays_*`, `AGymSession_WithTenMinutesChanging_*` |
| T2 | Targets can't fit; shortfall explained | Not built (Phase 6) | |
| T3 | 22:30-00:30: start day; 90/30 split | **Covered** | `CalendarDaysTests.AnActivityCrossingMidnight_*` |
| T4 | Clocks back: sleep 9 h; 01:30 series once, first 01:30 | **Covered** | `ClockTimesTests.SleepFrom2300To0700_*`, `SeriesTests.ADailySeriesAt0130_OccursOnce_*` |
| T5 | Clocks forward: 01:30 series at 02:30; sleep 7 h | **Covered** | `ClockTimesTests.SleepFrom2300To0700_*`, `SeriesTests.ADailySeriesAt0130_OccursAt0230_*` |
| T6 | London to New York: series keep clock times; logged sleep unchanged | **Series half covered**; the sleep half needs the sleep log (Phase 8) | `SeriesTests.InAnotherZone_KeepsTheClockTime`, `OccurrenceChangeTests.InAnotherZone_*` |
| T7 | Weekly, fortnightly, end date, count | **Covered** | `SeriesTests` |
| T8 | Cancel one, move one, edit series: both kept, reports 2 | **Covered** | `OccurrenceChangeTests.EditingTheSeries_KeepsBothOneOffChanges_AndReportsTwo` |
| T9 | Commute and gym travel counted once each | Not built (Phase 4) | |
| T10 | Buffer overlapping another activity is a conflict | Not built (Phase 4) | |
| T11 | Back-to-back: no conflict; 1 min overlap: conflict | Not built (Phase 4) | |
| T12 | Late event below sleep minimum | Not built (Phases 6, 8) | |
| T13 | Demanding sessions too close | Not built (Phase 6) | |
| T14 | Busy day: largest gap and footprint explained | Not built (Phase 6) | |
| T15 | "Brighton weekend" template | Not built (Phase 6) | |
| T16 | Reschedule a missed workout | Not built (Phase 6) | |
| T17 | Gym opens 06:00, travel 05:35: valid | Not built (Phase 4) | |
| T18 | A/B template: three weeks later is week B | **Covered** | `WeekCycleTests.ATwoWeekCycle_IsInWeekB_ThreeWeeksAfterWeekA` |
| T19 | Another user's data is "not found" | **First version** (locations) | `FitnessTracker.Web.Tests` `Security/AccessControlTests` |
| T20 | Past planned session shows "Not logged" | Not built (Phase 7) | |
| T21 | Calendar at 320 px | Not built (Phase 5) | |
| T22 | Same week planned twice: identical | Not built (Phase 6) | |
| T23, T24 | Meal prep; AI output | After the MVP | |
