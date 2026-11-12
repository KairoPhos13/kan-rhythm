/**
 * Unit tests for the streaming file uploads of the submission routes
 * (audit S1): file parts stream to a temporary file, one running byte counter
 * aborts the request early, `PUT` carries exactly one file part and `PATCH`
 * one part per changed file.
 */
import { existsSync, promises as fsp, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import express from 'express';
import request from 'supertest';
import { multipartUpload, UploadRequest } from '../src/common/multipart';

/** Temp directories of the upload middleware, one per request. */
function uploadDirs(): string[] {
  return readdirSync(tmpdir()).filter((name) => name.startsWith('kan-upload-'));
}

function app(options: { maxBytes: number; maxFiles?: number; maxPatchFiles?: number }) {
  const server = express();
  server.use(multipartUpload(options));
  server.put('/upload', (req, res) => {
    const files = (req as UploadRequest).files ?? [];
    res.json({
      files: files.map((file) => ({
        fieldname: file.fieldname,
        originalname: file.originalname,
        size: file.size,
        onDisk: existsSync(file.path),
      })),
    });
  });
  server.patch('/upload', (req, res) => {
    const files = (req as UploadRequest).files ?? [];
    res.json({ count: files.length });
  });
  return server;
}

describe('streamed file uploads', () => {
  let before: string[];

  beforeEach(() => {
    before = uploadDirs();
  });

  afterEach(async () => {
    // Whatever the outcome, no temporary directory may survive the response.
    let left = uploadDirs().filter((name) => !before.includes(name));
    for (let waited = 0; left.length > 0 && waited < 20; waited += 1) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      left = uploadDirs().filter((name) => !before.includes(name));
    }
    for (const name of left) {
      await fsp.rm(path.join(tmpdir(), name), { recursive: true, force: true });
    }
    expect(left).toEqual([]);
  });

  it('streams the file to a temporary file instead of buffering it', async () => {
    const body = Buffer.alloc(4096, 7);
    const response = await request(app({ maxBytes: 1024 * 1024 }))
      .put('/upload')
      .attach('beatmapArchive', body, 'export.osz');
    expect(response.status).toBe(200);
    expect(response.body.files).toEqual([
      { fieldname: 'beatmapArchive', originalname: 'export.osz', size: 4096, onDisk: true },
    ]);
  });

  it('refuses a second file part on PUT (the replace route takes one)', async () => {
    const response = await request(app({ maxBytes: 1024 * 1024 }))
      .put('/upload')
      .attach('beatmapArchive', Buffer.alloc(16), 'export.osz')
      .attach('beatmapArchive', Buffer.alloc(16), 'export2.osz');
    expect(response.status).toBe(400);
    expect(response.body.error).toContain('file part');
  });

  it('accepts one part per changed file on PATCH', async () => {
    const response = await request(app({ maxBytes: 1024 * 1024 }))
      .patch('/upload')
      .attach('filesChanged', Buffer.from('one'), 'a.txt')
      .attach('filesChanged', Buffer.from('tw'), 'b.txt');
    expect(response.status).toBe(200);
    expect(response.body.count).toBe(2);

    const refused = await request(app({ maxBytes: 1024 * 1024, maxPatchFiles: 1 }))
      .patch('/upload')
      .attach('filesChanged', Buffer.from('one'), 'a.txt')
      .attach('filesChanged', Buffer.from('tw'), 'b.txt');
    expect(refused.status).toBe(400);
    expect(refused.body.error).toContain('file part');
  });

  it('aborts the request as soon as the running byte counter passes the limit', async () => {
    // Ten parts of 600 bytes: the counter trips during the second part, long
    // before the request is over (audit S1).
    let upload = request(app({ maxBytes: 1000 })).patch('/upload');
    for (let i = 0; i < 10; i += 1) {
      upload = upload.attach('filesChanged', Buffer.alloc(600, i), `part-${i}.bin`);
    }
    const response = await upload;
    expect(response.status).toBe(413);
    expect(response.body.error).toContain('1000');
  });

  it('refuses one file part over the limit with 413', async () => {
    const response = await request(app({ maxBytes: 1024 }))
      .put('/upload')
      .attach('beatmapArchive', Buffer.alloc(8192), 'export.osz');
    expect(response.status).toBe(413);
    expect(response.body.error).toContain('1024');
  });
});
