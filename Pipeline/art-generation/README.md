# art-generation

Prompt templates and per-category generation configs for the **Gemini (Nano
Banana 2, paid API tier)** image passes that produce Beast Craft's character,
creature, environment, UI and key art. This stage owns the *look*: a consistent
painterly, soft-edged, warm-lit 2D style applied across every subject, plus the
per-category framing rules (creatures on a neutral field at a fixed pivot,
characters in a consistent three-quarter pose, environments at the target
parallax layer size). Prompts describe style by its qualities only — original IP,
no living artists, studios, franchises or characters named. Output from this
stage is a **raw** generation: it goes to `sprite-cleanup/` before it is allowed
anywhere near `content/art/`, and it is named per the
`<category>_<subject>_<variant>_<state>.png` convention in the parent README.
No scripts yet — templates and configs land in a later step.
