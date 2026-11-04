# Ideas not yet agreed

On 20 October 2026 the owner shared a plan for a "personal life dashboard": home, routine, food, training and life
sections in one app. Most of it is already in the [approved requirements](requirements.md). This page lists only what
isn't, where each idea would fit, and a recommendation.

**Nothing here is agreed scope.** When the owner says yes to an idea, it moves into requirements.md with an ID and
leaves this page.

## Already planned (not repeated below)

| The plan's idea | Already in |
|---|---|
| Fixed commitments entered once; repeating and every-other-week events | CAL-2, CAL-3 (built) |
| Travel time to and from the gym counted automatically | §7.1 footprints, TRV-2 (built) |
| Clash warnings | H1, CAL-6 (built on save and on the home page) |
| Fixed, flexible or optional | Fixed / movable and priority (built): the planner fits high priority first and never moves fixed |
| Drag and drop | CAL-8 (Phase 5) |
| Sleep protection: warn before a late event cuts sleep below the minimum | ACC-4, SLP-3, SLP-4, H3 (Phase 8) |
| Rescheduling a missed workout without cramming | SCH-6, H5-H7, T16 (Phase 6) |
| Three gym sessions and three runs a week, with progress | GOL-1, FIT-5 |
| Run types (easy, long, tempo, intervals) and how a run felt | FIT-3 (perceived effort) |
| Gym and running logs | FIT-2, FIT-3 (Phase 7) |
| Brighton weekends: a different base, the home gym out of reach, honest alternatives | TRV-4, CAL-9, T15, SCH-5 |
| A/B weeks | CAL-9 |
| Free-time view | CAL-7 |
| Energy and soreness ratings, recovery trends (without claiming to measure recovery) | SLP-2, SLP-6, §9 health-data note |
| Meal plans, batch cooking, recipes, portions, shopping lists, workday lunches, leftovers, macros | Section G (after the MVP) |
| Planning assistant ("where can I fit my third gym session?") | Section J (after the MVP, behind consent and the validator) |
| Export, deletion and privacy | ACC-8, ACC-9, P6 |

## New ideas worth adding

Size: S = a day or less, M = a few days, L = a phase of its own.

### Home page and look

The owner also asked for the app to look good, so ideas 1-5 were built on 20 October 2026 and have left this page:
a greeting and a **Next up** card (GOL-2's "next activity"), one colour per category (CAL-2), dark mode (dark by
default, with a choice in Preferences, ACC-3), a calmer look, and home-page activities that open their edit page. See [design](design.md) and the
[roadmap](roadmap.md).

| # | Idea | Why it's good | Fits | Size | Recommendation |
|---|---|---|---|---|---|
| 6 | Navigation grouped by what you do daily (Home, Calendar, Training, Sleep, later Food), with setup pages (locations, travel times, default times) under Settings | The menu grows with every phase; setup pages are rarely needed once done | Layout | S | Phase 5, when Calendar joins the menu |
| 7 | Progress that encourages rather than punishes: rings or bars for weekly targets; "weeks on target" rather than streaks that break | Fits P4 ("more training isn't automatically better"). GOL-3 lists streaks. | GOL-1, GOL-3 | S | Use for weekly targets (Phases 6-9); reword GOL-3's streaks when it is built |
| 8 | Choose which cards the home page shows | Personal, but only useful once there are several cards | GOL-2 | M | After the MVP |

### Training and sleep

| # | Idea | Why it's good | Fits | Size | Recommendation |
|---|---|---|---|---|---|
| 9 | A preferred day per kind of session ("long run on Sunday") | ACC-7 only has preferred days for exercise in general | GOL-1, §7.3 scoring | S | Add to Phase 6 |
| 10 | A run type per cycle week for a target (tempo in week A, intervals in week B) | The plan's "alternate tempo and interval weeks". A/B weeks exist for activities (CAL-9), not for targets. | GOL-1, CAL-9 | S | Add to Phase 6 |
| 11 | **Latest bedtime**: "In bed by 22:45 for 7 h 30 min before tomorrow's 06:15 start" | A friendlier way to show tonight's sleep opportunity (SLP-3), worked out from tomorrow's first footprint | GOL-2, SLP-3 | S | Add to Phase 8 |
| 12 | A quick daily energy and mood check-in, separate from the sleep log | One tap; SLP-2 only asks about energy with the sleep log | SLP-2, SLP-6 | S | After the MVP. Mood is health data (§9). |
| 13 | A weekly review with one or two suggested changes ("Tuesday's run was skipped three weeks running: try Wednesday?") | The numbers (SLP-5, FIT-5) become a next step | GOL-2, GOL-3 | M | Phase 9 or after. Fixed rules, not AI (P2); suggestions go through the validator (SCH-1). |

### Meal prep (section G, after the MVP)

To add to section G's list when it is planned:

| # | Idea | Size |
|---|---|---|
| 14 | A **"What needs preparing?"** screen for prep day: portions to cook, what to buy, what to pack for each workday | M |
| 15 | A prep-day checklist in cooking order, with storage notes | S |
| 16 | A shopping list that adds up quantities across recipes, takes off what's already in the fridge, and can be ticked off | M |
| 17 | Portion scaling, and swapping an ingredient recalculates the macros (always labelled as an estimate) | M |
| 18 | Work-lunch tags: can be eaten cold, travels well | S |
| 19 | A flexible "family dinner" allowance on chosen days | S |
| 20 | Optional cost per serving and weekly food spend | S |

### Life beyond fitness (after the MVP)

| # | Idea | Why it's good | Size | Recommendation |
|---|---|---|---|---|
| 21 | Occasions (birthdays, anniversaries): all-day markers that take no time, so they never clash | Today every activity takes time; these are a new kind of entry | M | After the MVP |
| 22 | Personal goals beyond fitness (books, places, skills) | Close to GOL-3's custom habits | S | Fold into GOL-3 |
| 23 | A notes inbox ("brain dump") to sort out later | Quick capture | S | After the MVP |
| 24 | Download the calendar as an `.ics` file, to view it in another calendar app | One way, a file download, no outside service. Two-way sync stays out of scope (§11). | S | After the MVP |

## Not recommended

| The plan's idea | Why not |
|---|---|
| Building meal planning before the calendar (the plan's first phase) | The scheduling core is what makes this app different, and meal prep has to take calendar time anyway (section G). The roadmap keeps meal prep after the MVP. |
| A money and savings tracker | Off the app's purpose, and financial data needs more protection. Idea 20 covers the food-cost part. |
| A recovery "score" | Only prompts from the user's own logs, never presented as a measurement. The plan says the same, and §9 flags health data. |
