# Design

How the app looks, so every page (and the Phase 5 calendar) uses the same colours and parts. The idea behind it came
from the owner's life-dashboard plan ([ideas](ideas.md)): calm and organised, colour used sparingly so the important
things stand out, and progress that encourages rather than punishes.

## Rules

1. **Colour is never the only signal** (WCAG 1.4.1, CAL-6): a clash has an icon and words, a category is named, the
   current page is bold and underlined as well as marked `aria-current`.
2. **Text meets WCAG 2.2 AA in both themes**: 4.5:1 for text, 3:1 for the borders that show where a form box is
   (1.4.11). Any new colour pair is measured before it's used and added to the table below.
3. **No new libraries or downloads for the look.** Bootstrap 5.3 (already self-hosted) with its own variables
   overridden in `wwwroot/css/site.css`; the device's own fonts; the logo is an inline SVG.
4. **Dark mode follows the device** (`wwwroot/js/theme.js` sets Bootstrap's `data-bs-theme` before the page is drawn).
   Without JavaScript the page is light. A choice in Preferences can come later.
5. **Small motion only**, and none that Bootstrap doesn't already switch off for "reduce motion".
6. **Missed or past sessions are never shown as failures in red** (P3, P4). Red is for clashes and form errors.

## Colours

| Role | Light | Dark |
|---|---|---|
| Page | `#f7f5f0` warm off-white | `#15171c` |
| Cards, lists, form boxes, header | `#ffffff` | `#1e2128` |
| Text | `#1f2328` charcoal | `#e8e6e3` |
| Secondary text, hints | `#59606a` | `#a9afb9` |
| Links | `#0b62b0` | `#7cb4f5` |
| Primary button (white text) | `#1d63b0` | `#1d63b0` |
| Errors, clashes, delete | `#b42318` | `#ff8a80` |
| Card edges (decorative) | `#e3dfd6` | `#30343d` |
| Form-box borders | `#868c96` | `#737b87` |

Measured contrast (WCAG formula, 20 October 2026):

| Pair | Light | Dark |
|---|---|---|
| Text on page / on card | 14.50 / 15.80 | 14.39 / 12.93 |
| Secondary text on page / on card | 5.83 / 6.35 | 8.13 / 7.30 |
| Links on page / on card | 5.69 / 6.20 | 8.29 / 7.45 |
| White on primary button | 6.06 | 6.06 |
| Error text on page / on card | 6.03 / 6.57 | 7.06 (card) |
| Form-box border on page / on card | 3.11 / 3.38 | 4.19 / 3.77 |

Bootstrap's own red (`#dc3545`) and outline grey (`#6c757d`) are replaced: they only just reach 4.5:1 on white, and
fall below it on the off-white page and in dark mode.

### Categories (CAL-2)

Each category has one colour everywhere (`ActivityDisplay.CategoryStyle`, the `cat-*` classes in `site.css`): a stripe
down the side of an item, and a label with the category's name. The name is always there, so the colour is never the
only signal. A new category fails `ActivityDisplayTests` until its colour is chosen.

| Group | Categories | Stripe on card, light / dark | Label text on label, light / dark |
|---|---|---|---|
| Blue | Work, Commute | `#2563eb` 5.17 / `#6ea8fe` 6.67 | `#1e40af` on `#e8effd` 7.56 / `#bcd4ff` on `#1f2a44` 9.50 |
| Green | Gym, Run, Other exercise | `#15803d` 5.02 / `#4ade80` 9.25 | `#166534` on `#e7f5ec` 6.34 / `#b7f0cb` on `#19362a` 10.20 |
| Orange | Meal prep | `#c2410c` 5.18 / `#fb923c` 7.12 | `#9a3412` on `#fdeee6` 6.45 / `#ffd2b5` on `#3d2618` 10.17 |
| Purple | Social, Appointment | `#7e22ce` 6.98 / `#c084fc` 6.10 | `#6b21a8` on `#f3e9fc` 7.42 / `#e4ccff` on `#33224a` 9.79 |
| Grey | Other | `#6b7280` 4.83 / `#9ca3af` 6.35 | `#374151` on `#eef0f2` 9.02 / `#d6dae0` on `#2c3038` 9.43 |

## Parts

- **Header**: sticks to the top of the screen; `scroll-padding-top` keeps a field the error summary links to from
  hiding under it (WCAG 2.4.11).
- **Cards and lists**: white (dark: raised) surfaces with rounded corners and a faint shadow (none in dark mode).
