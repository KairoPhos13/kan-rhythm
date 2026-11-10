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
        info.bpm = Math.round((60000 / beatLength) * 100) / 100;
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
        const slides = Math.max(1, numberOr(parts[6], 1));
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

  const seconds = Math.ceil(lastHitObjectMs / 1000);
  info.totalLengthSeconds = seconds;
  info.hitLengthSeconds = seconds;
  return info;
}

function applyKey(info: OsuFileInfo, section: string, key: string, value: string): void {
  if (section === 'General') {
    switch (key) {
      case 'audiofilename':
        info.general.audioFilename = value;
        break;
      case 'previewtime':
        info.general.previewTime = numberOr(value, -1);
        break;
      case 'mode':
        info.general.mode = numberOr(value, 0);
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
        info.metadata.beatmapId = numberOr(value, 0);
        break;
      case 'beatmapsetid':
        info.metadata.beatmapSetId = numberOr(value, 0);
        break;
    }
    return;
  }
  if (section === 'Difficulty') {
    switch (key) {
      case 'circlesize':
        info.difficulty.circleSize = numberOr(value, 0);
        break;
      case 'overalldifficulty':
        info.difficulty.overallDifficulty = numberOr(value, 0);
        break;
      case 'approachrate':
        info.difficulty.approachRate = numberOr(value, info.difficulty.overallDifficulty);
        break;
      case 'hpdrainrate':
        info.difficulty.hpDrainRate = numberOr(value, 0);
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
