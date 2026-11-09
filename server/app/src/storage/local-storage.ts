/** Local-disk storage driver (dev and tests). */
import { createReadStream, existsSync, promises as fs } from 'node:fs';
import path from 'node:path';
import { isSafeKey, OpenedFile, Storage } from './storage';

export class LocalStorage implements Storage {
  constructor(private readonly rootDir: string) {}

  private pathFor(key: string): string {
    if (!isSafeKey(key)) throw new Error(`unsafe storage key: ${key}`);
    return path.join(this.rootDir, key);
  }

  async put(key: string, body: Buffer, _contentType: string): Promise<void> {
    const target = this.pathFor(key);
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, body);
  }

  async remove(key: string): Promise<void> {
    await fs.rm(this.pathFor(key), { force: true });
  }

  async presign(_key: string, _expiresInSeconds: number): Promise<string | null> {
    // The local driver has no presigned URLs: callers stream the file instead.
    return null;
  }

  async open(key: string): Promise<OpenedFile | null> {
    const target = this.pathFor(key);
    try {
      const stat = await fs.stat(target);
      return { stream: createReadStream(target), size: stat.size };
    } catch {
      return null;
    }
  }

  async exists(key: string): Promise<boolean> {
    return existsSync(this.pathFor(key));
  }
}
