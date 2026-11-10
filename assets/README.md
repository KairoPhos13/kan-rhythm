# assets/

Shared creative assets: graphics, sounds, fonts, and other binary resources
used by the game client and tools.

**Status:** placeholder, no assets committed yet.

## Intended contents

- `graphics/`: sprites, UI art, backgrounds
- `audio/`: UI sounds, reference tones, sample recordings for DSP tests
- `fonts/`: licensed font files

## Ground rules

- Every asset must have a clear license; record provenance and license in
  a `CREDITS.md` inside each subfolder (English, see root `CONTRIBUTING.md`)
- No GPL/AGPL-licensed assets (incompatible with our MIT license)
- Prefer open, diff-friendly formats where possible (SVG, PNG, WAV, OGG)
- Keep assets out of code components: reference them by path, never embed
- Large binaries: discuss Git LFS before committing anything over ~5 MB
