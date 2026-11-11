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
 */
import { NextFunction, Request, Response } from 'express';
import multer from 'multer';

/** The editor's submission routes (`src/beatmaps/submission.controller.ts`). */
export const SUBMISSION_PATH_PREFIX = '/beatmap-submission';

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

/** One uploaded file, kept in memory (level packages are parsed right away). */
export interface UploadedFile {
  /** Form field name: `beatmapArchive` (replace) or `filesChanged` (patch). */
  fieldname: string;
  /** Archive entry name the client uploaded (`PatchBeatmapPackageRequest`). */
  originalname: string;
  buffer: Buffer;
}

/** Files and text fields of a parsed multipart request. */
export interface UploadRequest extends Request {
  files?: UploadedFile[];
}

/**
 * Parses a multipart request with files into `req.files` / `req.body`. Files
 * are buffered in memory: the package is unzipped and checked immediately, and
 * the whole request is bounded by `maxBytes`.
 */
export function multipartUpload(options: {
  maxBytes: number;
  maxFiles?: number;
}): (req: UploadRequest, res: Response, next: NextFunction) => void {
  const upload = multer({
    storage: multerMemoryStorage(),
    limits: {
      fileSize: options.maxBytes,
      files: options.maxFiles ?? 1024,
      fields: 64,
      fieldSize: 64 * 1024,
      fieldNameSize: 100,
      parts: (options.maxFiles ?? 1024) + 64,
    },
  }).any();

  return (req: UploadRequest, res: Response, next: NextFunction): void => {
    if (!req.is('multipart/form-data')) {
      next();
      return;
    }
    upload(req, res, (error?: unknown) => {
      if (error) {
        const code = (error as { code?: string }).code;
        if (code === 'LIMIT_FILE_SIZE') {
          res.status(413).json({
            error: `Level package is larger than the ${options.maxBytes} byte limit`,
          });
          return;
        }
        res.status(400).json({ error: 'Invalid upload' });
        return;
      }
      const files = (req.files as UploadedFile[] | undefined) ?? [];
      const total = files.reduce((sum, file) => sum + file.buffer.length, 0);
      if (total > options.maxBytes) {
        res.status(413).json({
          error: `Level package is larger than the ${options.maxBytes} byte limit`,
        });
        return;
      }
      next();
    });
  };
}

/**
 * multer's in-memory storage. The package types (src/types/multer.d.ts) cover
 * only what the server uses, so the factory is reached through this shim.
 */
function multerMemoryStorage(): unknown {
  return (multer as unknown as { memoryStorage(): unknown }).memoryStorage();
}
