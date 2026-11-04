# Plan: Phase 7, step 3, the strength log (FIT-2)

Written 4 November 2026, before any code. FIT-2: "Strength log: exercises (a starter list plus custom ones), sets with
reps and weight, optional rest time, notes." Q4: the starter list is about 30 common lifts and bodyweight exercises.
The owner chose Q30-Q33 below on 4 November 2026, each the recommended option.

## What it adds, as the user sees it

A gym session's page (Gym category), once it has started, lets you log the workout: a table of sets, one per row:
**exercise, reps, weight, rest**. Consecutive rows with the same exercise are its sets. Saved, the page shows each
exercise with its sets ("Back squat: 5 × 100 kg, 5 × 100 kg, 5 × 102.5 kg") and totals (sets, and the weight lifted).

Every session (gym, run, other) also gets **notes** ("felt strong; left knee a bit sore").

## Decisions (the owner's, 4 November 2026: Q30-Q33)

| # | Question | Decision |
|---|---|---|
| Q30 | How is an exercise chosen? | Typed in a box that suggests as you type: the 30 starter exercises and every exercise you've used before. A name not on the list becomes your own exercise ("custom"), so there's no separate page to set one up. Names are matched ignoring capitals ("back squat" is Back squat). |
| Q31 | How many sets can be typed at once? | Six empty rows to start; "Add more rows" gives six more (no script needed); rows left blank are ignored. Up to 60 sets a session. |
| Q32 | What's needed in a row? | An exercise and reps (1 to 1,000). Weight is optional (bodyweight exercises), 0 to 1,000 kg or 2,200 lb, in the unit from Preferences (kg by default: Q25). Rest after the set is optional, in seconds, up to an hour. |
| Q33 | How is weight stored? | In whole **grams** (100 kg is 100,000 g; 225 lb is 102,058 g), as distances are in whole metres: an exact number, so a weight saved unchanged comes back the same, and totals add up exactly. Grams are metric, as CLAUDE.md rule 6 asks; the rule now says so. |

## Data

- **`Exercise`** (new, per user, `IOwnedByUser`): name (up to 60 characters), unique per user ignoring capitals.
  Created the first time a user saves a set with that name, starter or not; the starter list itself is a list in
  code (`Domain/Workouts/StarterExercises`), not stored, so it can grow without a migration.
- **`SessionSet`** (new, per user): its session log, its position, its exercise, reps, weight in grams (optional),
  rest in seconds (optional). Deleted with its log (a session marked skipped or back to planned has no sets, as a
  run has no details: Q28).
- **`SessionLog.Notes`**: up to 2,000 characters, for every kind of session; kept when skipped (a note like "knee
  sore, skipped" is worth keeping), removed with the log when put back to planned.
- One migration, `AddStrengthLogs`.
- **Two tabs** (§9): saving sets replaces the session's sets, under the log's version, so an out-of-date tab is
  refused as for marks and runs. The log's version changes whenever its sets do.
- **T19**: exercises and sets are the user's own (the per-user filter); another user's session is "not found", and
  the suggestions never include another user's exercises.

## Rules (domain, test first)

- `Weights`: grams from an amount in kg or lb (a pound is 453.59237 g), the amount back, the text shown ("100 kg",
  "102.5 kg", "225 lb"), and the text a box is filled with that saves back to the same grams (as `Distances` does).
- `StarterExercises`: the 30 names (below), in a fixed order.
- `Workout` summary: sets grouped by exercise in the order first used, each set's reps and weight; the total sets;
  and the weight lifted (reps × weight, summed, bodyweight sets counting nothing).

Starter list (Q4): back squat, front squat, deadlift, Romanian deadlift, bench press, incline bench press, overhead
press, barbell row, pull-up, chin-up, lat pulldown, seated cable row, dumbbell bench press, dumbbell shoulder press,
dumbbell row, lunge, Bulgarian split squat, leg press, leg curl, leg extension, calf raise, hip thrust, bicep curl,
tricep pushdown, dip, push-up, plank, hanging leg raise, kettlebell swing, face pull.

## The page

As the run's page, so the two look like one app:
1. Title, when, status.
2. **Saved workout**: a card with tiles (Exercises, Sets, Weight lifted), then each exercise as a short list of its
   sets; rest shown where given ("rest 90 s"). Notes under it.
3. **The form** (once started), folded under "Change the details" once saved: the set rows as a compact table (on a
   phone each row wraps into two lines rather than scrolling sideways), "Add more rows", notes, "Save as done".
4. The buttons, with Q28's note when there's something to lose.

Notes are added to the run's page and the other-exercise page the same way.

## Edge cases to test

- A row with reps but no exercise, or an exercise but no reps; reps 0 or 1,001; weight -1, "abc", "100,5", over the
  limit; rest over an hour; 61 rows posted by hand.
- Blank rows between filled ones are ignored and the order kept.
- "back squat" and "Back Squat" are one exercise; a new name becomes the user's own; another user's exercises are
  never suggested or used (T19).
- Weight in lb saved unchanged keeps the same grams; kg by default.
- Skipping keeps notes and removes sets; back to planned removes both.
- Two tabs: an out-of-date tab can't replace the sets.
- Worst cases on screen: a 60-character exercise name, 60 sets, 1,000 kg, a long note, Arabic names, at 320 px and
  1280 px, light and dark.

## Commits, in order

1. This plan.
2. `Weights`, test first.
3. `StarterExercises` and the workout summary, test first.
4. `Exercise`, `SessionSet`, `SessionLog.Notes` and the migration.
5. Notes on every session page, test first.
6. The strength form, its summary and "Add more rows", test first.
7. The browser check and its fixes, then the docs (requirements Q30-Q33, roadmap, known limitations, design).
