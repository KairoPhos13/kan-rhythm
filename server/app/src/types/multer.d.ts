// Minimal typing for the part of multer kan!'s server uses (text fields and
// in-memory file uploads); multer ships no types and @types/multer would add a
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
  interface Multer {
    none(): RequestHandler;
    any(): RequestHandler;
  }
  function multer(options?: { limits?: Limits; storage?: unknown }): Multer;
  export default multer;
}
