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
  time, or the whole day on a longer day** (`LongestOn(day, zone)`, requirements Q7): midnight to midnight on the day
  the clocks go back (25 h) is allowed, but 25 h on any other day isn't, and the 23-hour day still allows 24. Callers
  pass the limit for the day each occurrence starts (`Times.LongestFor`); without one it is 24 h. A negative part
  counts as zero towards the limit.
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
  H4 on **every** occurrence checked, naming the first and counting the rest (Time). For a demanding activity, H5-H7
  from `PlanningContext.Rest` (ACC-7; `RestRules.None` checks none) on every occurrence over the same year as H4 and
  H8 (they depend on the weekday and the clocks changing too: a rule every 10 days first reaches Friday in January;
  Saturday to Sunday 18:00 is 23 h across 27-28 March 2027), against the demanding sessions near it, other
  activities' and its own (kept apart by whose they are, not by time: another activity's at the same moment isn't
  its own, review H-1), start to start in elapsed time, as the planner does:
  "It's 22 h from Hard ride, a demanding session on Mon 2 Nov 2026 at 20:00; your preferences keep demanding sessions
  at least 24 hours apart.", "On Mon 2 Nov 2026 there's already a demanding session, Hard ride; your preferences allow
  1 a day.", "Wed 4 Nov 2026 is one of your recovery days, and this is a demanding session." (Time). They are
  validation errors, not clashes to accept (§7.2). `OthersThatCanClash` and `OthersThatCanClashWith` take the rest
  between demanding sessions, to reach as far as it does (`RestReachDays`); `OthersThatCanClash(..., demanding: true)`
  also reaches to the end of that year, for one-offs months ahead (a running series is loaded anyway). Tests:
  `ActivityCheckTests.ADemanding*`, `TheRestRule_*`, `ARecoveryDay_IsFound_ForARuleEveryTenDays`,
  `CheckOccurrence_KeepsTheRestRule`, web `ActivityChecksTests.Add_ADemandingSessionTooSoonAfterAnother_*`,
  `Add_ADemandingSeries_KeepsTheRestRule_MonthsAhead_AcrossTheClockChange`.
- **Clashes** (H1) are only looked for when there are no problems (they depend on travel and length). The user can
  save them with "Save anyway" (Q2), asked on each save; acceptance isn't stored yet.
- **Windows**, from the later of today and the first date (`FirstCheckedDate`), or from the first date when nothing
  of the activity falls from today on (a past one-off, an ended series), so nothing is saved unchecked. "From today
  on" means at all (`HappensFrom`): in the year looked at, by the rule after it (every 60 weeks can skip a whole
  year), or moved there; not only within the year (scheduling review M-1, Phase 5). The Web
  loads only the other activities in `OthersThatCanClash`. Clashes: `WeeksAhead` (8) weeks, others' occurrences 3
  days either side (two footprints of up to a day each reach 49 h apart, a third date across a 23-hour day). Rules like every 98
  and every 99 weeks can't be proved clash-free in any practical window, so it is a window, said in the message.
  H4 and H8: `WeeksAheadForHoursAndLength` (53) weeks, or 7 x N days for a daily rule every N days if longer, so
  every weekday and both clock changes are reached; H8 on every occurrence (a moved one can be longer), and a
  length of zero or less is reported before any occurrence is worked out.
- **A moved occurrence (CAL-4) is checked wherever it lands from today on** (`MovedOutside`): before the windows
  (moved earlier than the first date, but not before today) or after them (19 weeks, two years on), for H1, H4 and
  H8, and `OthersThatCanClash` covers 3 days either side of it, so the Web loads what it can clash with. One moved
  into the past is history, like the rest of the series' past (Phase 5 code review), unless it is still running at
  the start of today (a shift moved to last night that runs into this morning's: the home page shows that clash, so
  the check does too; scheduling review L-1). A series with nothing from today on has all its moves checked. Moving
  an occurrence on its own page (`CheckOccurrence`, below) checks it wherever it goes, past included. Tests:
  `ActivityCheckTests.AnOccurrenceMoved*`, `AMovedOccurrenceStillRunningToday_*`, web
  `OccurrencePageTests.AMoveBeforeTheSeriesStarts_IsStillCheckedForClashes`.
