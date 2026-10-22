# Roadmap

Requirement IDs (ACC-, CAL-, SCH-, …) and test scenarios (T1–T24) refer to [requirements.md](requirements.md).
Each phase gets its own branch (`phase-N-short-name`). Claude pushes and opens the pull request where its tools allow
(the owner's decision, 20 October 2026); otherwise the owner does it from GitHub Desktop.

| # | Phase | Status | Objective | Main files | Key tests | Done when |
|---|---|---|---|---|---|---|
| 1 | Discovery & requirements | **Done** | Agree stack, MVP and workflow | `docs/`, `CLAUDE.md`, `.claude/skills/` | — | Owner approves the requirements |
| 2 | Foundation | **Done** | A runnable, tested skeleton | `.slnx`, `global.json`, `Directory.Build.props`, `.editorconfig`, `.gitattributes`, .NET `.gitignore`, `dev/ft-dev.ps1`, Identity + SQLite + first migration | Register/sign in; user B gets "not found" for user A's data (T19, first version) | `ft-dev check` passes; app runs on localhost |
| 3 | Time & recurrence (domain only) | **Done** | Occurrences that are correct across midnight, DST and timezones | `Domain/Time`, `Domain/Recurrence`, `scheduling-rules` skill, `scheduling-reviewer` agent | T3–T8, T18 | All pass; first `scheduling-reviewer` review done |
| 4 | Preferences, locations, commitments | **Done** | Users can enter their real week | Preference, location and activity controllers and views; footprint calculation; migrations | T1, T9, T10, T11, T17, T19 | Conflicting saves are flagged with reasons |
| 5 | Weekly calendar | **Done** | The central screen | `Views/Calendar`, `wwwroot/js/calendar.js`, FullCalendar (pinned), calendar data endpoint | Data endpoint scoped to user, date range and timezone; `break-ui`; accessibility; T21 | Day, week and agenda views; create, edit, drag; "this one" vs "whole series" |
| 6 | Scheduling engine | **Started** | "Plan my week" with honest explanations | `Domain/Scheduling` (free time, hard constraints, scoring, planner, explanations), planner screen | T2, T12–T16, T22 | Every suggestion passes the shared validator; impossible targets are explained, never overlapped |
| 7 | Workouts | Not started | Planned vs completed | `Domain/Workouts`, workout views | Status changes, pace, km/mi conversion, T20 | Log strength and runs against planned sessions; skip and reschedule |
| 8 | Sleep & recovery | Not started | Protect sleep | `Domain/Sleep`, sleep views | T4, T5, T12, weekly summary | Sleep log; sleep conflicts flagged before confirming |
| 9 | Dashboard, data rights, hardening | Not started | **MVP release** | Dashboard, export and delete, setup and deployment docs | Full suite; `/security-review`; `claude-security` scan; accessibility; mobile | Release checklist complete |
| 10+ | After the MVP | — | Meal prep → analytics → notifications → AI | — | T23, T24 | One milestone at a time |

Ideas that aren't agreed scope yet, with where each would fit: [ideas.md](ideas.md).

## Requirement status

Only requirements with work done are listed. What's missing is in [known limitations](known-limitations.md); how
the parts fit is in [architecture notes](architecture.md).

| ID | Status | Phase | Notes |
|---|---|---|---|
| ACC-1 | Done | 2 | Register, sign in, sign out. No email (documented limitation). |
| ACC-2 | Done | 2 | Sign-in required everywhere; per-user isolation in the data layer; another user's data is "not found". |
| ACC-3, ACC-4, ACC-7 | Done | 4 | Preferences page: timezone, week start, clock, units, appearance (dark by default), sleep, exercise preferences. Every page and explanation writes times the way chosen ("18:30" or "6:30pm"); form boxes take the browser's 24-hour time. |
| ACC-5 | Done | 4 | Work is an ordinary activity (category Work, at the office, repeating on workdays); no separate record. |
| ACC-6 | Done | 4 | "Default time around activities" page (linked from Preferences); a blank getting-ready or changing time on an activity gets its category's default. Built-in defaults for exercise until the user sets their own. |
| TRV-1 | Done | 2, 4 | Locations with a home and opening hours (closed days, closing after midnight). |
| TRV-2 | Done | 4 | Travel times per direction with a mode; footprints use them, a missing one is never zero. |
| CAL-2 | Done | 4 | Activities page: add, edit, delete with category, place, length, buffers, fixed/movable, priority, notes. Each category has one colour, shown with its name ([design](design.md)). |
| CAL-1, CAL-5, CAL-6, CAL-8, CAL-10 | Done | 5 | Calendar page: week, day and list views (list on phones, each title a link), from a data feed scoped to the user, a date range and their timezone; footprints as lighter bands behind the activity (CAL-5) and clashes in words (CAL-6). Times as on every other page, from the feed ("06:00 the next day", "01:30 BST"). The 12-hour clock and week start from Preferences apply. Selecting an empty time adds an activity there; adding or changing one from the calendar returns to its week. Dragging or resizing opens the occurrence's page (or a one-off's form) with the new time filled in, to check and confirm (CAL-8). Checked with worst-case data (`break-ui`) at 320 to 1440 px. |
| CAL-7 | Not started | 6 | Free time per day: comes with the planner's free-time maths. |
| SCH-2, SCH-3, SCH-4, SCH-5, SCH-7 | Started | 6 | "Plan my week" page: times for the sessions weekly goals still need, around everything planned, sleep and rest rules (§7.4, the scheduling-rules skill), with why each was chosen. Each can be added every week or just that week (Q12) through the ordinary activity form and its check on save (SCH-1), changed first, or refused for that day. What doesn't fit is explained per day with numbers, with a shorter session that would. The same week always plans the same (T22). |
| GOL-1 | Started | 6 | Weekly goals page: a name, category, how many times a week, how long each, where, getting ready and changing, demanding, priority, and preferred days and time of day. No time of its own: "Plan my week" finds it (Q12). Running distance and minutes per category: later (see known limitations). |
| CAL-3 | Done | 3, 4 | Daily, weekly on days, every N, end date or count; stored as RRULE text. One occurrence's changes: Phase 5. |
| FIT-6 | Done | 4 | The "demanding" flag on activities (used by H5-H7 in Phase 6). |
| SCH-1 | Started | 4, 5, 6 | One check on every activity save: H1 (clashes, "Save anyway"), H4, H8, travel, never-happening repeats, and H5-H7 for demanding sessions (Phase 6, from Preferences). Changes to one occurrence and accepted plan suggestions go through it too. H3 (sleep) in Phase 8. |
| GOL-2 | Started | 4 | Home page: a greeting, what to set up, the next activity (under way until you're back), today and the next 7 days with when to leave and clashes; each activity opens its edit page. |
| T1, T9, T10, T11, T17 | Covered | 4 | Domain and page tests; names in the `scheduling-rules` skill. |
| CAL-4 | Done | 3, 5 | An occurrence of a repeating activity (from the calendar or home page) opens its own page: move or resize just this one, cancel it, put it back, or change every occurrence. Changes are stored with the activity (OccurrenceChangesText); editing the whole activity keeps those still on its days and says how many were kept or removed (T8). Only what a change breaks is reported. |
| CAL-9 | Started | 3 | A/B week cycles (1-4 weeks, anchor week). Templates themselves weren't built in Phases 4-5; planned for Phase 6 with the planner and T15 ("Brighton weekend"). |
| §8 time rules | Domain done | 3 | Clock changes, elapsed durations, crossing midnight, timezone change for series. |
| §9 privacy and security | Partly done | 2 | Isolation enforced and tested, anti-forgery on every form, Identity password hashing, no secrets committed. Hosting hardening in Phase 9. |
| T19 | Covered so far | 2, 4, 5 | Every page with an id (locations, travel times, activities, an occurrence), the lists, preferences, the check on save, the home page and the calendar feed. Extended as each kind of record is added. |
| T21 | Checked by hand | 5 | The calendar at 320 px in the built-in browser: viewed (list, day), opened and changed an occurrence, no sideways scrolling, with worst-case data. A browser check, so not in the automated suite (requirements §10). |
| T3, T4, T5, T7, T8, T18 | Covered | 3 | Domain tests; the catalogue with test names is in the `scheduling-rules` skill. |
| T6 | Half covered | 3 | Series keep clock times; "logged sleep unchanged" needs the sleep log (Phase 8). |

## Definition of done (every phase)

1. `dev\ft-dev.ps1 check` passes: formatting check, build with warnings as errors, every test.
2. New behaviour has tests, including the T-scenarios listed for the phase.
3. Reviewed: `/code-review` on the changes; `scheduling-reviewer` for scheduling, recurrence or sleep code;
   `break-ui` and `accessibility` for UI.
4. Docs updated (requirements status, architecture notes, known limitations).
5. A milestone report: what was built, why, files changed, tests run with **actual** results, open issues, decisions
   needed, next step.
6. Committed, one commit per change, and pushed with a pull request (by Claude where its tools allow, otherwise the
   owner).
