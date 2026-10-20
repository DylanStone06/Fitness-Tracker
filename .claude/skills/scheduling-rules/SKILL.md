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
  evening before `from` is not included. To work out a day's load or clashes, ask for the day before **and the
  day after** too: footprints add preparation and travel before an activity, which can start before midnight.
- **One-off changes** (cancelled, or moved/resized) are keyed by the **date the rule put the occurrence on**, never by
  its time, so editing the series' time keeps track of them. A moved occurrence shows on the day it now starts.
- **Editing the series** keeps changes for dates the edited series still falls on, removes the rest, and reports
  both counts (`SeriesEdit`), so the app never loses a change silently (P5). Restoring, or moving an occurrence back
  to its usual **moment** and length, removes its change (compared as moments: a 01:30 series happens at 02:30 on
  the night the clocks go forward).
- A move is stored as a **clock time**, so a drop in the second 01:00-01:59 on the night the clocks go back resolves
  to the first one (§8). The Phase 5 calendar must handle that (warn, or store the offset).
- `Series.InZone` keeps clock times, one-off changes included ("gym at 7am wherever I am").
- **Stored as RRULE text** (ADR 0001) with `RecurrenceRuleText`. `Format` writes one canonical form
  (`FREQ;INTERVAL;BYDAY;WKST;COUNT|UNTIL`, days in week order) so the same rule is always the same text; `Parse`
  accepts any order, case, spaces and an `RRULE:` prefix, and refuses anything outside the subset with a
  `FormatException` naming the part (WKST is checked on daily rules too, though they don't use it). UNTIL is a
  plain date on the series' own clock (the app's inclusive last date, not UTC), and `EndingOn` refuses a date in
  any calendar but ISO. This text keeps the app's meanings above, so it is not ready to hand to another calendar:
  export would need converting.
- **A rule can end before its first possible date** (Monday-only from a Tuesday, ending that Sunday; or a year
  typo in UNTIL), giving a series with no occurrences. The text and the rule can't tell, because they don't know the
  first date. The activity form refuses it (`RecurrenceRule.FallsOnAnyDate`), so the user is told rather than
  seeing nothing.
- **Stored activities** (`Web/Data/Activity`): start date, start clock time, IANA timezone and elapsed length, plus
  the rule as RRULE text (null for a one-off). A new activity takes the Preferences timezone; changing that
  preference moves activities in the old timezone to the new one, keeping clock times (T6, `PreferencesController`).
  A new weekly rule takes the week-start preference; editing a weekly activity keeps its rule's own week start,
  while turning a daily one weekly takes the preference. The form only accepts dates in the years 1900-2999, and
  `RecurrenceRule.Dates` stops on its last date, so nothing steps past 31 December 9999.

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

## Opening hours (`Domain/Locations`, TRV-1, H4)

- Per weekday; a weekday not listed is **closed**; `OpeningHours.Always` (with `IsAlwaysOpen`) is a location with
  no hours. Store "no hours entered" as `Always`, never as an empty dictionary, which means closed every day.
- Closing at or before opening means **after midnight** (18:00-02:00 runs into the next day); the same time means
  until that time the next day (midnight-midnight is the whole day: 23 or 25 h on clock-change days).
- Only the footprint's **activity part** must fit (`ProblemWith(footprint.Activity, zone)`, T17), half-open.
  Openings that touch or overlap are merged first.
- **Clock-change nights are resolved conservatively, unlike series:** a skipped time is the moment the clocks jump
  past it (closing 01:30 on 28 Mar 2027 = 02:00 BST); a repeated time is the second one for opening and the first
  for closing. So an opening is never longer than the clock shows, and H4 is never silently broken.
- A problem names the day of the opening the activity **started in**, or, if it started while closed, the day it
  starts. Explanations must show "(next day)" when Closes <= Opens, or "Friday 18:00-02:00" reads as covering
  Friday 01:00.
- Pass the **location's** timezone. Locations have none yet, so callers use the owner's timezone preference.

## Conflicts and daily load (`Domain/Scheduling`, H1, TRV-3, §7.3)

