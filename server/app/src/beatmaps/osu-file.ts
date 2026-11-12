/**
 * `.osu` (osu!lazer beatmap) file parsing and id rewriting.
 *
 * Format reference: the osu! file format as read by the client; only the
 * fields the importer and the API need are parsed (server/docs/mvp-scope.md
 * §4). The parser is deliberately line-based and tolerant of unknown keys.
 */
import { createHash } from 'node:crypto';

export interface OsuGeneral {
  audioFilename: string;
  previewTime: number;
  mode: number;
}

export interface OsuMetadata {
  title: string;
  titleUnicode: string;
  artist: string;
  artistUnicode: string;
  creator: string;
  version: string;
  source: string;
  tags: string;
  /** `BeatmapID:`, 0 when absent. */
  beatmapId: number;
  /** `BeatmapSetID:`, 0 when absent. */
  beatmapSetId: number;
}

export interface OsuDifficulty {
  circleSize: number;
  overallDifficulty: number;
  approachRate: number;
  hpDrainRate: number;
}

export interface OsuFileInfo {
  general: OsuGeneral;
  metadata: OsuMetadata;
  difficulty: OsuDifficulty;
  /** Background image filename from `[Events]`, if any. */
  backgroundFile: string | null;
  /** First uninherited timing point, beats per minute. */
  bpm: number;
  /** Seconds from the first to the last hit object (rounded up). */
  totalLengthSeconds: number;
  hitLengthSeconds: number;
  hitObjectCount: number;
  countCircles: number;
  countSliders: number;
  countSpinners: number;
  /** Approximation: sliders count as `slides + 1` combo (no slider ticks). */
  maxCombo: number;
}

const EMPTY_FILE_INFO: OsuFileInfo = {
  general: { audioFilename: '', previewTime: -1, mode: 0 },
  metadata: {
    title: '',
    titleUnicode: '',
    artist: '',
    artistUnicode: '',
    creator: '',
    version: '',
    source: '',
    tags: '',
    beatmapId: 0,
    beatmapSetId: 0,
  },
  difficulty: { circleSize: 0, overallDifficulty: 0, approachRate: 0, hpDrainRate: 0 },
  backgroundFile: null,
  bpm: 0,
  totalLengthSeconds: 0,
  hitLengthSeconds: 0,
  hitObjectCount: 0,
  countCircles: 0,
  countSliders: 0,
  countSpinners: 0,
  maxCombo: 0,
};

