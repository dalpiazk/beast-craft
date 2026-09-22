# audio-generation

Prompt templates and configs for the **Stable Audio (Creator tier)** passes that
produce Beast Craft's shipped music, ambient beds and sound effects. Stable Audio
Creator is the *only* sanctioned source for shipped audio — its explicit
commercial terms cover distribution in a paid mobile title. **Google Lyria is
permitted for free prototyping and temp tracks only and must never reach a
shipped build**, as it has no published commercial terms; any Lyria temp cue has
to be regenerated in Stable Audio before it enters `BeastCraft/Assets/`. Configs
here capture the per-cue intent (mood, instrumentation, tempo, target length) and
whether the cue is a seamless loop or a one-shot, since that drives both the
generation settings and the `<category>_<name>_<loop|oneshot>.wav` filename.
Deliverables are 48 kHz `.wav`; platform compression is a Unity import setting,
never pre-baked here. No scripts yet — templates and configs land in a later step.