- **A change to one occurrence is checked on that occurrence** (`ActivityCheck.CheckOccurrence`, by its rule date):
  where it now is, its travel (unknown travel is a problem, never a reason to skip its clashes), H8 against its own
  day, H4, and H1 against the other activities near it (`OthersThatCanClashWith`) and the rest of its own series. A
  cancelled one has nothing to check. The rest of the series isn't looked at, so clashes kept elsewhere don't hold a
  change up. (Comparing the whole series' check before and after, by description text, was tried first and had
  holes: scheduling review H-1, H-2, L-5.) Tests: `ActivityCheckTests.CheckOccurrence_*`, web
  `OccurrencePageTests`.
- The Preferences page does compare findings before and after a timezone move, by text; the descriptions include the
  clock format (ACC-3), so both runs use the same one (`ActivityChecker.CheckAsync(activity, clock)`).
- **Other activities never stop the check:** the candidate's own stored version is left out (an edit doesn't clash
  with itself); one whose travel can't be worked out is checked without travel; an occurrence whose footprint breaks
  H8 is skipped.
- `ActivityCheck.Describe(clash, zone, clock)`: "On Mon 2 Nov 2026, its journey back overlaps Dinner (Dinner is 19:30 to
  20:30)", or "7:30pm to 8:30pm" with the 12-hour clock (`ClockText`, `PlanningContext.Clock`). Spans past midnight
  say "the next day".

## The agenda (`Domain/Scheduling/Agenda`, GOL-2, first part)

- `Agenda.Between(activities, context, from, through)`: every occurrence starting on those dates (in its own zone),
  in time order, then title, then id (SCH-7). Each has its footprint (null when longer than the day allows, H8), a plain-English
  `Note` when travel or the footprint can't be worked out (P1), and `ClashesWith` titles, found on footprints over
  `MarginDays` (3) days either side, so a clash with last night's shift shows. The check on save uses the same
  margin, travel rule (`Travel`) and time wording (`Times`), and also reports an activity overlapping its own next
  occurrence, so the two never disagree.
- `Agenda.Trip`: "Leave home 17:35, back 19:45": leaving is when the journey out starts, after getting ready at home
  (§7.1). Times on another day say "the day before" or "the next day", or the date when further away; a time the
  clocks repeat says which one ("01:15 GMT"). `Agenda.Starts` does the same for the start.
- `Agenda.Next(items, now)`: the home page's "next activity". Of the items not over, the one whose footprint starts
  first (you set off for the 18:00 gym at 17:35 before a 17:40 call), ties in the agenda's order whatever order the
  items come in (SCH-7). Over = back home, the footprint's end (the occurrence's own end with no footprint); under
  way from the footprint's start, getting ready included. Half-open, and moments only: on 25 October 2026 a first-
  01:30 session is under way at the second 01:15. Only what `Between` listed counts, so yesterday's night shift
  isn't "next". Tests: `AgendaTests` (`Next_*`, `AnActivity_IsUnderWay_FromLeavingUntilBack`,
  `OnTheNightTheClocksGo*`).

## The planner (`Domain/Scheduling/Planner`, SCH-2, SCH-3, SCH-5, SCH-7, §7.3, §7.4)

- `Planner.Plan(PlanRequest)`: the sessions weekly goals (GOL-1, `PlanGoal`) still need in a week's dates, from
  `Now` on. A session counts towards a goal when its activity has that `PlannedActivity.GoalId`; `AlreadyPlanned`
  says how many did. Goals go highest `Priority` first, then by title and id (SCH-7), each goal's sessions in turn.
- **Busy time** (§7.4): every occurrence's footprint over `Planner.DatesToLoadFor` (`Agenda.MarginDays` either side,
  or further when the rest between demanding sessions reaches further, so H5 sees a session 4 days before the week:
  scheduling review H-1; the Web loads the same dates), and each night's `SleepWindow`, from winding down before bed
  to getting going after waking. A bedtime before midday is after midnight. Sleep is never cut into (T12's planner
  half). Travel that can't be worked out is left out, as on the home page, and said in `WeekPlan.Notes` (P1).
- **Start times**: every quarter hour (Q1) that happens exactly once; on clock-change nights the skipped and repeated
  quarter hours aren't tried rather than guessed.
