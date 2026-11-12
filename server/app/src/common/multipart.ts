/**
 * multipart/form-data handling.
 *
 * The game's forms arrive as multipart/form-data: osu!framework's WebRequest
 * posts every AddParameter body that way (POST /oauth/token, POST /users).
 * Express parses only JSON and urlencoded, so without this the server saw an
 * empty body ("Unknown OAuth client").
 *
 * Two middlewares live here:
 *  - `multipartFields()` for the text-only forms (any file part, or a form over
 *    the limits, is refused with 400). It stands aside for the submission
 *    routes, which carry files and are parsed by `multipartUpload()`;
 *  - `multipartUpload()` for the level packages the editor uploads
 *    (PutBeatmapSetRequest / ReplaceBeatmapPackageRequest /
 *    PatchBeatmapPackageRequest, server/docs/lazer-protocol.md §4.8).
 *
 * Uploaded file parts are never buffered in memory: every part streams into a
 * temporary directory that is removed again when the response is done, and one
 * running byte counter for the whole request stops parsing the moment the
 * request would exceed `maxBytes` (answered with 413). Ten 100 MB parts
 * therefore cost one temporary file of at most `maxBytes`, not a gigabyte of
 * heap (audit S1). `PUT` carries exactly one file part (`beatmapArchive`);
 * `PATCH` carries one part per changed file, bounded by `maxPatchFiles`.
 */
import { randomBytes } from 'node:crypto';
import { createWriteStream, mkdirSync, promises as fsp } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { NextFunction, Request, Response } from 'express';
import multer, { StorageEngine } from 'multer';

/** The editor's submission routes (`src/beatmaps/submission.controller.ts`). */
export const SUBMISSION_PATH_PREFIX = '/beatmap-submission';

/**
 * File parts a `PATCH /beatmap-submission/beatmapsets/{id}` may carry: the
 * client sends one part per changed file (`PatchBeatmapPackageRequest`), so
 * this is an overhead bound, not the size bound (that is the running counter).
 */
export const MAX_CHANGED_FILES = 256;

const fieldsOnly = multer({
  limits: { fields: 20, fieldSize: 16 * 1024, fieldNameSize: 100, files: 0, parts: 20 },
}).none();

export function multipartFields() {
  return (req: Request, res: Response, next: NextFunction): void => {
    if (!req.is('multipart/form-data') || req.path.startsWith(SUBMISSION_PATH_PREFIX)) {
      next();
      return;
    }
    fieldsOnly(req, res, (error?: unknown) => {
      if (error) {
        res.status(400).json({ error: 'Invalid form data' });
        return;
      }
      next();
    });
  };
}

/**
 * One uploaded file, streamed to a temporary file (the package is parsed from
 * there right away; `multipartUpload` deletes the file with the response).
 */
export interface UploadedFile {
  /** Form field name: `beatmapArchive` (replace) or `filesChanged` (patch). */
  fieldname: string;
  /** Archive entry name the client uploaded (`PatchBeatmapPackageRequest`). */
  originalname: string;
  /** Path of the streamed temporary file. */
  path: string;
  /** Bytes written for this part. */
  size: number;
}

/** Files and text fields of a parsed multipart request. */
export interface UploadRequest extends Request {
  files?: UploadedFile[];
}

/**
 * Parses a multipart request with files into `req.files` / `req.body`, one
 * temporary file per part and a running byte counter over the whole request.
 * The request is refused with 413 as soon as the counter passes `maxBytes`,
 * and every temporary file is removed when the response is done.
 */
export function multipartUpload(options: {
  maxBytes: number;
  /** File parts on the routes that carry exactly one (`PUT`, replace). */
  maxFiles?: number;
  /** File parts on `PATCH` (one per changed file). */
  maxPatchFiles?: number;
}): (req: UploadRequest, res: Response, next: NextFunction) => void {
  return (req: UploadRequest, res: Response, next: NextFunction): void => {
    if (!req.is('multipart/form-data')) {
      next();
      return;
    }

    const maxFiles =
      req.method === 'PATCH'
        ? options.maxPatchFiles ?? MAX_CHANGED_FILES
        : options.maxFiles ?? 1;

    // One temporary directory per request; it goes away with the response,
    // whether the upload succeeded, was refused or was aborted halfway.
    const dir = path.join(tmpdir(), `kan-upload-${randomBytes(8).toString('hex')}`);
    mkdirSync(dir, { recursive: true });
    res.on('close', () => {
      void fsp.rm(dir, { recursive: true, force: true }).catch(() => undefined);
    });

    // Shared by every part of this request: the running byte counter that
    // aborts parsing early (audit S1).
    let received = 0;
    let sequence = 0;

    const storage: StorageEngine = {
      _handleFile(_req, file, cb): void {
        const target = path.join(dir, `${sequence}.part`);
        sequence += 1;
        const out = createWriteStream(target, { flags: 'wx' });
        let size = 0;
        let settled = false;
        const settle = (error?: unknown, info?: { path: string; size: number }): void => {
          if (settled) return;
          settled = true;
          cb(error, info);
        };
        const discard = (): void => {
          file.stream.unpipe(out);
          out.destroy();
          void fsp.unlink(target).catch(() => undefined);
        };

        // Counting on the source stream (not the write stream) so the counter
        // is honest even while a write is still draining.
        file.stream.on('data', (chunk: Buffer) => {
          if (settled) return;
          size += chunk.length;
          received += chunk.length;
          if (received > options.maxBytes) {
            discard();
            settle(
              Object.assign(new Error('upload is larger than the limit'), {
                code: 'REQUEST_TOO_LARGE',
              }),
            );
          }
        });

        file.stream.pipe(out);
        out.on('finish', () => settle(undefined, { path: target, size }));
        out.on('error', (error) => {
          discard();
          settle(error);
        });
        file.stream.on('error', (error) => {
          discard();
          settle(error);
        });
      },
      _removeFile(_req, file, cb): void {
        void fsp
          .unlink(file.path ?? '')
          .catch(() => undefined)
          .finally(() => cb(null));
      },
    };

    const upload = multer({
      storage,
      limits: {
        fileSize: options.maxBytes,
        files: maxFiles,
        fields: 64,
        fieldSize: 64 * 1024,
        fieldNameSize: 100,
        parts: maxFiles + 64,
      },
    }).any();

    upload(req, res, (error?: unknown) => {
      if (error) {
        const code = (error as { code?: string }).code;
        if (code === 'LIMIT_FILE_SIZE' || code === 'REQUEST_TOO_LARGE') {
          res.status(413).json({
            error: `Level package is larger than the ${options.maxBytes} byte limit`,
          });
          return;
        }
        if (code === 'LIMIT_FILE_COUNT') {
          res.status(400).json({
            error: `A level upload carries at most ${maxFiles} file part(s)`,
          });
          return;
        }
        res.status(400).json({ error: 'Invalid upload' });
        return;
      }
      next();
    });
  };
}
