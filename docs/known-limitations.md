# Known limitations

What the app doesn't do yet, or does imperfectly, and when that is planned to change. Requirement IDs refer to
[requirements.md](requirements.md); phases to the [roadmap](roadmap.md). Remove a row when it is fixed.

## Product

| Area | Limitation | Planned |
|---|---|---|
| Accounts | The app sends no email, so there is no email confirmation and no emailed password reset (ACC-1). The register page says so. | After the MVP (scope A) |
| Accounts | The register page ("an account with this email address already exists") and the lockout message can reveal that an address has an account. Sign-in itself gives one message for both. | Reassess before any public hosting (Phase 9) |
| Locations | Name only: no opening hours, travel legs or base location yet (TRV-1, TRV-2). | Phase 4 |
| Locations | Two tabs editing the same location can overwrite each other: no optimistic concurrency yet (requirements §9). | Phase 4 |
| Locations | Duplicate names are allowed. | Decide in Phase 4 (location pickers) |
| Locations | The browser's 100-character limit cuts a pasted longer name short without a message (ASP.NET adds `maxlength` from the validation rule). Typed or posted names over 100 characters get a proper error. | Phase 4 form rework |
| Locations | A double-clicked "Delete location" ends on the "Page not found" page. | Phase 4 |
| Accessibility | Error summary items are plain text, not links to their fields as in the GOV.UK pattern. Each field's own message is linked for screen readers. | Phase 9 accessibility pass |

## Hosting and security

| Limitation | Planned |
|---|---|
| Not hosted anywhere. Where, and how a hosted database is migrated, are decided with hosting. | Phase 9 |
| The local HTTPS development certificate isn't trusted on this PC, so `ft-dev run` uses `http://localhost:5137`. Trusting it changes the certificate store, so that is left to the owner. | Owner's choice |
| Host filtering (`AllowedHosts`) isn't configured for a real domain. | Phase 9 |
| EF Core's design-time files reach the build output; they are to be kept out of published builds. | Phase 9 |
| The accounts commit (`11e433f`) deferred 4 review findings "to hosting (Phase 9) or documented" without listing them individually. `/security-review` couldn't run then (it needs git on PATH) and was done by hand. | Re-run a full security review in Phase 9 |

## Not yet checked

| Check | Planned |
|---|---|
| A real screen reader (pages were checked against WCAG 2.2 AA by reading their markup and in the built-in browser). | Phase 9 accessibility pass |
| Visual Studio's Test Explorer (tests are run with `dev\ft-dev.ps1`). | Owner's choice |
| Account deletion cascading to a user's data (ACC-9). | Phase 9 |

## Harmless, by choice

- DevTools reports missing source maps for Bootstrap: they were removed to keep the repository small.
- Local runs over http log a warning that the HTTPS port for redirects can't be found.