function stripBom(text: string): string {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

function splitLines(text: string): string[] {
  return stripBom(text).split(/\r\n|\n|\r/);
}

function numberOr(value: string | undefined, fallback: number): number {
  const parsed = Number.parseFloat((value ?? '').trim());
  return Number.isFinite(parsed) ? parsed : fallback;
}

/**
 * A number from a `.osu` file, clamped into the range the format allows (audit
 * S8: crafted values used to overflow the catalogue's columns and answer 500
 * after a full upload). `fallback` stands where the key is missing or broken.
 */
function clampedNumberOr(value: string | undefined, fallback: number, min: number, max: number): number {
  return clamp(numberOr(value, fallback), min, max);
}

/** Like {@link clampedNumberOr}, for the integer fields of the format. */
function clampedIntOr(value: string | undefined, fallback: number, min: number, max: number): number {
  return clamp(Math.trunc(numberOr(value, fallback)), min, max);
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

/** osu!'s difficulty settings run 0..10 (CS, AR, OD, HP). */
const DIFFICULTY_MIN = 0;
const DIFFICULTY_MAX = 10;
/** Longest level the catalogue holds (seconds); nothing real comes near it. */
const MAX_LENGTH_SECONDS = 24 * 60 * 60;
/** Longest level (milliseconds) for `PreviewTime`; osu! stores -1 for "none". */
const MAX_PREVIEW_MS = 24 * 60 * 60 * 1000;
/** Fastest tempo the catalogue holds (BPM). */
const MAX_BPM = 1000;
/** Upper bound on a difficulty's combo counter (audit S8). */
const MAX_COMBO_OBJECTS = 10_000_000;
/** Slider traversals (`slides`): the field is a count, not a free number. */
const MAX_SLIDES = 1000;

/**
 * Parses the sections of a `.osu` file that matter for the catalogue and the
 * API responses.
 */
export function parseOsu(text: string): OsuFileInfo {
  const info: OsuFileInfo = structuredClone(EMPTY_FILE_INFO);
  const lines = splitLines(text);

  let section = '';
  let lastHitObjectMs = 0;
  let sawTimingPoint = false;

  for (const raw of lines) {
    const line = raw.trim();
    if (line.length === 0) continue;

    const sectionMatch = /^\[(.+)\]$/.exec(line);
    if (sectionMatch) {
      section = sectionMatch[1];
      continue;
    }
    if (line.startsWith('//')) continue;

    if (section === 'General' || section === 'Metadata' || section === 'Difficulty') {
      const separator = line.indexOf(':');
      if (separator < 0) continue;
      // Key matching is case-insensitive (archives in the wild vary).
      const key = line.slice(0, separator).trim().toLowerCase();
      const value = line.slice(separator + 1).trim();
      applyKey(info, section, key, value);
      continue;
    }

    if (section === 'Events') {
      // `0,0,"background.jpg",0,0`: a background; `Video,...` is ignored.
      const event = /^\s*0\s*,/.exec(line);
      if (event) {
        const quoted = /"([^"]*)"/.exec(line);
        if (quoted && !info.backgroundFile) info.backgroundFile = quoted[1];
      }
      continue;
    }

    if (section === 'TimingPoints') {
      // time,beatLength,meter,...,uninherited (1 = red line)
      const parts = line.split(',');
      const beatLength = numberOr(parts[1], 0);
      const uninherited = numberOr(parts[6], 1);
      if (!sawTimingPoint && uninherited === 1 && beatLength > 0) {
        info.bpm = clamp(Math.round((60000 / beatLength) * 100) / 100, 0, MAX_BPM);
        sawTimingPoint = true;
      }
      continue;
    }

    if (section === 'HitObjects') {
      const parts = line.split(',');
      const time = numberOr(parts[2], 0);
      const type = numberOr(parts[3], 0);
      info.hitObjectCount += 1;
      let end = time;
      if (type & 8) {
        // Spinner: end time in field 6 (0-based index 5).
        info.countSpinners += 1;
        end = Math.max(time, numberOr(parts[5], time));
        info.maxCombo += 1;
      } else if (type & 2) {
        // Slider: `slides` (traversals) in field 7; combo ≈ slides + 1.
        info.countSliders += 1;
        const slides = clampedNumberOr(parts[6], 1, 1, MAX_SLIDES);
        info.maxCombo += slides + 1;
      } else if (type & 128) {
        // Hold note (mania): end time in field 6.
        end = Math.max(time, numberOr(parts[5], time));
        info.maxCombo += 1;
      } else {
        info.countCircles += 1;
        info.maxCombo += 1;
      }
      lastHitObjectMs = Math.max(lastHitObjectMs, end);
    }
  }

  const seconds = clamp(Math.ceil(lastHitObjectMs / 1000), 0, MAX_LENGTH_SECONDS);
  info.totalLengthSeconds = seconds;
  info.hitLengthSeconds = seconds;
  info.maxCombo = clamp(info.maxCombo, 0, MAX_COMBO_OBJECTS);
  return info;
}

