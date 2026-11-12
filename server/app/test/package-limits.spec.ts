/**
 * Unit tests for the zip bomb defence of package reading (audit S2): the
 * central directory is checked (entry count, declared sizes, declared total,
 * ratio to the archive's own size) before anything is inflated, and one entry
 * cannot inflate past its declared size even when its header lies.
 */
import AdmZip from 'adm-zip';
import {
  PackageRejectedError,
  PackageTooLargeError,
  readPackageBuffer,
  zipLimitsFor,
  ZipLimits,
} from '../src/beatmaps/importer';
import { buildOsz, silentWav } from './fixture';

const SMALL: ZipLimits = {
  maxEntries: 10,
  maxEntryBytes: 1000,
  maxTotalBytes: 1500,
  maxCompressionRatio: 5,
};

function archiveOf(files: Record<string, Buffer>): Buffer {
  const zip = new AdmZip();
  for (const [name, data] of Object.entries(files)) zip.addFile(name, data);
  return zip.toBuffer();
}

describe('package zip bounds', () => {
  it('reads a normal level package unchanged', () => {
    const files = readPackageBuffer(buildOsz(), zipLimitsFor(100 * 1024 * 1024));
    expect(files.map((file) => file.entryName).sort()).toEqual([
      'Fixture Artist - Fixture Song (KairoPhos) [Normal].osu',
      'audio.wav',
      'bg.png',
    ]);
    expect(files.find((file) => file.entryName === 'audio.wav')?.data).toEqual(silentWav());
  });

  it('refuses a package with too many entries', () => {
    const many: Record<string, Buffer> = {};
    for (let i = 0; i < 11; i += 1) many[`f${i}.txt`] = Buffer.from('x');
    expect(() => readPackageBuffer(archiveOf(many), SMALL)).toThrow(PackageRejectedError);
    expect(() => readPackageBuffer(archiveOf(many), SMALL)).toThrow(/11 files/);
  });

  it('refuses an entry that declares more than the per-entry limit', () => {
    const archive = archiveOf({ 'big.bin': Buffer.alloc(1500) });
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(PackageTooLargeError);
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(/"big\.bin" declares 1500/);
  });

  it('refuses a package whose declared total is over the limit', () => {
    const archive = archiveOf({ 'a.bin': Buffer.alloc(900), 'b.bin': Buffer.alloc(800) });
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(PackageTooLargeError);
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(/inflates to 1700/);
  });

  it('refuses a package that inflates far past its own size', () => {
    // 1000 bytes of zeros in an archive of under 150 bytes: ratio over 5.
    const archive = archiveOf({ 'zeros.bin': Buffer.alloc(1000) });
    expect(archive.length).toBeLessThan(150);
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(PackageTooLargeError);
    expect(() => readPackageBuffer(archive, SMALL)).toThrow(/inflates \d+x its own size/);
  });

  it('refuses a realistic zip bomb before inflating it', () => {
    // 4 MB of zeros compress to a few KB: the classic "small archive that
    // declares megabytes". The default bounds answer without inflating.
    const bomb = archiveOf({ 'bomb.bin': Buffer.alloc(4 * 1024 * 1024) });
    expect(bomb.length).toBeLessThan(64 * 1024);
    expect(() => readPackageBuffer(bomb, zipLimitsFor(100 * 1024 * 1024))).toThrow(
      PackageTooLargeError,
    );
  });

  it('caps one entry at its declared size even when the header lies', () => {
    // Patch both size fields of the local header and the central directory to
    // claim 10 bytes for data that inflates to 100000: the inflater must fail
    // instead of producing the bomb.
    const zip = new AdmZip();
    zip.addFile('liar.bin', Buffer.alloc(100000, 65));
    const archive = zip.toBuffer();
    archive.writeUInt32LE(10, 22); // local header: uncompressed size
    const central = archive.indexOf(Buffer.from([0x50, 0x4b, 0x01, 0x02]));
    expect(central).toBeGreaterThan(0);
    archive.writeUInt32LE(10, central + 24); // central directory: uncompressed size

    expect(() =>
      readPackageBuffer(archive, { ...SMALL, maxEntryBytes: 10_000_000, maxTotalBytes: 10_000_000, maxCompressionRatio: 100_000 }),
    ).toThrow(PackageRejectedError);
  });
});
