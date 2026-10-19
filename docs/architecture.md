# Architecture notes

How the pieces fit together, and the rules that keep each user's data private. The stack and the reasons for it
are in [ADR 0001](decisions/0001-stack.md); what's still missing is in [known limitations](known-limitations.md).

## Projects

| Project | Holds | Rule |
|---|---|---|
| `FitnessTracker.Domain` | `Time` (clock changes, crossing midnight, weeks), `Recurrence` (series, rules, one-off changes), `Templates` (A/B week cycles); later footprints, scheduling, sleep and workouts. The rules as built are in the `scheduling-rules` skill. | References nothing from ASP.NET Core or EF Core. `ArchitectureTests` allows only the base library plus NodaTime. |
| `FitnessTracker.Web` | MVC controllers and views, `Data/` (database, migrations, Identity), `Services/` | No scheduling rules in controllers or views. |
| `FitnessTracker.*.Tests` | xUnit v2 tests | Integration tests run the real app in memory (`TestAppFactory`). |

## Keeping each user's data private (ACC-2, T19)

Isolation is enforced in the data layer, so a controller that forgets to filter still can't reach anyone else's data.

| Layer | What it does | Where | Tested by |
|---|---|---|---|
| Reads | A query filter named `Owner` returns only the signed-in user's rows of every `IOwnedByUser` type. Nobody signed in means no rows. | `AppDbContext.OnModelCreating` | `Data/OwnershipTests` |
| Saves | New rows are stamped with the signed-in user, whatever owner was set. Changing or deleting anyone else's row, or giving a row away, is refused. | `AppDbContext.CheckOwnership` | `Data/OwnershipTests` |
| Database | `OwnerId` is a concurrency token, so an `UPDATE` or `DELETE` only applies where the stored owner matches, even if a bug built the entity with the wrong owner. | `AppDbContext.OnModelCreating` | `Data/OwnershipTests` |
| HTTP | A record that isn't found (including someone else's) is answered with `NotFound()`: the same 404 page as an id that never existed. | Each controller, e.g. `LocationsController` | `Security/AccessControlTests` |

### Rules for new code

1. **Every table of personal data implements `IOwnedByUser`, child rows included.** Give opening hours or travel legs
   their own `OwnerId`; don't rely on their parent being filtered, because a child table can be queried directly. A
   test fails for any table that isn't owned, isn't Identity's, and isn't listed in
   `OwnershipTests.SharedNonPersonalTables` (for genuinely shared data, such as a starter list of exercises).
2. **Turn "not found" into `NotFound()`, never `Forbid()`.** "Forbidden" would confirm the record exists.
3. **No `IgnoreQueryFilters()` in request code.** `Find`/`FindAsync` return an entity the context is already tracking
   without checking the filter again, so never load another user's row into a request's context.
   `Database.ExecuteSql` bypasses every layer above; don't use it for personal data.
4. **Record ids are a route parameter named `id`.** Add every action that takes one to
   `AccessControlTests.ActionsThatTakeAnId`; a test fails when one is missing. For a new controller, also teach
   `AccessControlTests.AlexAddsARecordForAsync` to create Alex's record of that kind, so the test sends a real id of
   the right type. An endpoint that returns a user's data without an id (preferences; the calendar feed in Phase 5)
   needs its own T19 test.
5. **A form that refers to another record by id checks it is the user's own** (a travel leg's locations): the filter
   makes another user's id "not found", which the form reports as "choose from the list".

### Notes for later phases

- **Optimistic concurrency** (requirements §9, built for locations). Records that implement `IVersioned` get a new
  `Version` on every save, checked like `OwnerId`, so an out-of-date tab can't overwrite a newer save. An ownership
  mismatch and a genuine "changed in another tab" conflict both raise `DbUpdateConcurrencyException`. When handling
  it, re-read the row through the filter first: if it's gone, answer "not found"; only otherwise say it was changed
  elsewhere (see `LocationsController`).
- **Phase 9, deleting an account** (ACC-9). The database deletes a user's rows with the account (cascade, tested by
  `OwnershipTests.DeletingAnAccount_DeletesItsLocations_AndNobodyElses`). Deleting
  owned rows through EF needs that user to be signed in, because `CheckOwnership` refuses otherwise, so delete before
  signing out, or let the cascade do it without loading the rows.

## Who is signed in

`ICurrentUser` answers "who is signed in?". In the app, `HttpCurrentUser` reads Identity's user id from the request.
Tests that build a database context directly use `FixedCurrentUser`.

## Pages and forms

- Every page needs a signed-in user unless it is marked `[AllowAnonymous]` (a fallback policy in `Program.cs`).
- Every form post must carry its anti-forgery token (a global filter). Signing out is a POST for the same reason.
- Validation happens on the server only. Errors follow the GOV.UK pattern: "Error:" at the start of the page title,
  `_ErrorSummary` at the top of the form, and each message next to its field, linked with `aria-describedby` and
  `aria-invalid` (`ModelStateExtensions.AriaInvalid`).
- A 404 or 400 with no page of its own shows `Home/Status`, keeping its status code.

## Databases

| Where | Database | Notes |
|---|---|---|
| `dev\ft-dev.ps1 run` | `%LOCALAPPDATA%\FitnessTracker\fitness-dev.db` | Connection string in user-secrets, never in the repository. Migrated on start; a copy is saved first when existing data has migrations to apply. |
| Integration tests | A private in-memory SQLite database per test class | `TestAppFactory` uses the "Testing" environment, so user-secrets and the real database are never loaded. |
| Database context tests | `Data Source=:memory:` | Built from the migrations. |
| Checking pages by hand | A throwaway file | `dotnet run --project src\FitnessTracker.Web --launch-profile http -- "--ConnectionStrings:Default=Data Source=<path>"`: command-line settings override user-secrets. |

## Working on this PC

- Files Claude creates can have LF line endings, which fail `ft-dev check`'s formatting step. Run
  `dev\ft-dev.ps1 format` first.
- A running app (`ft-dev run`) locks its build output, so `build`, `test` and `check` fail until it is stopped.