- **Hard rules, in this order** (a time that breaks one is never suggested): not over (the footprint starts at or
  after `Now`); one session of a goal a day (two for 8-14 a week); not on a day the user said no to for the goal
  (`PlanRequest.Rejected`, SCH-4; saying no to one time gave the next quarter hour); H7, no demanding session on a
  recovery day; H6, no more than `MostDemandingPerDay` demanding sessions starting that day, those already planned
  included; H1, the footprint overlaps no busy time (half-open); H4, the activity part inside its place's opening
  hours; H5, demanding sessions at least `BetweenDemanding` apart, **start to start** in elapsed time (the
  requirements don't say from what). So "24 hours" is the same clock time each day, except across a clock change: on
  28 March 2027, Saturday 18:00 GMT to Sunday 18:00 BST is 23 hours (review L-4). H8 is checked once per goal: a
  footprint longer than a day is `Blocked`, as is travel that can't be worked out (P1).
- **Scores** (`Planner.Scores`, one place, higher is better; ties go to the earliest start): preferred day +30;
  preferred time of day +20 (by start time: morning 05:00-12:00, midday 11:00-14:00, afternoon 12:00-17:00, evening
  17:00-22:00, `Planner.HoursOf`); +10 for each day from the nearest other session of the goal, up to 3 (none: 3);
  -1 for each whole half hour already taken up that day (daily load, `CalendarDays.Split`, so after midnight counts
  towards the next day); +1 for each quarter hour free before and after the footprint, up to an hour each side
  (slack); +1 for each day before the week's last. The reasons shown with a suggestion name what scored: "Your
  preferred day", "In the evening, as you prefer", "3 days from your nearest other Gym", "Nothing else planned that
  day" or "A light day" (under 2 hours).
- **Greedy**: each session takes the best time left, so the week isn't guaranteed to be the best possible one
  (§7.4): with 48 hours between demanding sessions it picks Monday, Thursday, Sunday, where Monday, Wednesday, Friday,
  Sunday would fit four (`PlannerTests.DemandingSessions_AreKeptApart_AndTheRuleIsNamed`).
- **Shortfalls** (SCH-5): the sessions that didn't fit, and a reason for each day that isn't over and doesn't already
  have as many of the goal's sessions as a day allows (a day with one of two allowed is listed, review L-3), from the
  first rule its times break: "You said no to this day"; "A recovery day, and Intervals is demanding"; "It already
  has 1 demanding session, the most for a day"; when every time is busy, "The longest free time is 1 h 25 min; Gym
  needs 2 h 10 min with getting ready, travel and changing" (T14), or, when that time is long enough but no
  quarter-hour start fits in it, "The longest free time, 17:40 to 19:55, is 2 h 15 min, but with sessions starting on
  the quarter hour, Gym (2 h 10 min …) doesn't fit in it" (review M-1); when every free time is outside opening hours,
  that day's own hours: "Town gym is closed that day" or "… is open 06:00 to 09:00, and your free time then is too
  short" (not the day before's late opening, review L-1); otherwise the nearest demanding session and the rest rule
  (T13). When no day gives a reason, `Shortfall.Note` says why ("Every day left this week already has a Gym session
  (one a day)", review M-2). `Shorter`, worked out once every goal has its times so it is never a time given to
  another (review M-4): the longest session, in 5-minute steps down to 15, that would fit, with its time, its length
  written as other lengths are ("A session of 1 h would fit: Mon 2 Nov 2026 at 17:30").
- **Free time** (CAL-7, `WeekPlan.FreeTime`): each day not over, from `Now`, midnight to midnight, the stretches not
  busy (sleep windows and footprints): in all and the longest, before any suggestion. The longest-free-time reasons
  use the same stretches.
- **Other times for one session** (SCH-6, T16, `Planner.Options`): the same rules and scores, for one session (a
  missed or skipped occurrence, or one to move) in its week from `Now` on: the best time on each day that has one,
  best first, at most `most`. The caller passes the activities with that occurrence cancelled, so it isn't in its own
  way, and nothing else moves. The Web prefers the part of the day it was in and leaves out the time it has now. It
  plans it as its goal (`Activity.GoalId`, or the activity's own id when it has none) and gives the activity's other
  occurrences that `GoalId`, so one session a day and the spread count them (review M-2: a daily series was offered
  days that already had one; test `ReschedulePageTests.OtherTimes_AreNotOnADayThatAlreadyHasTheSession`). None
  fitting gives the same day reasons as a shortfall.
- Tests: `PlannerTests`, including every suggestion passing `ActivityCheck.Check` as a one-off (SCH-1), T2, T13, T14,
  T16, T22.

## Not built yet: read the requirements, don't guess

| Area | Requirements | Phase |
|---|---|---|
| The rest of the one validator on save: sleep minimum (H3); the planner keeps the sleep window free already | SCH-1, §7.2, SLP-4 | 8 (H1, H4, H8 built in 4; H5-H7 in 6) |
| Templates and their base location | CAL-9, TRV-4, T15 | 6 (if the owner still wants them) |
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
| T2 | Targets can't fit; shortfall explained | **Covered** (domain) | `PlannerTests.TargetsThatDontFitAroundWork_AreExplainedPerDay_WithNoOverlaps` |
| T3 | 22:30-00:30: start day; 90/30 split | **Covered** | `CalendarDaysTests.AnActivityCrossingMidnight_*` |
| T4 | Clocks back: sleep 9 h; 01:30 series once, first 01:30 | **Covered** | `ClockTimesTests.SleepFrom2300To0700_*`, `SeriesTests.ADailySeriesAt0130_OccursOnce_*` |
| T5 | Clocks forward: 01:30 series at 02:30; sleep 7 h | **Covered** | `ClockTimesTests.SleepFrom2300To0700_*`, `SeriesTests.ADailySeriesAt0130_OccursAt0230_*` |
| T6 | London to New York: series keep clock times; logged sleep unchanged | **Series half covered** (domain and stored activities); the sleep half needs the sleep log (Phase 8) | `SeriesTests.InAnotherZone_KeepsTheClockTime`, `OccurrenceChangeTests.InAnotherZone_*`, web `ActivitiesTests.ChangingTheTimezone_*` |
| T7 | Weekly, fortnightly, end date, count | **Covered** | `SeriesTests` |
| T8 | Cancel one, move one, edit series: both kept, reports 2 | **Covered** | `OccurrenceChangeTests.EditingTheSeries_KeepsBothOneOffChanges_AndReportsTwo` |
| T9 | Commute and gym travel counted once each | **Covered** (domain) | `DailyLoadTests.AnOfficeDayAndAnEveningGymSession_*` |
| T10 | Buffer overlapping another activity is a conflict | **Covered** (domain and on save) | `ConflictsTests.ChangingTimeRunningInto*`, `PreparationTimeOverlapping*`, `ActivityCheckTests.AClash_IsFoundOnFootprints_*`, web `ActivityChecksTests.Add_WhoseTravelRunsIntoAnotherActivity_Clashes` |
| T11 | Back-to-back: no conflict; 1 min overlap: conflict | **Covered** (domain and on save) | `ConflictsTests.BackToBackActivities_*`, `OverlappingByOneMinute_*`, web `ActivityChecksTests.Add_BackToBackWithAnotherActivity_IsSaved` |
| T12 | Late event below sleep minimum | **Planner half covered**: never suggested into the sleep window; the manual edit's confirmation is Phase 8 | `PlannerTests.NoSuggestion_CutsIntoSleep` |
| T13 | Demanding sessions too close | **Covered** (planner and on save) | `PlannerTests.DemandingSessions_AreKeptApart_AndTheRuleIsNamed`, `ActivityCheckTests.ADemandingSessionTooCloseToAnother_IsAProblem`, web `ActivityChecksTests.Add_ADemandingSessionTooSoonAfterAnother_*` |
| T14 | Busy day: largest gap and footprint explained | **Covered** (domain) | `PlannerTests.ABusyDay_SaysItsLongestFreeTime_AndWhatTheSessionNeeds` |
| T15 | "Brighton weekend" template | Not built (Phase 6) | |
| T16 | Reschedule a missed workout | **Covered** (domain and page) | `PlannerTests.AMissedSession_*`, web `ReschedulePageTests` |
| T17 | Gym opens 06:00, travel 05:35: valid | **Covered** (domain and on save) | `OpeningHoursTests.AWorkoutStartingAtOpeningTime_*`, `ActivityCheckTests.AWorkoutStartingAtOpeningTime_*`, web `ActivityChecksTests.Add_AtAGymWithOpeningHours_MustBeInsideThem` |
| T18 | A/B template: three weeks later is week B | **Covered** | `WeekCycleTests.ATwoWeekCycle_IsInWeekB_ThreeWeeksAfterWeekA` |
| T19 | Another user's data is "not found" | **Covered for every page so far** (locations, travel times, activities, preferences) | `FitnessTracker.Web.Tests` `Security/AccessControlTests`, `PreferencesTests` |
| T20 | Past planned session shows "Not logged" | Not built (Phase 7) | |
| T21 | Calendar at 320 px | **Checked by hand** (a browser check, Phase 5) | |
| T22 | Same week planned twice: identical | **Covered** (domain) | `PlannerTests.TheSameWeekPlannedTwice_GivesTheSameSuggestions` |
| T23, T24 | Meal prep; AI output | After the MVP | |
