# Plan: Phase 7, step 5, planned against completed per goal (FIT-5)

Written 4 November 2026, before any code. FIT-5: "Planned versus completed for the week, per target." Q24 (decided):
a goal counts the sessions of the activities linked to it, from Plan my week or by choosing the goal on the activity's
own form ("Counts towards"). The owner chose Q37-Q39 the same day, each the recommended option.

## What it adds, as the user sees it

- **An activity's form** has "Counts towards": every weekly goal (with its category beside its name) or "No goal".
  Added from Plan my week it is already filled in, as now.
- **Each goal's week**: how many of its sessions are done, skipped, not logged and still to come, against how many the
  goal asks for, and how many aren't planned at all yet ("Gym: 1 of 3 done · 1 skipped · 1 to come"). A goal in a
  category without statuses (meal prep, Q20) says how many are planned.
- **Where** (Q37): on the Goals page (this week, under each goal), on Plan my week (the week it plans, in place of "1
  of 3 already planned"), and on the home page in a small "This week" card, each goal with a bar.
- **A skipped session** (Q38) no longer counts towards its goal, so Plan my week suggests another time that week. It
  stays on the calendar, marked skipped; its own time stays busy, so it isn't offered (and the check on save, which
  still sees it, agrees with the planner).

## Decisions (the owner's, 4 November 2026)

| # | Question | Decision |
|---|---|---|
| Q37 | Where does a goal's progress show? | The Goals page (this week), Plan my week (its week) and the home page ("This week" card). |
| Q38 | Does a skipped session still count towards its goal? | No: Plan my week suggests a replacement that week. Its own time stays busy and isn't offered. |
| Q39 | Which goals does "Counts towards" offer? | All of the user's goals, each with its category, and "No goal". |

## Rules (domain, test first)

- `GoalProgress` (Domain/Workouts): from a goal's target and the states of its sessions that week (`SessionState`,
  or none for a category without statuses): done, skipped, not logged, still to come, planned without statuses, and
  how many aren't planned yet (the target less every session but the skipped ones).
- The planner (Q38): `PlanRequest.Skipped` lists sessions marked skipped (activity and the date its rule puts them
  on); one of them doesn't count towards its goal (the number planned, one a day, the spread), and is still busy time.
  The `scheduling-reviewer` agent reviews this.

## Web

- `PlanningData.GoalWeeksAsync(week)`: for each goal, its sessions in those dates (the agenda, by the day each
  starts, §8) and their states, in one query for the marks, as the home page and calendar do (FIT-1).
- The activity form: "Counts towards" (a list), checked as locations are: a goal that isn't the user's is "Choose a
  goal from the list" (T19). Today a goal that isn't found is dropped quietly, because it was hidden; now it is a
  choice, so it is said.
- Plan my week passes the week's skipped sessions to the planner, and shows each goal's progress.
- Goals page and home page: each goal's progress this week (the user's week, in their timezone); a `<progress>` bar
  with the same words beside it, so the colour is never the only signal (design rule 1).

## Tests

Domain: progress counts (each state, a moved session, a category without statuses, more sessions than the target);
the planner suggests a replacement for a skipped session and doesn't offer its time. Web: the form's list, saving
with a goal, "No goal", another user's goal refused; progress on each page, only the user's own (T19); Plan my week
suggesting a replacement after a skip.

## Commits, in order

1. This plan and Q37-Q39.
2. Goal progress and the planner's skipped sessions (domain), test first; then the scheduling review.
3. "Counts towards" on the activity form, test first.
4. Progress on the Goals page, Plan my week and the home page, test first.
5. The browser check, then the docs.
