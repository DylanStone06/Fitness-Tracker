# Plan: a moved session survives its week being given to another routine (Q17)

Written 4 November 2026, before any code, after the owner's decision on requirements Q17 ("what you recommend").

**Status: built the same day**, as planned, then reviewed by the `scheduling-reviewer` agent: no high findings; each
of the others fixed in its own commit (the refusal moved into the one check on save; an edit treating a moved session
the same with or without a holiday; Find another time offering only days a kept session happens on; a note that
cancelling one can't be undone for now; more tests), except an older gap in the re-check after removing a routine's
dates, now in the known limitations. One domain test from the Phase 6b scheduling review pinned the old rule (a hidden move
mustn't count as happening); it now uses a move whose both ends are left out, which still mustn't count, and a new
test checks that a kept move is checked where it is.

## The problem

A session of a repeating activity is known by the date its repeat puts it on (CAL-4). An activity in a routine
happens only on dates that routine is in use (CAL-9), and today a moved session is shown only if the date it was moved
**from** is one of those. So: Monday 2 November's gym moved to Friday 30 October, then 2-8 November chosen for a
holiday, and Friday's session silently disappears, though you moved it out of that week, very likely because you'd be
away.

## The rule

**A moved session happens if either end of the move is a date its activity is in use on**: the date it was moved
from, or the date it was moved to. So:

| Moved | Then | Happens? |
|---|---|---|
| Mon 2 Nov to Fri 30 Oct | 2-8 Nov becomes a holiday | **Yes**, on Friday (the change) |
| Mon 2 Nov 07:00 to Mon 2 Nov 19:00 | 2-8 Nov becomes a holiday | No: both ends are in the holiday, so it goes with the week, as an unmoved one does |
| Wed 7 Oct (week A) to Tue 13 Oct (week B) | (a week-A-only activity) | Yes, as now: moved by you into a week it isn't usually in |
| A cancelled session | anything | No, as now |

Why not "every move always happens"? A session moved within the week later given to a holiday would then show on the
holiday's Monday evening; going with the week is what choosing a holiday means.

## Where it changes (domain first, test first)

- `Series`: one private rule, "in use on this date, or moved to a date in use", used by `OccurrencesBetween` (moved
  occurrences) and `FallsOn` (so a kept move has a page, can be moved again, cancelled or put back, and can be marked
  done or skipped). `Edit` keeps working by the date the repeat put it on: changing the activity itself to "week A
  only" still removes its week B changes and says so (P5). `SeriesEdit.HiddenChanges` counts only the changes that
  can't be seen (a kept move is seen).
- Nothing else in the domain asks about this directly: the check on save, the agenda, the planner and the session
  pages go through `FallsOn`, `OccurrencesBetween` and `ActivityCheck.OccurrenceOn`, so they all follow.
- The occurrence page (web):
  - "Put back" on a kept move whose own date isn't in use would make it disappear: the page says so beside the button
    first.
  - Moving a kept move to another date that isn't in use would make it disappear too: refused, with a message, so it
    never vanishes without a word (P1).

## Tests

- Domain (`SeriesOnlyOnTests`): the Q17 example happens on the Friday (the old test that pinned the opposite is
  changed); a move within a left-out week is left out; a kept move can be changed again; an edit counts a kept move as
  kept and not hidden; the rule on the night the clocks go forward (28 March 2027).
- Web: the calendar and home page show the Friday session after the holiday is chosen; its occurrence page opens and
  says what putting it back does; moving it into the holiday is refused.

## Then

The `scheduling-reviewer` agent reviews the change (recurrence and routine code); its findings get their own
commits. The `scheduling-rules` skill, the known limitations (the Q17 row goes) and requirements Q17 are updated.