function applyKey(info: OsuFileInfo, section: string, key: string, value: string): void {
  if (section === 'General') {
    switch (key) {
      case 'audiofilename':
        info.general.audioFilename = value;
        break;
      case 'previewtime':
        info.general.previewTime = clampedIntOr(value, -1, -1, MAX_PREVIEW_MS);
        break;
      case 'mode':
        info.general.mode = clampedIntOr(value, 0, 0, 3);
        break;
    }
    return;
  }
  if (section === 'Metadata') {
    switch (key) {
      case 'title':
        info.metadata.title = value;
        break;
      case 'titleunicode':
        info.metadata.titleUnicode = value;
        break;
      case 'artist':
        info.metadata.artist = value;
        break;
      case 'artistunicode':
        info.metadata.artistUnicode = value;
        break;
      case 'creator':
        info.metadata.creator = value;
        break;
      case 'version':
        info.metadata.version = value;
        break;
      case 'source':
        info.metadata.source = value;
        break;
      case 'tags':
        info.metadata.tags = value;
        break;
      case 'beatmapid':
        info.metadata.beatmapId = clampedIntOr(value, 0, 0, 2_147_483_647);
        break;
      case 'beatmapsetid':
        info.metadata.beatmapSetId = clampedIntOr(value, 0, 0, 2_147_483_647);
        break;
    }
    return;
  }
  if (section === 'Difficulty') {
    switch (key) {
      case 'circlesize':
        info.difficulty.circleSize = clampedNumberOr(value, 0, DIFFICULTY_MIN, DIFFICULTY_MAX);
        break;
      case 'overalldifficulty':
        info.difficulty.overallDifficulty = clampedNumberOr(value, 0, DIFFICULTY_MIN, DIFFICULTY_MAX);
        break;
      case 'approachrate':
        info.difficulty.approachRate = clampedNumberOr(
          value,
          info.difficulty.overallDifficulty,
          DIFFICULTY_MIN,
          DIFFICULTY_MAX,
        );
        break;
      case 'hpdrainrate':
        info.difficulty.hpDrainRate = clampedNumberOr(value, 0, DIFFICULTY_MIN, DIFFICULTY_MAX);
        break;
    }
  }
}

/**
 * Writes `BeatmapID:` / `BeatmapSetID:` into the `[Metadata]` section,
 * replacing existing values or appending the keys when missing. Everything
 * else in the file (including line endings) is preserved byte for byte.
 */
export function rewriteOsu(text: string, ids: { beatmapId: number; beatmapSetId: number }): string {
  const usesCrlf = stripBom(text).includes('\r\n');
  const newline = usesCrlf ? '\r\n' : '\n';
  const hadBom = text.charCodeAt(0) === 0xfeff;

  const lines = splitLines(text);
  const metadataStart = lines.findIndex((line) => line.trim() === '[Metadata]');
  if (metadataStart < 0) {
    throw new Error('.osu file has no [Metadata] section');
  }

  let metadataEnd = lines.length;
  for (let i = metadataStart + 1; i < lines.length; i += 1) {
    if (/^\[.+\]$/.test(lines[i].trim())) {
      metadataEnd = i;
      break;
    }
  }

  const out: string[] = [];
  out.push(...lines.slice(0, metadataStart + 1));

  let sawBeatmapId = false;
  let sawBeatmapSetId = false;
  for (const line of lines.slice(metadataStart + 1, metadataEnd)) {
    const key = /^([^:]+):/.exec(line.trim())?.[1]?.trim().toLowerCase();
    if (key === 'beatmapid') {
      out.push(`BeatmapID: ${ids.beatmapId}`);
      sawBeatmapId = true;
    } else if (key === 'beatmapsetid') {
      out.push(`BeatmapSetID: ${ids.beatmapSetId}`);
      sawBeatmapSetId = true;
    } else {
      out.push(line);
    }
  }
  if (!sawBeatmapId) out.push(`BeatmapID: ${ids.beatmapId}`);
  if (!sawBeatmapSetId) out.push(`BeatmapSetID: ${ids.beatmapSetId}`);

  out.push(...lines.slice(metadataEnd));

  // splitLines() drops the trailing newline marker; re-add it as a single one.
  const body = out.join(newline);
  return (hadBom ? '\uFEFF' : '') + body;
}

/** MD5 of the exact bytes stored in the archive, the client's checksum. */
export function md5Of(content: string | Buffer): string {
  return createHash('md5')
    .update(typeof content === 'string' ? Buffer.from(content, 'utf8') : content)
    .digest('hex');
}
