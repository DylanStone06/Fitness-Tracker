# Product requirements: Fitness & Weekly Routine Planner

| | |
|---|---|
| Status | **Approved as written**, 13 October 2026 (open questions keep their defaults) |
| Date | 13 October 2026 |
| Owner | Dylan Stone |
| Related | [ADR 0001: technology stack](decisions/0001-stack.md) · [Roadmap](roadmap.md) · [Skills and agents](skills-and-agents.md) |

## 1. What this is

A planner that fits fitness around real life: work, commuting, sleep, travel, meal prep, social plans and recovery.
It is not just a workout logger or a calendar. It knows that a 50-minute gym session costs two hours once travel
and changing are counted, and it says plainly when a week's goals won't fit instead of quietly overbooking you.

## 2. Principles

| # | Principle |
|---|---|
| P1 | **Hard constraints are never silently broken.** If something can't fit, say so, say why, and offer realistic alternatives. |
| P2 | **The planner is deterministic**: explicit rules plus a scoring system. AI (later) only proposes; every proposal passes the same checks as a manual edit. |
| P3 | **Nothing is marked done because its time has passed.** Only the user marks an activity completed. |
| P4 | **More training isn't automatically better.** Recovery, rest gaps and the user's own limits count. |
| P5 | **Changing one occurrence never silently changes the others.** |
| P6 | **Private by default.** Users see only their own data; nothing leaves the app without explicit consent. |
| P7 | **Progressive disclosure.** Simple first, detail on demand. |

## 3. Users and defaults

- First user: the owner, on desktop and phone. Built **multi-user-safe from day one** (each user's data isolated and
  tested), but not offered as a public service in the MVP.
- Defaults, all changeable: timezone Europe/London, week starts Monday, 24-hour clock, miles and kilograms (UK units:
  the owner, 4 November 2026, Q25; kilometres until then).

## 4. Glossary

| Term | Meaning |
|---|---|
| **Activity** | Anything that takes time: work, gym, run, commute, meal prep, social plans, appointments. |
| **Series** | A repeating activity ("Gym, Mon/Wed/Fri 07:00, weekly"). Defined in clock time plus a timezone. |
| **Occurrence** | One instance of a series on one date. Worked out from the series when needed; stored only if changed or logged. |
| **Override** | A change to one occurrence (moved, resized, cancelled), stored against the series and that occurrence's original start. |
| **Footprint** | The whole block of time an activity reserves: preparation + travel there + the activity + changing/shower + travel back. **Conflicts are checked on footprints, not on the bare activity.** |
| **Buffer** | Preparation before, or changing/wind-down after, an activity. |
| **Location** | Home, office, gym, etc., optionally with opening hours. |
| **Base location** | Where the user starts and ends the day: home, or a travel template's base (e.g. a Brighton flat). |
| **Travel leg** | Time between two locations, **one direction**: home→gym may differ from gym→home. |
| **Routine template** | A named set of series for a kind of week (normal, travel, holiday, busy). Can cycle over 1–4 weeks (A/B weeks). |
| **Fixed / movable** | Fixed activities are never moved by the planner. Movable ones may be suggested elsewhere. |
| **Hard constraint** | A rule the planner must never break (§7.2). |
| **Soft preference** | A rule that only changes how good a slot scores (§7.3). |
| **Weekly target** | A goal such as "3 gym sessions" or "20 km running" per week. |
| **Session status** | Planned, Completed, Skipped or Rescheduled. A past planned session shows as **Not logged**, which is a display state, never a completion. |

## 5. Scope at a glance

