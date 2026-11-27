# Phase 44 — DiceBear profile avatars

Profile settings now let a person use an uploaded photo, a generated illustration, or initials.
The illustrated avatar picker offers Identicon, Lorelei, Open Peeps, Pixel Art, Shapes, and Thumbs.
Each style has a new variation action. SVG generation runs locally in the browser; the API stores
only the style slug and seed so the same illustration appears on every device. Selecting a photo,
another style, or initials clears the previous avatar choice and removes any replaced photo file.

The six bundled style definitions declare the [CC0 1.0 Universal license](https://creativecommons.org/publicdomain/zero/1.0/).
The picker links to the license. DiceBear's core package is MIT licensed; the style definitions
retain their individual licenses.

## Database migration

Apply `Phase44DiceBearAvatars` in both SQLite and PostgreSQL deployments. It adds nullable
`generated_avatar_style` and `generated_avatar_seed` columns to `user_profiles`; existing profiles
continue to use their uploaded photo or initials.

## API

`PUT /api/v1/users/me/avatar` accepts `{ "style": "pixel-art", "seed": "stable-value" }`. To
return to initials, send `{ "style": null, "seed": null }`. Unknown styles and invalid seeds are
rejected. The current-user response and user summaries include the generated style and seed.
