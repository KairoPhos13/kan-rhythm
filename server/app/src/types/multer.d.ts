// Minimal typing for the part of multer kan!'s server uses (text fields and
// streamed file uploads); multer ships no types and @types/multer would add a
// dependency for two calls.
declare module 'multer' {
  import { RequestHandler } from 'express';
  interface Limits {
    fields?: number;
    fieldSize?: number;
    fieldNameSize?: number;
    files?: number;
    parts?: number;
    fileSize?: number;
  }
  /** The storage engine contract kan! implements (streaming to temp files). */
  export interface StorageEngine {
    _handleFile(
      req: unknown,
      file: { fieldname: string; originalname: string; stream: NodeJS.ReadableStream },
      cb: (error: unknown, info?: { path: string; size: number }) => void,
    ): void;
    _removeFile(req: unknown, file: { path?: string }, cb: (error: unknown) => void): void;
  }
  interface Multer {
    none(): RequestHandler;
    any(): RequestHandler;
  }
  function multer(options?: { limits?: Limits; storage?: StorageEngine }): Multer;
  export default multer;
}
