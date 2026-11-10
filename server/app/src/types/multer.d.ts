// Minimal typing for the part of multer kan!'s server uses (text fields only);
// multer ships no types and @types/multer would add a dependency for one call.
declare module 'multer' {
  import { RequestHandler } from 'express';
  interface Limits {
    fields?: number;
    fieldSize?: number;
    fieldNameSize?: number;
    files?: number;
    parts?: number;
  }
  interface Multer {
    none(): RequestHandler;
  }
  function multer(options?: { limits?: Limits }): Multer;
  export default multer;
}
