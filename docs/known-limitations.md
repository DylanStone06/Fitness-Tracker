# Known limitations

What the app doesn't do yet, or does imperfectly, and when that is planned to change. Requirement IDs refer to
[requirements.md](requirements.md); phases to the [roadmap](roadmap.md). Remove a row when it is fixed.

## Product

| Area | Limitation | Planned |
|---|---|---|
| Accounts | The app sends no email, so there is no email confirmation and no emailed password reset (ACC-1). The register page says so. | After the MVP (scope A) |
| Accounts | The register page ("an account with this email address already exists") and the lockout message can reveal that an address has an account. Sign-in itself gives one message for both. | Reassess before any public hosting (Phase 9) |
| Activities | Each activity is stored with a timezone, but there is no way yet to choose one: it is always the Preferences timezone. Changing that moves every activity in the old timezone to the new one, keeping its clock time (§8, T6; the page says so before saving). That includes one-offs and past activities, so a meeting booked abroad, or a planned session already over, moves too. | Choosing a timezone per activity: after the MVP (travel, scope F). Past activities: before workout logging (Phase 7) |
| Activities | A night shift entered as 22:00 for 8 hours ends at 05:00 or 07:00 by the clock on the two nights a year the clocks change, because lengths are elapsed time (§8). If the shift really ends at 06:00 by the clock, the app will think the user is free, or still at work, for that hour. The form says so. | Kept: the owner's decision (requirements Q6) |
| Activities | The list describes a repeating activity by its usual time; on the night the clocks go forward, an occurrence at 01:30 really happens at 02:30 (a one-off says so). The calendar shows each occurrence at its real time. | Kept: the calendar is where each occurrence is shown |
| Calendar | Dragging opens the page to confirm the move rather than saving straight away (CAL-8, SCH-1). Dragging a repeating activity there moves that one occurrence; moving every occurrence is done in the ordinary form, which doesn't take the dragged time. The drag itself has no automated test (the suite doesn't run a browser); it was checked by hand once. | Phase 9 browser checks |
| Calendar | FullCalendar's week and day grids are laid out with tables that a screen reader reads cell by cell, mostly empty. The list view (the default on phones) reads more easily, and every activity is also on the Activities page. | Phase 9 accessibility pass, with a real screen reader |
| Calendar | With more than two activities overlapping, the rest are behind "+N more", which opens FullCalendar's box listing them. Its close control is a styled "×" (not a real button: only a mouse can use it), and Escape closes the box; whether the keyboard's focus moves into the box when it opens wasn't checked. The calendar's look with worst-case data (break-ui: long and unbreakable names, many overlaps, 140 activities a week, the night the clocks go back) was checked by hand in the browser at 320, 800 and 1440 px; its script and styles have no automated test. | Phase 9 accessibility pass, with a real screen reader, and browser checks |
| Calendar | On the night the clocks go back, the repeated hour (01:00-01:59) is drawn once, so an activity in the second one sits on top of one in the first; the event says which ("01:30 BST"), in the grid and the list. | Kept for now (not changed in Phase 5); revisit in Phase 9 |
| Activities | The check on save looks for clashes 8 weeks ahead, from today or the activity's first date if later (opening hours and the length limit, H8: a year). A clash further ahead (two activities every 9 weeks that first meet in week 9) isn't found then. | The calendar (Phase 5) shows clashes on any week shown |
| Activities | The check on save covers overlaps (H1), opening hours (H4), travel, the length limit (H8) and the rest rules for demanding sessions (H5-H7, Phase 6). Sleep (H3) isn't checked yet: it needs the sleep opportunity (SLP-3). The planner already keeps every planned sleep window free. | Phase 8 |
| Activities | "Save anyway" isn't stored: nothing records which clashes were accepted, so every later save of the activity asks about them again, and the calendar and home page show each clash the same way ("⚠ Clashes with …"), whether it was accepted or has appeared since (beyond the 8 weeks checked, after travel times, opening hours or home change, or with an activity that could only be checked in part). Storing them needs each clash to know which two occurrences overlap, which the planner's accepted conflicts (Q2) need too. | Phase 6, with the planner's accepted conflicts (moved from Phase 5 on 21 October 2026) |
| Activities | Changing a location's opening hours, a travel time or which location is home doesn't re-check the activities already saved. The other activities a new one is checked against are taken as they are: one whose travel can no longer be worked out is checked without travel, one now longer than the day allows or unreadable is left out, and each is named in a note shown after saving (or with the clashes). | Phase 6, with the planner, which checks the week it plans (planned for Phase 5, not built: the calendar shows clashes but not other activities that no longer pass) |
| Home page | An activity stored in a timezone other than the one in Preferences (only possible by editing the database by hand, or by adding an activity in one tab at the moment the timezone is changed in another) is listed by the date and clock time in its own timezone, without naming it. One whose date in its own timezone is yesterday is left out, of Next up too, even if it starts later today where the user is. | When activities can have their own timezone (travel, after the MVP) |
| Planning | "Not this day" on a suggestion isn't stored: it lasts while the page's address carries it (going back to Plan my week from the menu forgets it). Plan my week only finds times for goals' missing sessions; it doesn't move movable activities already planned (§7.4 doesn't either). | Owner's choice: store refusals if they come back too often |
| Planning | A session added "every week" is checked on save for 8 weeks like any activity, so a clash in a later week (with a one-off then) is shown on the activity form, after the plan, to change or "Save anyway". | Phase 6: plan more than one week (SCH-8 is after the MVP) |
| Goals | A weekly goal is a number of sessions of a set length ("3 times a week, 50 min each"). Goals for a running distance ("20 km a week") or total minutes of a category ("150 minutes of exercise") (GOL-1) aren't offered: the planner would have to choose session lengths, and distance needs the run log. | Distance with workouts (Phase 7); total minutes when the owner wants it |
| Home page | While an activity is under way, Next up shows it, even if another starts during it. That only happens with a clash kept by "Save anyway" (a dentist visit in the middle of a conference day); the clash is flagged on both. | Owner's decision: keep, or show what starts next while something is under way |
| Home page | Only the first part of the dashboard (GOL-2): a greeting, what to set up, the next activity, and today and the next 7 days with when to leave and any clashes. Free time left today, tonight's sleep and weekly targets come with the phases that build them. | Phases 6, 8 and 9 |
| Home page | "Next up" only looks at activities starting today or later, so during a night shift that started yesterday it shows the next thing after the shift. | Phase 9, with the dashboard (planned for Phase 5, not built) |
| Look | With "Match my device" chosen, switching the device's theme while a page is open was checked only by reloading (the browser used for checks doesn't send the change live). | Phase 9 browser checks |
| Travel | Deleting a travel time doesn't check whether another tab changed it since the delete page opened (deleting a location does). Nothing is overwritten, but the page may have shown the old time. | When convenient |
| Travel | Every activity away from home gets its own round trip from home, so two activities back to back at the same place (the office gym straight after work, a work day split at lunch) each get a commute and show as clashing. That is location chaining (SCH-8). | After the MVP: the owner's decision (requirements Q10) |
| Series | Moving an occurrence into the second 01:00-01:59 on the night the clocks go back lands it on the first one (requirements §8 uses the earlier time). | Phase 9: warn, or store the offset (planned for Phase 5, not built) |
| Opening hours | When an activity runs through two openings that touch (a place open midnight to midnight on consecutive days) and then past a closing time, the explanation names the opening it started in rather than the one that closed. Only very long activities are affected. | Not fixed by the check on save (it uses the same naming); with the planner's explanations (Phase 6) |
| Time | A span that would end before it starts once the clocks change (01:45 to 02:00 on the night they go forward) is refused rather than shifted. Chosen as the cautious option; can be revisited. | Owner's choice |
| Series | Working out occurrences walks from each series' first date, so long-running series cost more each query. | Measured against the 1-second target in Phase 9 |
| Series | A repeating activity with any occurrence changed on its own (CAL-4) is loaded for every calendar week, home page and check on save, whatever the dates, because a moved one can land anywhere. Activities that ended long ago are loaded too, so the cost grows with history. Storing the earliest and latest dates the changes land on would let the query skip them. | Measured against the 1-second target in Phase 9 (found by the Phase 5 code review) |
| T6 | Only the series half is tested; "logged sleep unchanged" needs the sleep log. | Phase 8 |

