# Known limitations

What the app doesn't do yet, or does imperfectly, and when that is planned to change. Requirement IDs refer to
[requirements.md](requirements.md); phases to the [roadmap](roadmap.md). Remove a row when it is fixed.

## Product

| Area | Limitation | Planned |
|---|---|---|
| Accounts | The app sends no email, so there is no email confirmation and no emailed password reset (ACC-1). The register page says so. | After the MVP (scope A) |
| Accounts | The register page ("an account with this email address already exists") and the lockout message can reveal that an address has an account. Sign-in itself gives one message for both. | Reassess before any public hosting (Phase 9) |
| Preferences | Changing the timezone only shows a hint that repeating activities keep their clock times; requirements §8 says the user is told before it happens. There are no stored activities yet to move. | Phase 4, with activities |
| Travel | Deleting a travel time doesn't check whether another tab changed it since the delete page opened (deleting a location does). Nothing is overwritten, but the page may have shown the old time. | When convenient |
| Travel | Every activity away from home gets its own round trip from home, so two activities back to back at the same place (the office gym straight after work, a work day split at lunch) each get a commute and show as clashing. That is location chaining (SCH-8). | After the MVP, unless the owner brings "staying at the same place" forward |
| Series | The time and recurrence rules exist only in the domain: series can't be saved or shown yet. Rules can be written as and read from RRULE text (ADR 0001, `RecurrenceRuleText`), but nothing stores that text until activities do. | Phases 4-5 |
| Series | Moving an occurrence into the second 01:00-01:59 on the night the clocks go back lands it on the first one (requirements §8 uses the earlier time). | Phase 5 calendar: warn, or store the offset |
| Opening hours | When an activity runs through two openings that touch (a place open midnight to midnight on consecutive days) and then past a closing time, the explanation names the opening it started in rather than the one that closed. Only very long activities are affected. | When explanations are written (Phase 4, validator) |
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