- A `Booking` is a label and a footprint. `Conflicts.Between(candidate, others)` and `Conflicts.Among(bookings)`
  compare **whole footprints** as moments, half-open (back-to-back is fine, T11). Each `Clash` names the parts
  where the overlap **begins** (T10: changing after Gym vs Dinner), the overlap, and `ActivitiesOverlap` (whether
  the activities themselves overlap, so an explanation doesn't blame travel or buffers wrongly).
- Results don't depend on input order (SCH-7): ties go by start, then end, then label. `Among` names each pair
  once, earlier footprint as the candidate.
- `Between` doesn't skip the candidate itself: the caller must leave out the booking's own stored version, or an
  edit clashes with itself.
- The **commute** is the travel part of the work activity's footprint (work at the office, journeys from home), so
  it is its own pair of blocks and a gym session from home has its own travel (T9). A remote day is work at base:
  no commute.
- Known limitation: every activity away from base gets a **round trip from base**, so two activities back-to-back
  at the same place (office gym after work, a work day split at lunch) each get a commute and clash. That is
  location chaining (SCH-8, after the MVP) unless the owner brings "staying at the same place" forward. Pinned by
  `DailyLoadTests.AnOfficeGymStraightAfterWork_*`.
- `DailyLoad.Of(footprints, zone)`: footprint time per local day, split at real midnight (23/25-hour days);
  overlapping footprints each count (§7.3 "total footprint minutes"; kept by the owner, requirements Q11). Days with
  no load are left out.

## The check on save (`Domain/Scheduling/ActivityCheck`, SCH-1, first part)

- `ActivityCheck.Check(candidate, others, context)` is the one check for a new or changed activity. The Web calls it
  through `ActivityChecker`, which builds `PlannedActivity`s from stored activities (`Activity.ToPlannedActivity`: a
  one-off is a series with `Daily().EndingAfter(1)`, so both resolve clock times the same way) and a
  `PlanningContext` (today in the user's timezone, home, travel times, each location's name and opening hours).
- **Problems** stop the save, each with a `CheckAspect` for the form field: a repeat that never happens (Repeat), no
  home or a missing travel leg for an activity away from home (Location; never guessed as zero), H8 (Length), and
  H4 on **every** occurrence checked, naming the first and counting the rest (Time).
- **Clashes** (H1) are only looked for when there are no problems (they depend on travel and length). The user can
  save them with "Save anyway" (Q2), asked on each save; acceptance isn't stored yet.
- **Windows**, from the later of today and the first date (`FirstCheckedDate`), or from the first date when nothing
  of the activity falls from today on (a past one-off, an ended series), so nothing is saved unchecked. The Web
  loads only the other activities in `OthersThatCanClash`. Clashes: `WeeksAhead` (8) weeks, others' occurrences 3
  days either side (two 24-hour footprints reach 48 h apart, a third date across a 23-hour day). Rules like every 98
  and every 99 weeks can't be proved clash-free in any practical window, so it is a window, said in the message.
  H4 and H8: `WeeksAheadForHoursAndLength` (53) weeks, or 7 x N days for a daily rule every N days if longer, so
  every weekday and both clock changes are reached; H8 on every occurrence (a moved one can be longer), and a
  length of zero or less is reported before any occurrence is worked out.
- **Other activities never stop the check:** the candidate's own stored version is left out (an edit doesn't clash
  with itself); one whose travel can't be worked out is checked without travel; an occurrence whose footprint breaks
  H8 is skipped.
- `ActivityCheck.Describe(clash, zone)`: "On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 19:30 to
  20:30)." Spans past midnight say "the next day".

## The agenda (`Domain/Scheduling/Agenda`, GOL-2, first part)

- `Agenda.Between(activities, context, from, through)`: every occurrence starting on those dates (in its own zone),
  in time order, then title, then id (SCH-7). Each has its footprint (null over 24 hours, H8), a plain-English
  `Note` when travel or the footprint can't be worked out (P1), and `ClashesWith` titles, found on footprints over
  `MarginDays` (3) days either side, so a clash with last night's shift shows. The check on save uses the same
  margin, travel rule (`Travel`) and time wording (`Times`), and also reports an activity overlapping its own next
  occurrence, so the two never disagree.
