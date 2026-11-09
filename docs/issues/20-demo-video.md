# A short demo video for the release

Labels: `docs`, `help wanted`
Size: M

## Context

The release checklist in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
asks for *"a short demo video"* next to the README work — a first
impression for people who will never build the project from source. The
root [README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
has a "Join in" section and a player story ("practice made fun … what
players learn goes with them — to their church, their friends, their
community"), but words cannot show the core moment: you blow a real
flute, and the game listens and judges what you play.

The video is also the tone-setter: kan! is calm by design, and the video
must not look like an adrenaline reel.

## What to do

A script and shot list already exists: [game/docs/demo-video-script.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/demo-video-script.md) (legal rules for music on screen included). Start from it.


1. Produce a **30–90 second** demo video showing, in order:
   - grabbing an instrument and speaking/playing toward a plain
     microphone (nothing else needed — no cables, no account);
   - a few seconds of real play: notes scrolling on the staff, the
     player playing them, the judge responding (perfect/good/miss shown
     calmly, including one forgiving "a little off is still a hit"
     moment);
   - a glimpse of making a level in kan!forge (import a song, choose the
     companion, a few notes on the piano roll) — optional but valuable;
   - end card: kan! logo, "Grab your instrument. You kan!", where to get
     it (link left as a placeholder if the release URL is not final).
2. Constraints:
   - audio must be yours or freely licensed (your own playing is ideal
     and on-brand); never osu!'s music, voices, art or logo;
   - calm pacing; no countdown pressure, no hype language, no
     "addictive" framing — the project explicitly rejects that
     ([game/README.md — Ground rules](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md));
   - captions/subtitles included (accessibility; also serves the
     no-audio viewing case).
3. Deliver the video file plus a poster frame and a written shot list;
   where it is hosted is a maintainer decision (link placeholders in the
   PR are fine).

## Acceptance criteria

- [ ] 30–90 s video: instrument + microphone + real play + judging
      visible
- [ ] All audio and visuals original or freely licensed; no osu!
      assets, music, voices or logos anywhere
- [ ] Captions/subtitles included
- [ ] Poster frame + shot list delivered alongside the file
- [ ] Tone check: calm and inviting; no pressure/compulsion framing
- [ ] A maintainer-approved place to embed it (root README "Join in" /
      player section) is proposed in the PR

## Pointers

- [game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md) —
  what to show ("What it does" is a ready-made script outline)
- [README.md — Join in](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md) —
  the audience and the message
- `game/tools/gen-provisional-brand.py` / `game/brand/` — the current
  logo assets for the end card (do not draw osu!'s)
- [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md) —
  what judging looks like, for scripting the play segment

## Notes

- Recording the game window and your hands in one shot beats heavy
  editing; authenticity fits the product.
- If the only obstacle is the release URL/logo file, say so in the PR —
  a maintainer can fill those in.
