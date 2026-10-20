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
| Activities | The list describes a repeating activity by its usual time; on the night the clocks go forward, an occurrence at 01:30 really happens at 02:30 (a one-off says so). | Phase 5, when the calendar shows each occurrence |
| Activities | The check on save looks for clashes 8 weeks ahead, from today or the activity's first date if later (opening hours and the 24-hour limit: a year). A clash further ahead (two activities every 9 weeks that first meet in week 9) isn't found then. | The calendar (Phase 5) shows clashes on any week shown |
| Activities | The check on save covers overlaps (H1), opening hours (H4), travel and the 24-hour limit (H8). Sleep (H3) and the recovery rules (H5-H7) aren't checked yet. | Phases 6 and 8 |
| Activities | "Save anyway" isn't stored: nothing records which clashes were accepted. Clashes can also exist without anyone confirming them (beyond the 8 weeks checked, after travel times, opening hours or home change, or with an activity that could only be checked in part), so the calendar must not show every clash as accepted. | Phase 5, with the calendar: store accepted clashes |
| Activities | Changing the timezone in Preferences moves activities to the new timezone without running the check on save, although CLAUDE.md rule 3 says every save path goes through it. Clock times are kept, so clashes and opening hours only change on the new timezone's clock-change nights. | Owner's choice: re-check after the move and show the results, or accept this |
| Activities | Changing a location's opening hours, a travel time or which location is home doesn't re-check the activities already saved. The other activities a new one is checked against are taken as they are: one whose travel can no longer be worked out is checked without travel, one now over 24 hours or unreadable is left out, and each is named in a note shown after saving (or with the clashes). | Phase 5: the calendar marks activities that no longer pass |
| Activities | Preparation and changing times are entered for each activity; there are no default buffers per category yet (ACC-6). | Owner to place: end of Phase 4 or Phase 6 |
| Activities | The activities list and the home page always show 24-hour times, even with the 12-hour clock chosen in Preferences. | Phase 5, with the calendar |
| Home page | An activity stored in a timezone other than the one in Preferences (only possible by editing the database by hand, or by adding an activity in one tab at the moment the timezone is changed in another) is listed by the date and clock time in its own timezone, without naming it. | When activities can have their own timezone (travel, after the MVP) |
| Home page | Only the first part of the dashboard (GOL-2): what to set up, and today and the next 7 days with when to leave and any clashes. Free time left today, tonight's sleep and weekly targets come with the phases that build them. | Phases 6, 8 and 9 |
| Travel | Deleting a travel time doesn't check whether another tab changed it since the delete page opened (deleting a location does). Nothing is overwritten, but the page may have shown the old time. | When convenient |
| Travel | Every activity away from home gets its own round trip from home, so two activities back to back at the same place (the office gym straight after work, a work day split at lunch) each get a commute and show as clashing. That is location chaining (SCH-8). | After the MVP: the owner's decision (requirements Q10) |
| Series | Repeating activities are saved (their rule as RRULE text, ADR 0001) and listed, but their occurrences aren't shown on a calendar yet, and one occurrence can't yet be cancelled or moved on its own (CAL-4: the domain supports it, the screens don't). | Phase 5 |
| Series | Moving an occurrence into the second 01:00-01:59 on the night the clocks go back lands it on the first one (requirements §8 uses the earlier time). | Phase 5 calendar: warn, or store the offset |
| Opening hours | When an activity runs through two openings that touch (a place open midnight to midnight on consecutive days) and then past a closing time, the explanation names the opening it started in rather than the one that closed. Only very long activities are affected. | Not fixed by the check on save (it uses the same naming); with the planner's explanations (Phase 6) |
| Time | H8's 24-hour limit on a footprint is elapsed time, so an activity from midnight to midnight on the day the clocks go back (which lasts 25 hours) is refused, while the same clock times on the day they go forward (23 hours) are accepted. | Owner's choice: keep, or allow "the whole day" |
| Time | A span that would end before it starts once the clocks change (01:45 to 02:00 on the night they go forward) is refused rather than shifted. Chosen as the cautious option; can be revisited. | Owner's choice |
| Series | Working out occurrences walks from each series' first date, so long-running series cost more each query. | Measured against the 1-second target in Phase 9 |
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
| The double-submit guard in `wwwroot/js/site.js` (a second click on a form's button is ignored; the button works again after 10 seconds without a new page) has no automated test, because the test suite doesn't run a browser. | Phase 9, with the other browser checks |

## Harmless, by choice

- DevTools reports missing source maps for Bootstrap: they were removed to keep the repository small.
- Local runs over http log a warning that the HTTPS port for redirects can't be found.
