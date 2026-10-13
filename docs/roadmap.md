# Roadmap

Requirement IDs (ACC-, CAL-, SCH-, …) and test scenarios (T1–T24) refer to [requirements.md](requirements.md).
Each phase gets its own branch (`phase-N-short-name`). The owner merges and pushes.

| # | Phase | Status | Objective | Main files | Key tests | Done when |
|---|---|---|---|---|---|---|
| 1 | Discovery & requirements | **In review** | Agree stack, MVP and workflow | `docs/`, `CLAUDE.md`, `.claude/skills/` | — | Owner approves the requirements |
| 2 | Foundation | Not started | A runnable, tested skeleton | `.sln`, `global.json`, `Directory.Build.props`, `.editorconfig`, `.gitattributes`, .NET `.gitignore`, `dev/ft-dev.ps1`, Identity + SQLite + first migration | Register/sign in; user B gets "not found" for user A's data (T19, first version) | `ft-dev check` passes; app runs on localhost |
| 3 | Time & recurrence (domain only) | Not started | Occurrences that are correct across midnight, DST and timezones | `Domain/Time`, `Domain/Recurrence`, `scheduling-rules` skill, `scheduling-reviewer` agent | T3–T8, T18 | All pass; first `scheduling-reviewer` review done |
| 4 | Preferences, locations, commitments | Not started | Users can enter their real week | Preference, location and activity controllers and views; footprint calculation; migrations | T1, T9, T10, T11, T17, T19 | Conflicting saves are flagged with reasons |
| 5 | Weekly calendar | Not started | The central screen | `Views/Calendar`, `wwwroot/js/calendar.js`, FullCalendar (pinned), calendar data endpoint | Data endpoint scoped to user, date range and timezone; `break-ui`; accessibility; T21 | Day, week and agenda views; create, edit, drag; "this one" vs "whole series" |
| 6 | Scheduling engine | Not started | "Plan my week" with honest explanations | `Domain/Scheduling` (free time, hard constraints, scoring, planner, explanations), planner screen | T2, T12–T16, T22 | Every suggestion passes the shared validator; impossible targets are explained, never overlapped |
| 7 | Workouts | Not started | Planned vs completed | `Domain/Workouts`, workout views | Status changes, pace, km/mi conversion, T20 | Log strength and runs against planned sessions; skip and reschedule |
| 8 | Sleep & recovery | Not started | Protect sleep | `Domain/Sleep`, sleep views | T4, T5, T12, weekly summary | Sleep log; sleep conflicts flagged before confirming |
| 9 | Dashboard, data rights, hardening | Not started | **MVP release** | Dashboard, export and delete, setup and deployment docs | Full suite; `/security-review`; `claude-security` scan; accessibility; mobile | Release checklist complete |
| 10+ | After the MVP | — | Meal prep → analytics → notifications → AI | — | T23, T24 | One milestone at a time |

## Definition of done (every phase)

1. `dev\ft-dev.ps1 check` passes: formatting check, build with warnings as errors, every test.
2. New behaviour has tests, including the T-scenarios listed for the phase.
3. Reviewed: `/code-review` on the changes; `scheduling-reviewer` for scheduling, recurrence or sleep code;
   `break-ui` and `accessibility` for UI.
4. Docs updated (requirements status, architecture notes, known limitations).
5. A milestone report: what was built, why, files changed, tests run with **actual** results, open issues, decisions
   needed, next step.
6. Committed locally, one commit per change; the owner pushes.