| Area | MVP | Later |
|---|---|---|
| A. Accounts & preferences | Register, sign in/out, preferences, export, account deletion | Email confirmation, emailed password reset, external sign-in |
| B. Calendar & templates | Day/week/agenda, series, single-occurrence edits, every-N-weeks, A/B templates, drag & drop, conflicts, free time | Side-by-side comparison, "this and following" edits |
| C. Scheduling engine | One validator on every save, slot finder, scoring, suggestions with explanations, accept/reject, reschedule a missed session | Location chaining, multi-week optimisation |
| D. Fitness tracking | Strength (sets/reps/weight), runs (type, distance, time, pace), other activities by duration, statuses | Templates/splits, substitutions, personal records, charts, interval structures, heart rate/elevation |
| E. Sleep & recovery | Sleep targets, sleep log, sleep-opportunity checks, rest gaps between demanding sessions, weekly summary | Consistency score, recovery-rating trends |
| F. Work, commute, travel | Work pattern, office/remote days, commute legs, locations, directional travel legs, travel templates | Location-only activities, dated trips that switch templates automatically, timezone changes while away |
| G. Meal prep | Meal prep as an ordinary activity category (it takes calendar time) | Plans, recipes, batch sessions, shopping lists, allocations, storage notes, optional macros |
| H. Goals & progress | Weekly targets, dashboard | Monthly targets, habits, streaks, trends |
| I. Notifications | None | Reminders, prompts, missed-session handling |
| J. AI assistance | None | Natural-language planning behind consent and the validator |

## 6. Requirements

IDs are referenced from tests and commit messages. **[MVP]** = first release; **[Later]** = after the MVP.

### A. Accounts and preferences

- **ACC-1 [MVP]** Register with email and password, sign in, sign out. *MVP sends no email, so there is no email
  confirmation or emailed password reset; documented limitation.*
- **ACC-2 [MVP]** Every page except sign-in and register needs a signed-in user. A user can only read or change their
  own data; anyone else's returns "not found" (not "forbidden", which would confirm it exists).
- **ACC-3 [MVP]** Preferences: timezone (IANA name), week start day, 12/24-hour clock, distance unit (km/mi), weight
  unit (kg/lb), appearance (dark by default, light, or matching the device; added by the owner on 20 October 2026).
- **ACC-4 [MVP]** Sleep preferences: target sleep, minimum sleep, usual bedtime and wake time, wind-down before bed
  (default 30 min), wake-up routine before the first commitment (default 30 min).
- **ACC-5 [MVP]** Work pattern: workdays and hours, office or remote per day, commute legs for office days.
- **ACC-6 [MVP]** Default buffers (preparation, changing) per activity category.
- **ACC-7 [MVP]** Exercise preferences: preferred days and time of day, recovery days, minimum hours between demanding
  sessions, maximum demanding sessions per day.
- **ACC-8 [MVP]** Export all of my data as one JSON file.
- **ACC-9 [MVP]** Delete my account and all its data, after re-entering my password. Immediate and permanent.

### B. Calendar and routine planner

- **CAL-1 [MVP]** Week (time grid), day and agenda (list) views; move between weeks; jump to today.
- **CAL-2 [MVP]** Create, edit and delete activities: title, category (colour), location, start, end or duration,
  fixed/movable, priority, buffers, notes.
- **CAL-3 [MVP]** Repeat daily, weekly on chosen days, or every N weeks (fortnightly = 2); end on a date, after a count,
  or never.
- **CAL-4 [MVP]** Edit or delete **one occurrence** without touching the rest, or edit the **whole series**. Editing the
  series keeps individual changes and tells the user how many occurrences have them.
- **CAL-5 [MVP]** Footprints are visible: travel and buffers are drawn as lighter segments joined to the activity, so a
  50-minute workout reads clearly as a two-hour commitment.
- **CAL-6 [MVP]** Conflicts: overlapping footprints show a warning (icon and text, not colour alone) on both items.
- **CAL-7 [MVP]** Free time per day: total, and the largest free gap, excluding sleep and footprints.
- **CAL-8 [MVP]** Drag and drop to move or resize; for an occurrence, ask "just this one" or "whole series". Every drag
  action is also possible from the edit form (keyboard and screen-reader users).
- **CAL-9 [MVP]** Routine templates: create, duplicate, rename; cycle of 1–4 weeks with an anchor week (A/B weeks);
  apply a template to a date range; each week shows its template and cycle week.
- **CAL-10 [MVP]** Usable at 320 px wide; on phones the default view is day or agenda.
- **CAL-11 [Later]** Compare two templates or weeks side by side (load per day, free time, targets met).
- **CAL-12 [Later]** "This and following" edits (splits a series in two).

### C. Constraint-aware scheduling engine

The heart of the product. The rules themselves are in §7.

- **SCH-1 [MVP]** One validator checks every change before it is saved: manual edits, accepted suggestions, imports,
  and later AI proposals.