- `Agenda.Trip`: "Leave home 17:35, back 19:45": leaving is when the journey out starts, after getting ready at home
  (§7.1). Times on another day say "the day before" or "the next day"; a time the clocks repeat says which one
  ("01:15 GMT"). `Agenda.Starts` does the same for the start.

## Not built yet: read the requirements, don't guess

| Area | Requirements | Phase |
|---|---|---|
| Drawing footprints on the calendar | CAL-5 | 5 |
| The rest of the one validator: sleep minimum (H3), rest gaps and demanding sessions (H5-H7), and checking moves, suggestions and imports | SCH-1, §7.2 | 5-8 (H1, H4, H8 built in 4) |
| Scoring, "Plan my week", explanations | §7.3, §7.4, SCH-2 to SCH-7 | 6 |
| Sleep opportunity and sleep log | SLP-1 to SLP-5 | 8 |

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
| T6 | London to New York: series keep clock times; logged sleep unchanged | **Series half covered** (domain and stored activities); the sleep half needs the sleep log (Phase 8) | `SeriesTests.InAnotherZone_KeepsTheClockTime`, `OccurrenceChangeTests.InAnotherZone_*`, web `ActivitiesTests.ChangingTheTimezone_*` |
| T7 | Weekly, fortnightly, end date, count | **Covered** | `SeriesTests` |
| T8 | Cancel one, move one, edit series: both kept, reports 2 | **Covered** | `OccurrenceChangeTests.EditingTheSeries_KeepsBothOneOffChanges_AndReportsTwo` |
| T9 | Commute and gym travel counted once each | **Covered** (domain) | `DailyLoadTests.AnOfficeDayAndAnEveningGymSession_*` |
| T10 | Buffer overlapping another activity is a conflict | **Covered** (domain and on save) | `ConflictsTests.ChangingTimeRunningInto*`, `PreparationTimeOverlapping*`, `ActivityCheckTests.AClash_IsFoundOnFootprints_*`, web `ActivityChecksTests.Add_WhoseTravelRunsIntoAnotherActivity_Clashes` |
| T11 | Back-to-back: no conflict; 1 min overlap: conflict | **Covered** (domain and on save) | `ConflictsTests.BackToBackActivities_*`, `OverlappingByOneMinute_*`, web `ActivityChecksTests.Add_BackToBackWithAnotherActivity_IsSaved` |
| T12 | Late event below sleep minimum | Not built (Phases 6, 8) | |
| T13 | Demanding sessions too close | Not built (Phase 6) | |
| T14 | Busy day: largest gap and footprint explained | Not built (Phase 6) | |
| T15 | "Brighton weekend" template | Not built (Phase 6) | |
| T16 | Reschedule a missed workout | Not built (Phase 6) | |
| T17 | Gym opens 06:00, travel 05:35: valid | **Covered** (domain and on save) | `OpeningHoursTests.AWorkoutStartingAtOpeningTime_*`, `ActivityCheckTests.AWorkoutStartingAtOpeningTime_*`, web `ActivityChecksTests.Add_AtAGymWithOpeningHours_MustBeInsideThem` |
| T18 | A/B template: three weeks later is week B | **Covered** | `WeekCycleTests.ATwoWeekCycle_IsInWeekB_ThreeWeeksAfterWeekA` |
| T19 | Another user's data is "not found" | **Covered for every page so far** (locations, travel times, activities, preferences) | `FitnessTracker.Web.Tests` `Security/AccessControlTests`, `PreferencesTests` |
| T20 | Past planned session shows "Not logged" | Not built (Phase 7) | |
| T21 | Calendar at 320 px | Not built (Phase 5) | |
| T22 | Same week planned twice: identical | Not built (Phase 6) | |
| T23, T24 | Meal prep; AI output | After the MVP | |
