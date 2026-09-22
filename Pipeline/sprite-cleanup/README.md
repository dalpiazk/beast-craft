# sprite-cleanup

Post-processing recipes that turn a raw Gemini generation into an import-ready
sprite, using **rembg** (open-source background removal, self-hosted) and
**GIMP** (open-source, scripted via Script-Fu for the batch steps). This stage
owns everything between "the model produced an image" and "Unity can import it":
background knockout to true alpha, matte/halo cleanup at the edges, alpha
trimming and consistent padding, canvas normalization so a subject's pivot is
stable across its variants and states, sprite-sheet packing for multi-frame
states, and a final power-of-two / max-dimension check against the mobile texture
budget. Everything runs locally — no cloud service, no per-image cost, fully
reproducible. Output of this stage is the *only* art that gets committed to
`BeastCraft/Assets/_Project/Art/`, under the
`<category>_<subject>_<variant>_<state>.png` convention in the parent README; raw
inputs stay in ignored scratch directories. No scripts yet — recipes land in a
later step.