- **SCH-2 [MVP]** Find genuinely available slots for a movable activity: the footprint fits a free gap, the activity
  part is inside opening hours, sleep is protected, rest rules hold.
- **SCH-3 [MVP]** "Plan my week": from the weekly targets, propose slots for the sessions still missing, best first.
- **SCH-4 [MVP]** Each suggestion can be accepted, rejected or adjusted. An adjusted suggestion is validated again.
- **SCH-5 [MVP]** When a target can't be met, explain why in plain English, per day, with the numbers ("Tuesday: the
  largest free gap is 85 min; a gym session needs 130 min"), and offer alternatives: a shorter session, a different
  activity, a different day, or a lower target.
- **SCH-6 [MVP]** Rescheduling a missed or skipped session offers slots in the rest of the week **without moving
  anything else**.
- **SCH-7 [MVP]** The same inputs always give the same suggestions (no randomness).
- **SCH-8 [Later]** Chained locations (office → gym → home) and multi-week planning.

### D. Fitness and workout tracking

- **FIT-1 [MVP]** Session statuses: Planned, Completed, Skipped, Rescheduled; past planned sessions show "Not logged".
  **Never completed automatically.**
- **FIT-2 [MVP]** Strength log: exercises (a starter list plus custom ones), sets with reps and weight, optional rest
  time, notes.
- **FIT-3 [MVP]** Run and cardio log: type (easy, long, tempo, intervals, recovery, custom), planned and actual duration,
  distance, pace (calculated), optional perceived effort (1–10).
- **FIT-4 [MVP]** Other activities (walk, cycle, swim, mobility, custom): duration and optional intensity.
- **FIT-5 [MVP]** Planned versus completed for the week, per target.
- **FIT-6 [MVP]** A "demanding" flag on activities, used by the recovery rules.
- **FIT-7 [Later]** Workout templates and splits, substitutions, personal records, progress charts, interval
  structures, heart rate and elevation, distance trends.

### E. Sleep and recovery

- **SLP-1 [MVP]** Planned sleep window from preferences, with per-night changes (e.g. an early start).
- **SLP-2 [MVP]** Log actual sleep: bedtime, wake time, optional quality, energy and soreness (1–5). Duration is real
  elapsed time, correct across midnight and clock changes.
- **SLP-3 [MVP]** Sleep opportunity per night = from the end of the evening's last footprint plus wind-down, to the start
  of the next morning's first footprint minus the wake-up routine. Shown on the calendar.
- **SLP-4 [MVP]** If a change would cut a night's sleep opportunity **below the minimum**, the planner won't suggest it
  and a manual edit needs explicit confirmation **before saving**. Below target but above minimum: warning only.
- **SLP-5 [MVP]** Weekly sleep summary: average sleep, nights below target, nights below minimum.
- **SLP-6 [Later]** Consistency score; energy, soreness and recovery trends.

### F. Work, commuting and travel

- **TRV-1 [MVP]** Locations with optional opening hours per weekday, including closed days and closing after midnight.
- **TRV-2 [MVP]** Directional travel legs with a mode label (walk, drive, train, etc.). Example: home → gym 25 min,
  gym → home 45 min.
- **TRV-3 [MVP]** A commute is its own pair of blocks on office days. It is never counted inside a workout footprint,
  and a workout's travel is never counted inside a commute.
- **TRV-4 [MVP]** A template can change the base location (e.g. "Brighton weekend": base is the Brighton flat, the home
  gym is out of reach, running is still possible).
- **TRV-5 [Later]** Activities only possible at certain locations; dated trips that switch template automatically;
  changing timezone while away.

### G. Meal planning and preparation [Later]

Full scope as in the original brief: weekly meal plans, batch-cooking sessions split over one or more days, recipes
and ingredients, portions, shopping lists, workday lunches, prepared meals allocated to days, fridge/freezer notes,
leftovers and optional macros. **Meal prep is always a calendar activity, not just a recipe list.** No nutritional or
medical claims without evidence.

*MVP stand-in:* "Meal prep" is an ordinary activity category with buffers, so it already takes calendar time and is
checked by the validator.

### H. Goals and progress

- **GOL-1 [MVP]** Weekly targets: number of gym sessions, number of runs, running distance, count or minutes for any
  activity category.
