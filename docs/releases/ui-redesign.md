# UI redesign (2026-11): delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-24. Requested by the project
owner before Phase 25.

## Owner decisions of 2026-11-24

| Question | Decision |
| --- | --- |
| Reference | A commercial chat template the owner holds a licence for, used **only as visual inspiration**: no code, styles, class names, images, icons or fonts copied (it is not open source). |
| Accent colour | Keep our teal (the template's violet would make the product look like it) |
| Layout | Full-height panels: rail, list and conversation fill the window, told apart by tone |
| Scope | The whole app: tokens, shell, chat, sign-in pages, every other page through the shared tokens |

## Delivered

| Area | What |
| --- | --- |
| Tokens (`styles/theme.css`) | Light: cool lavender-grey canvas, white working surfaces, filled inputs. Dark: layered slate greys (rail, list and chat one step apart, never pure black). Teal accent kept (`#0f766e`, `#3cc4b4` in dark); bubble colours with their own tokens so white text stays readable in both themes. 15 px base text, softer borders and shadows, 0.75 rem radius. |
| Design layer (`styles/design.css`, new) | All layout and component rules of the redesign, written for this project and commented. |
| Chat | Edge-to-edge list and conversation on wide screens; teal bubbles for other people, soft grey for your own, small time line inside the bubble; reactions and thread summaries under it; history events as small centred pills; a clean composer (filled field, icon attach and send buttons). |
| Message actions | "Reply · React · Reply in thread · More · Edit · Delete" moved from a line of links under every message into a small icon toolbar beside the bubble, shown on hover, tap or keyboard focus (`ToolButton`, our own line icons in `NavIcon`). Buttons keep their words as accessible names. |
| Signed-out pages | The teal side panel became a slim top bar (brand, sign in, create account); pages are centred; language and licence moved to a footer. The home page opens with the product introduction and its two actions; the outdated "Next milestone" card was removed. |
| Phones | Message tools float over the bubble's corner; the formatting toolbar and the keyboard hint are hidden; the history is sized so the composer sits above the tab bar; the top bar fits on one line. |
| Accessibility | Focus moves to the new page without scrolling the top bar away. Focus rings, labels and keyboard access unchanged. |

## Validation

- Checked in the browser on a local throwaway installation with demo data: chat, list, account,
  contacts, home and sign-in, in light and dark, at 1280×800 and 375×812 (phone).
- Web tests 81/81 (unchanged: every action is still found by its name); type-check clean; bundle
  167.5 kB of 180 kB.
- No API or database change.

## Open items

- Administration, channels, search and the support inbox were restyled only through the shared
  tokens; a dedicated pass can follow if they need it.
- The template folder (`Demo/`) stays out of the repository (it is in `.gitignore`).