## Hosting and security

| Limitation | Planned |
|---|---|
| Not hosted anywhere. Where, and how a hosted database is migrated, are decided with hosting. | Phase 9 |
| The local HTTPS development certificate isn't trusted on this PC, so `ft-dev run` uses `http://localhost:5137`. Trusting it changes the certificate store, so that is left to the owner. | Owner's choice |
| Host filtering (`AllowedHosts`) isn't configured for a real domain. | Phase 9 |
| The app doesn't fix its culture. Number boxes send decimals with a dot ("7.5 hours"), which a server set to a decimal-comma culture (German, French) wouldn't read; this PC and the tests use en-GB, so nothing catches it. Fix with `UseRequestLocalization` set to en-GB. | Phase 9 |
| EF Core's design-time files reach the build output; they are to be kept out of published builds. | Phase 9 |
| The accounts commit (`11e433f`) deferred 4 review findings "to hosting (Phase 9) or documented" without listing them individually. `/security-review` couldn't run then (it needs git on PATH) and was done by hand. | Re-run a full security review in Phase 9 |

## Not yet checked

| Check | Planned |
|---|---|
| A real screen reader (pages were checked against WCAG 2.2 AA by reading their markup and in the built-in browser). | Phase 9 accessibility pass |
| Visual Studio's Test Explorer (tests are run with `dev\ft-dev.ps1`). | Owner's choice |
| Copying Monday's opening hours to other days on the location form (`wwwroot/js/site.js`) has no automated test, for the same reason; it was checked by hand in the built-in browser. | Phase 9, with the other browser checks |
| The double-submit guard in `wwwroot/js/site.js` (a second click on a form's button is ignored; the button works again after 10 seconds without a new page) has no automated test, because the test suite doesn't run a browser. | Phase 9, with the other browser checks |

## Harmless, by choice

- DevTools reports missing source maps for Bootstrap: they were removed to keep the repository small.
- Local runs over http log a warning that the HTTPS port for redirects can't be found.