- **GOL-2 [MVP]** Dashboard: today's timeline, the next activity, free time left today, tonight's sleep opportunity
  against target, weekly target progress, preparation starting soon.
- **GOL-3 [Later]** Monthly targets, custom habits (e.g. hydration), streaks, trends, missed and rescheduled analysis.

### I. Notifications [Later]

Activity, preparation, bedtime and meal-prep reminders; a weekly planning prompt; missed-session prompts; conflict
alerts. Configurable, rate-limited, with quiet hours. Needs background jobs and a delivery channel, chosen at the time.

### J. AI-assisted planning [Later]

Natural language → structured activities; suggested training distributions; explanations and summaries. Rules: off by
default; an explicit consent screen naming the provider and exactly what is sent; the minimum data sent; output parsed
into the same structures and checked by the validator (SCH-1) before it is shown as a proposal; **never saved
automatically**.

## 7. Scheduling rules

### 7.1 Footprint

`footprint = preparation + travel out + activity + changing + travel back`

Travel applies when the activity's location differs from the base location. Opening hours apply to the **activity
part only** (travel happens outside the gym).

> **Required example:** a gym session with 25 min travel there, a 50 min workout and a 45 min walk home reserves
> **exactly 120 min**; with 10 min changing it reserves **130 min**.

### 7.2 Hard constraints

The planner never breaks these. A manual edit that would break **H1 or H3** needs explicit confirmation and is then
shown on the calendar as an *accepted conflict*, so it's a visible decision, never a silent one. The others are
validation errors.

| # | Rule |
|---|---|
| H1 | Footprints don't overlap. Intervals are half-open: one ending at 10:00 and another starting at 10:00 don't conflict. |
| H2 | Fixed activities are never moved by the planner. |
| H3 | Each night's sleep opportunity is at least the minimum sleep. |
| H4 | The activity part is inside the location's opening hours. |
| H5 | Demanding sessions are at least the configured number of hours apart. |
| H6 | No more than the configured number of demanding sessions per day. |
| H7 | No demanding session on a recovery day. |
| H8 | Durations are positive; a footprint is at most 24 hours, or the length of the day its activity starts on when that day is longer, such as the 25-hour day the clocks go back (Q7). |

### 7.3 Soft preferences (scoring)

Each slot that passes the hard constraints gets a score from these, shown to the user as reasons ("Preferred day",
"Keeps 2 days between runs"):

- preferred day, and preferred time of day;
- spread: distance from other sessions of the same kind;
- daily load: total footprint minutes already that day (lower is better);
- slack: free time either side of the footprint;
- a small bonus for earlier in the week, which leaves room to catch up.

Weights live in one place in code and are documented in the scheduling-rules reference (created in Phase 3).

### 7.4 How "Plan my week" works (MVP)

1. Work out every occurrence in the week (series, overrides, active template).
2. Busy time = all footprints + sleep windows (with wind-down and wake-up routine).
3. Free gaps = everything else in the week.
4. For each missing session, highest priority first: try start times every 15 minutes in the free gaps; keep those that
   pass H1–H8; score them; take the best; add it to busy time; repeat.
5. For every day with no valid slot, record the constraint that ruled it out and the numbers involved.
6. If targets are still short, report the shortfall with the reasons from step 5 and the alternatives from SCH-5.

This is a greedy method in priority order: fast, explainable and predictable, **but not guaranteed to find the best
possible week**. That limitation is documented to the user in the help text.

## 8. Time and date rules

