/**
 * Test fixtures: a minimal, self-made `.osz` archive (no copyrighted audio —
 * a generated silent WAV and a generated 1x1 PNG) with one valid `.osu`.
 */
import AdmZip from 'adm-zip';

export interface FixtureOsu {
  text: string;
  version: string;
}

export const FIXTURE_TITLE = 'Fixture Song';
export const FIXTURE_ARTIST = 'Fixture Artist';
export const FIXTURE_CREATOR = 'KairoPhos';

/** Minimal but valid `.osu` content (osu file format v14). */
export function minimalOsu(options: { version?: string; beatmapId?: number; beatmapSetId?: number } = {}): string {
  const version = options.version ?? 'Normal';
  return [
    'osu file format v14',
    '',
    '[General]',
    'AudioFilename: audio.wav',
    'PreviewTime: -1',
    'Countdown: 0',
    'SampleSet: Soft',
    'StackLeniency: 0.7',
    'Mode: 0',
    'LetterboxInBreaks: 0',
    '',
    '[Editor]',
    'DistanceSpacing: 1',
    'BeatDivisor: 4',
    'GridSize: 4',
    'TimelineZoom: 1',
    '',
    '[Metadata]',
    `Title:${FIXTURE_TITLE}`,
    `TitleUnicode:${FIXTURE_TITLE}`,
    `Artist:${FIXTURE_ARTIST}`,
    `ArtistUnicode:${FIXTURE_ARTIST}`,
    `Creator:${FIXTURE_CREATOR}`,
    `Version:${version}`,
    'Source:',
    'tags:fixture test kan',
    `BeatmapID:${options.beatmapId ?? 0}`,
    `BeatmapSetID:${options.beatmapSetId ?? 0}`,
    '',
    '[Difficulty]',
    'HPDrainRate:5',
    'CircleSize:4',
    'OverallDifficulty:6',
    'ApproachRate:7',
    'SliderMultiplier:1.4',
    'SliderTickRate:1',
    '',
    '[Events]',
    '//Background and Video events',
    '0,0,"bg.png",0,0',
    '',
    '[TimingPoints]',
    '0,500,4,2,0,60,1,0',
    '',
    '[HitObjects]',
    '256,192,0,1,0,0:0:0:0:',
    '100,100,500,1,0,0:0:0:0:',
    '400,100,1000,2,0,L|440:100,1,100',
    '256,192,1500,12,0,2000,0:0:0:0:',
    '',
  ].join('\n');
}

/** 44-byte WAV header + 20 ms of silence at 8 kHz mono 16-bit. */
export function silentWav(): Buffer {
  const sampleRate = 8000;
  const samples = 160;
  const dataSize = samples * 2;
  const buffer = Buffer.alloc(44 + dataSize);
  buffer.write('RIFF', 0, 'ascii');
  buffer.writeUInt32LE(36 + dataSize, 4);
  buffer.write('WAVE', 8, 'ascii');
  buffer.write('fmt ', 12, 'ascii');
  buffer.writeUInt32LE(16, 16);
  buffer.writeUInt16LE(1, 20); // PCM
  buffer.writeUInt16LE(1, 22); // mono
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(sampleRate * 2, 28);
  buffer.writeUInt16LE(2, 32);
  buffer.writeUInt16LE(16, 34);
  buffer.write('data', 36, 'ascii');
  buffer.writeUInt32LE(dataSize, 40);
  return buffer;
}

/** 1x1 transparent PNG. */
export function tinyPng(): Buffer {
  return Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
    'base64',
  );
}

export interface BuildOszOptions {
  versions?: string[];
  title?: string;
  artist?: string;
  creator?: string;
  includeIds?: { beatmapId: number; beatmapSetId: number };
}

/** Builds an in-memory `.osz` (zip) with one `.osu` per difficulty. */
export function buildOsz(options: BuildOszOptions = {}): Buffer {
  const versions = options.versions ?? ['Normal'];
  const zip = new AdmZip();
  for (const version of versions) {
    let text = minimalOsu({ version });
    if (options.title) text = text.replace(new RegExp(`Title:${FIXTURE_TITLE}`, 'g'), `Title:${options.title}`);
    if (options.artist) text = text.replace(new RegExp(`Artist:${FIXTURE_ARTIST}`, 'g'), `Artist:${options.artist}`);
    if (options.creator) text = text.replace(new RegExp(`Creator:${FIXTURE_CREATOR}`, 'g'), `Creator:${options.creator}`);
    if (options.includeIds) {
      text = text
        .replace('BeatmapID:0', `BeatmapID:${options.includeIds.beatmapId}`)
        .replace('BeatmapSetID:0', `BeatmapSetID:${options.includeIds.beatmapSetId}`);
    }
    zip.addFile(`${options.artist ?? FIXTURE_ARTIST} - ${options.title ?? FIXTURE_TITLE} (${options.creator ?? FIXTURE_CREATOR}) [${version}].osu`, Buffer.from(text, 'utf8'));
  }
  zip.addFile('audio.wav', silentWav());
  zip.addFile('bg.png', tinyPng());
  return zip.toBuffer();
}
