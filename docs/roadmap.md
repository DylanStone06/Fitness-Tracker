# Roadmap

Requirement IDs (ACC-, CAL-, SCH-, …) and test scenarios (T1–T24) refer to [requirements.md](requirements.md).
Each phase gets its own branch (`phase-N-short-name`). The owner merges and pushes.

| # | Phase | Status | Objective | Main files | Key tests | Done when |
|---|---|---|---|---|---|---|
| 1 | Discovery & requirements | **Done** | Agree stack, MVP and workflow | `docs/`, `CLAUDE.md`, `.claude/skills/` | — | Owner approves the requirements |
| 2 | Foundation | **Done** | A runnable, tested skeleton | `.slnx`, `global.json`, `Directory.Build.props`, `.editorconfig`, `.gitattributes`, .NET `.gitignore`, `dev/ft-dev.ps1`, Identity + SQLite + first migration | Register/sign in; user B gets "not found" for user A's data (T19, first version) | `ft-dev check` passes; app runs on localhost |
| 3 | Time & recurrence (domain only) | **Done** | Occurrences that are correct across midnight, DST and timezones | `Domain/Time`, `Domain/Recurrence`, `scheduling-rules` skill, `scheduling-reviewer` agent | T3–T8, T18 | All pass; first `scheduling-reviewer` review done |
| 4 | Preferences, locations, commitments | **In progress** | Users can enter their real week | Preference, location and activity controllers and views; footprint calculation; migrations | T1, T9, T10, T11, T17, T19 | Conflicting saves are flagged with reasons |
| 5 | Weekly calendar | Not started | The central screen | `Views/Calendar`, `wwwroot/js/calendar.js`, FullCalendar (pinned), calendar data endpoint | Data endpoint scoped to user, date range and timezone; `break-ui`; accessibility; T21 | Day, week and agenda views; create, edit, drag; "this one" vs "whole series" |
| 6 | Scheduling engine | Not started | "Plan my week" with honest explanations | `Domain/Scheduling` (free time, hard constraints, scoring, planner, explanations), planner screen | T2, T12–T16, T22 | Every suggestion passes the shared validator; impossible targets are explained, never overlapped |
| 7 | Workouts | Not started | Planned vs completed | `Domain/Workouts`, workout views | Status changes, pace, km/mi conversion, T20 | Log strength and runs against planned sessions; skip and reschedule |
| 8 | Sleep & recovery | Not started | Protect sleep | `Domain/Sleep`, sleep views | T4, T5, T12, weekly summary | Sleep log; sleep conflicts flagged before confirming |
| 9 | Dashboard, data rights, hardening | Not started | **MVP release** | Dashboard, export and delete, setup and deployment docs | Full suite; `/security-review`; `claude-security` scan; accessibility; mobile | Release checklist complete |
| 10+ | After the MVP | — | Meal prep → analytics → notifications → AI | — | T23, T24 | One milestone at a time |

## Requirement status

Only requirements with work done are listed. What's missing is in [known limitations](known-limitations.md); how
the parts fit is in [architecture notes](architecture.md).

| ID | Status | Phase | Notes |
|---|---|---|---|
| ACC-1 | Done | 2 | Register, sign in, sign out. No email (documented limitation). |
| ACC-2 | Done | 2 | Sign-in required everywhere; per-user isolation in the data layer; another user's data is "not found". |
| ACC-3, ACC-4, ACC-7 | Done | 4 | Preferences page: timezone, week start, clock, units, sleep, exercise preferences. The 12-hour clock isn't applied to times shown yet (Phase 5). |
| ACC-5 | Done | 4 | Work is an ordinary activity (category Work, at the office, repeating on workdays); no separate record. |
| ACC-6 | Not started | - | Default buffers per category: owner to place (end of Phase 4 or Phase 6). |
| TRV-1 | Done | 2, 4 | Locations with a home and opening hours (closed days, closing after midnight). |
| TRV-2 | Done | 4 | Travel times per direction with a mode; footprints use them, a missing one is never zero. |
| CAL-2 | Done | 4 | Activities page: add, edit, delete with category, place, length, buffers, fixed/movable, priority, notes. |
| CAL-3 | Done | 3, 4 | Daily, weekly on days, every N, end date or count; stored as RRULE text. One occurrence's changes: Phase 5. |
| FIT-6 | Done | 4 | The "demanding" flag on activities (used by H5-H7 in Phase 6). |
| SCH-1 | Started | 4 | One check on every activity save: H1 (clashes, "Save anyway"), H4, H8, travel, never-happening repeats. H3, H5-H7 and other save paths in later phases. |
| GOL-2 | Started | 4 | Home page: what to set up, today and the next 7 days with when to leave and clashes. |
| T1, T9, T10, T11, T17 | Covered | 4 | Domain and page tests; names in the `scheduling-rules` skill. |
| CAL-4 | Domain done | 3 | Cancel, move, restore one occurrence; series edits keep and report one-off changes. Screens in Phase 5. |
| CAL-9 | Started | 3 | A/B week cycles (1-4 weeks, anchor week). Templates themselves in Phases 4-5. |
| §8 time rules | Domain done | 3 | Clock changes, elapsed durations, crossing midnight, timezone change for series. |
| §9 privacy and security | Partly done | 2 | Isolation enforced and tested, anti-forgery on every form, Identity password hashing, no secrets committed. Hosting hardening in Phase 9. |
| T19 | Covered so far | 2, 4 | Every page with an id (locations, travel times, activities), the lists, preferences, the check on save and the home page. Extended as each kind of record is added. |
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
6. Committed locally, one commit per change; the owner pushes.