- **Series** are stored as clock time plus an IANA timezone (default: the user's). Occurrences are converted to exact
  moments before any comparison.
- **Logged times** (sleep, completed sessions) are stored as exact moments in UTC.
- **Clocks going forward** (Europe/London, 01:00 to 02:00 on 28 March 2027 doesn't exist): an occurrence in the gap
  moves forward by the gap (01:30 becomes 02:30).
- **Clocks going back** (01:00 to 02:00 on 25 October 2026 happens twice): use the first, earlier one.
- **Durations are elapsed time.** Sleeping 23:00 Saturday 24 October 2026 to 07:00 Sunday 25 October 2026 is
  **9 hours**; 23:00 Saturday 27 March 2027 to 07:00 Sunday is **7 hours**.
- **Crossing midnight:** an activity belongs to the day it starts (for the calendar and targets), but its minutes count
  towards each day's load for the part that falls on that day.
- **Changing the timezone preference:** series keep their clock times in the new zone ("gym at 7am wherever I am");
  logged history doesn't change. The user is told before it happens.
- **Weeks** follow the user's week start day, in their timezone.
- **Units:** stored in metric (metres, seconds, kilograms); converted only for display.

## 9. Non-functional requirements

| Area | Requirement |
|---|---|
| Privacy & security | Per-user isolation enforced in the data layer **and** tested. Anti-forgery tokens on every form. Passwords hashed by ASP.NET Core Identity. No secrets in committed files (development uses .NET user-secrets). No analytics or tracking. Front-end libraries are self-hosted at pinned versions. Error pages and logs show IDs, never personal details. |
| Accessibility | WCAG 2.2 AA. The calendar is keyboard-usable; drag has a form alternative; colour is never the only signal. |
| Responsive | Works from 320 px to wide desktop; touch targets at least 24 × 24 px. |
| Performance | Target: a week view, including working out occurrences and validation, for a user with about 100 series renders in under a second locally. Measured in Phase 9. |
| Reliability | Two tabs editing the same item can't silently overwrite each other (optimistic concurrency). |
| Data rights | Export (ACC-8) and permanent deletion (ACC-9). |
| Health data | Sleep and workout records may count as health data under UK GDPR if the app is ever offered publicly. Reassess before any public launch. *(A flag, not legal advice.)* |

## 10. Required test scenarios

Each becomes at least one automated test (T21 is a browser check). IDs are cited in test names or comments.

| # | Scenario | Expected |
|---|---|---|
| T1 | Gym: 25 min there, 50 min workout, 45 min walk back | Footprint exactly 120 min; with 10 min changing, 130 min |
| T2 | Weekly targets can't fit around work and commute | Shortfall explained per day; no overlaps created; sleep minimum intact |
| T3 | Activity 22:30–00:30 | Shown on the start day; load split 90/30 min across the two days |
| T4 | Clocks back, 25 Oct 2026 | Sleep 23:00→07:00 = 9 h; a 01:30 series occurs once, at the first 01:30 |
| T5 | Clocks forward, 28 Mar 2027 | A 01:30 series occurs at 02:30; sleep 23:00→07:00 = 7 h |
| T6 | User changes timezone London → New York | Series keep clock times; logged sleep unchanged |
| T7 | Weekly, fortnightly, end date, count | Correct occurrences across several months |
| T8 | Cancel one occurrence; move one; then edit the series | Others unchanged; the series edit keeps both individual changes and reports 2 |
| T9 | Office day with 30 min commute each way, evening gym from home | Commute and gym travel counted once each; no double-counting |
| T10 | Preparation or changing time overlaps another activity | Reported as a conflict |
| T11 | Back-to-back at 10:00; overlapping by one minute | First: no conflict. Second: conflict |
| T12 | Late event pushes sleep below minimum | Planner never suggests it; manual edit needs confirmation |
| T13 | Two demanding sessions closer than the minimum rest | Not suggested; explanation names the rest rule |
| T14 | Busy day with no slot | Explanation states the largest gap and the footprint needed |
| T15 | "Brighton weekend" template | Base location changes; home gym unavailable; runs still suggested |
| T16 | Reschedule a missed workout | Slots offered later in the week; nothing else moves |
| T17 | Gym opens 06:00; travel starts 05:35 | Valid: only the workout must be within opening hours |
| T18 | Two-week A/B template anchored on week A | Three weeks later is week B |
| T19 | User B requests user A's activity, series, log or export | "Not found" every time |
| T20 | A planned session's time passes | Shows "Not logged"; status stays Planned |
| T21 | Calendar at 320 px | Usable: view, create, edit (browser check) |
| T22 | Same week planned twice | Identical suggestions |
| T23 *[Later]* | Meal-prep allocation | Prepared meals allocated to days after the prep session ends |
| T24 *[Later]* | AI output with overlapping or invalid events | Rejected by the validator with reasons; nothing saved |

## 11. Out of scope for the MVP

Meal planning and shopping lists, notifications, AI assistance, analytics charts and personal records, workout
templates and splits, "this and following" edits, location chaining, sending email, external sign-in, native mobile
apps, and importing from or syncing with other calendars.

## 12. Open questions

The owner approved the requirements with these defaults on 13 October 2026. Any of them can still be revisited in a
later phase; the change is then recorded here.

| # | Question | Default |
|---|---|---|
| Q1 | How fine-grained should the planner be when trying start times? | Every 15 minutes |
| Q2 | Can a manual edit override H1 (overlap) or H3 (minimum sleep)? | Yes, with explicit confirmation; shown as an accepted conflict |
| Q3 | Which week is "week A" in a cycle? | The week the template is first applied |
| Q4 | What's in the starter exercise list? | About 30 common lifts and bodyweight exercises |
| Q5 | Where will it be hosted? | Decide in Phase 9 |

Decided by the owner on 20 October 2026, at the end of Phase 4:

| # | Question | Decision |
|---|---|---|
| Q6 | Does a night shift on a clock-change night end at its usual clock time? | No: lengths stay elapsed time (§8), so it ends an hour earlier or later by the clock. The form says so. |
| Q7 | Can an activity take up the whole of the 25-hour day the clocks go back? | Yes: H8's limit is 24 hours, or the length of the day the activity starts on when that day is longer (the day the clocks go back: 25 hours in the UK). |
| Q8 | Are activities re-checked when the timezone preference changes? | Yes, after the move. Problems and clashes are shown on the Preferences page; they don't stop the change. |
| Q9 | Default getting-ready and changing times per category (ACC-6)? | Built in Phase 4. |
| Q10 | Back-to-back activities at the same place (the office gym straight after work)? | After the MVP, with location chaining (SCH-8). |
| Q11 | Does a day's load count overlapping footprints each in full? | Yes, as §7.3's "total footprint minutes": an overlap there is a clash the user chose to keep. |

Decided by the owner on 21 October 2026, during Phase 5:

| # | Question | Decision |
|---|---|---|
| Q12 | What does accepting a "Plan my week" suggestion (SCH-4) add? | Either, chosen each time: the main button adds it every week at that day and time, from that week on (building a routine, since the owner plans a routine before knowing set times); a second button adds it for that week only. One week can then be changed on the calendar as any occurrence (CAL-4). Weekly targets (GOL-1) have no time of their own: the planner finds it. |

Decided by the owner on 4 November 2026. Q13-Q16 and Q20-Q29 were defaults chosen while building (Q13-Q16 in Phase 6b,
Q20-Q29 in Phase 7); Q17-Q19 were open. Where the owner answered "what you recommend", the decision is Claude's
recommendation, accepted. Those marked **to be built** differ from what the app does today until the change is made:

| # | Question | Decision |
|---|---|---|
| Q13 | Does an A/B cycle keep counting through a holiday week (another routine), or pause? | Keeps counting by the calendar, as a rota does: the week after a holiday in week B is week A. |
| Q14 | Which routine is in use on a date? | The one chosen for that date, if any (on the Routines page; ranges can't overlap); otherwise the usual routine, if there is one. Activities not in a routine happen every week, whatever the routine, so every activity from before Phase 6b is unchanged. |
| Q15 | When Plan my week plans week B and a session is added "every week", is it every week or every week B? | Every week B (the button says so), so planning week B builds week B and week A is untouched. With a routine that is the same every week, it is added to that routine; with no routines, to every week. |
| Q16 | What does duplicating a routine copy? | A new routine with the name given (offered as "… copy"), the same cycle and week A, not the usual one and with no dates chosen, so nothing new happens until it is used. Each activity in it is copied, in the same weeks, without changes made to single occurrences (those are on the original's dates). Activities in every week aren't in a routine, so aren't copied. |
| Q17 | An occurrence moved out of a week that is later given to a holiday (Monday 2 November moved to Friday 30 October, then 2-8 November chosen for Holiday): should it still happen where it was moved to? | **Yes (built 4 November 2026).** A session moved on its own happens where it was moved to when either end of the move is a date its activity is in use on: the date it was moved from, or the date it was moved to. So one moved out of the week later given to a holiday still happens (a session is often moved because you'll be away), while one moved within that week goes with the week, as an unmoved one does. A cancelled one stays cancelled. Its page says before "Put it back" that doing so would remove it, and moving it to a date where neither end is in use is refused, since it would disappear. Plan: [docs/plans/q17-moved-sessions.md](plans/q17-moved-sessions.md). |
| Q18 | A holiday routine with its own A/B cycle: is its week A the week it is first used (Q3), or the week it was added (what its form offers)? | The week set as its week A on its form (the week it was added, unless changed): one rule for every routine. Chosen for 9-22 November but added on 19 October, its first holiday week is week B, which the calendar and Plan my week say on each week; changing its week A changes that. |
| Q19 | Find another time, for a session inside a holiday's dates: should it only offer days inside them? | No: it offers any day later that week, like any other session. A session moved out of the holiday still happens. |
| Q20 | Which activities are sessions with a status (FIT-1)? | Those in an exercise category: Gym, Run, Other exercise. Work, commuting, social and the rest have no status. |
| Q21 | When can a session be marked done or skipped? | Done once it has started (never before, never by itself: FIT-1); skipped at any time ("not going tomorrow"). Either can be undone, back to planned. Cancelling one (CAL-4) is different: it is no longer planned at all, so it has no status. |
| Q22 | What is "Rescheduled"? | A session of a repeating activity moved on its own to another time (CAL-4: on the calendar, or with Find another time). It says where it was ("Rescheduled from Tue 3 Nov, 18:00") and counts as planned at its new time until it is marked. Not a separate choice. |
| Q23 | Does logging a session change the calendar? | No. The calendar keeps the planned time; the log records what was done (how long, and for each kind its details: a run's type, distance and effort; sets; intensity). |
| Q24 | What counts towards a weekly goal (FIT-5)? | **Built with FIT-5.** That week's sessions of the activities linked to the goal, each done, skipped, not logged or still to come, against how many the goal asks for. An activity is linked when it is added from Plan my week, or by choosing the goal on the activity's own form ("Counts towards"), so a session added by hand can count too. Not by category alone: two goals can share one (upper body and legs, both Gym). |
| Q25 | Units for distance, weight and pace? | Stored in metres and kilograms (CLAUDE.md rule 6), shown in the units chosen in Preferences (km or miles; kg or lb). UK units by default: a new account shows miles and kilograms. Pace is worked out from time and distance, never stored, and shown per km or per mile ("8:51 per mile"). |
| Q26 | What does a run's log hold (FIT-3)? | Its type (easy, long, tempo, intervals, recovery, or a custom name of up to 40 characters), the time it took, its distance and how hard it felt, each optional: saving with none just marks it done. The planned time is the session's own length, shown beside the time taken, never typed. Logged once the run has started. |
| Q27 | How are a run's time and distance typed and shown? | Time in three boxes (hours, minutes, seconds; up to 24 hours), shown as a stopwatch shows it ("28:36", "1:05:20"). Distance as a number with a decimal point ("5.2"), 0.01 to 1,000 in km or miles, stored to the nearest metre, shown with up to 2 decimals ("5.2 km", "3.11 miles", "1 mile"); its box is filled so that saving it unchanged gives back the same metres. |
| Q28 | What happens to a run's details when it is marked skipped, or back to planned? | They are removed: a skipped run has no distance. The page says so beside those buttons, before they are pressed. |
| Q29 | What do the effort numbers mean? | 1 very easy, 2-3 easy, 4-6 moderate, 7-8 hard, 9 very hard, 10 all out (a common perceived-effort scale), shown as "6 of 10, moderate". |

Decided by the owner on 4 November 2026 for the strength log (FIT-2), before it was built; the plan is
[docs/plans/phase-7-strength-log.md](plans/phase-7-strength-log.md):

| # | Question | Decision |
|---|---|---|
| Q30 | How is an exercise chosen? | Typed in a box that suggests as you type: the 30 starter exercises (Q4) and every exercise you've used. A name not on the list becomes your own exercise, with no separate page to set one up. Names are matched ignoring capitals. |
| Q31 | How are sets entered? | As rows (exercise, reps, weight, rest): six empty rows, and "Add more rows" for six more, with no script needed; rows left blank are ignored. Consecutive rows with the same exercise are its sets. Up to 60 sets a session. |
| Q32 | Is rest time recorded? | Optionally, per set, in seconds (up to an hour). |
| Q33 | How is weight stored? | In whole grams (100 kg is 100,000 g), exact as distances are in metres, and shown in kg or lb as chosen in Preferences (kg by default). CLAUDE.md rule 6 says so. |

Decided by the owner on 4 November 2026 for other activities (FIT-4), before they were built; the plan is
[docs/plans/phase-7-other-activities.md](plans/phase-7-other-activities.md):

| # | Question | Decision |
|---|---|---|
| Q34 | Which types of other activity? | Walk, cycle, swim, mobility, and custom with a name of up to 40 characters, as a run's. |
| Q35 | How is intensity recorded? | On the same 1-10 "How hard it felt" scale as runs (Q29), so every session's effort can be compared. |
| Q36 | Do they have a distance? | Optionally, any of them (meant for walks, cycles and swims), in km or miles, stored in whole metres. Shown on its own: no speed or swim pace yet (FIT-7). |

Decided by the owner on 4 November 2026 for planned against completed (FIT-5), before it was built; the plan is
[docs/plans/phase-7-goal-progress.md](plans/phase-7-goal-progress.md):

| # | Question | Decision |
|---|---|---|
| Q37 | Where does a goal's progress for the week show? | On the Goals page (this week), on Plan my week (the week it plans, in place of "1 of 3 already planned") and on the home page (a "This week" card). |
| Q38 | Does a skipped session still count towards its goal? | No: Plan my week then suggests another time that week. The skipped session stays on the calendar, marked skipped, and its own time stays busy, so it isn't offered. |
| Q39 | Which goals does an activity's "Counts towards" offer? | All of the user's weekly goals, each with its category beside its name, and "No goal". |

Built with Q38 (Claude's choice, found by the scheduling review on 4 November 2026, as the only one that agrees with the
check on save, SCH-1): a skipped demanding session still counts for the rest rules (H5-H7), as its time stays busy. So
with 48 hours between demanding sessions, a skipped Wednesday interval session still keeps its replacement away from
Monday evening to Friday evening. The owner can revisit it.

Built with Q38 (Claude's choice, 9 November 2026): on Plan my week, a time suggested in place of a skipped session has
"Just this week" as its main button, and its "Change" opens the form for just this week, when skipped sessions account
for every session the goal still needs that week. Added every week, it would be one more than the goal asks for in the
weeks after. When the goal is short of others as well, "Every week" leads, as adding them builds the routine. Next up
on the home page passes over a session marked skipped.

Built with Q39 (Claude's choice, from the browser check, 9 November 2026): a goal whose name is its category's ("Gym")
is listed by its name alone, not "Gym (Gym)".

Open, found by the scheduling reviews of 4 November 2026 (Q40) and 9 November 2026 (Q40's done sessions, Q41):

| # | Question | Recommendation |
|---|---|---|
| Q40 | A session marked skipped or done, then given another time (Find another time, moved on the calendar, or a one-off's time changed on its form, keeping its date): does it keep its mark? | Today it does: the mark stays with the date its rule gives the session. A skipped one says "Skipped" at its new time, doesn't count towards its goal, and Plan my week suggests a second replacement. A done one moved to a time still to come counts as done before it has started (against Q21), in the week it was moved to. **Recommended:** a skipped session given another time goes back to planned ("Rescheduled from …", Q22), keeping its notes. A done one can be moved only to a time that has already started (to correct when it happened, keeping its log); a time still to come is refused with the reason, as setting it back to planned would remove its details (Q28). The move page says which before saving. A one-off given another date already loses its mark (it stays with the old date: see the Workouts rows in [known-limitations.md](known-limitations.md)), so a skipped one would then agree. |
| Q41 | On Plan my week, a goal is short this week because of a skip and also a change to this week alone (a session cancelled, or moved into another week): which button leads? | Today "Every week": "Just this week" leads only when skipped sessions account for every missing session. With a goal of 2 on Mondays and Wednesdays, Monday's cancelled and Wednesday's skipped, both suggestions lead with "Every week", and taking it makes the routine 3 a week. A cancellation alone did the same before Q38. **Recommended:** count a session cancelled or moved out of the week like a skipped one here, so "Just this week" leads whenever changes to this week alone account for every missing session, and "Every week" when the usual week itself is short. |
