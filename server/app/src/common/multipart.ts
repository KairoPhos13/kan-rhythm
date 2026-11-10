/**
 * The game's forms arrive as multipart/form-data: osu!framework's WebRequest
 * posts every AddParameter body that way (POST /oauth/token, POST /users).
 * Express parses only JSON and urlencoded, so without this the server saw an
 * empty body ("Unknown OAuth client"). Text fields only: any file part, or a
 * form over the limits, is refused with 400.
 */
import { NextFunction, Request, Response } from 'express';
import multer from 'multer';

const fieldsOnly = multer({
  limits: { fields: 20, fieldSize: 16 * 1024, fieldNameSize: 100, files: 0, parts: 20 },
}).none();

export function multipartFields() {
  return (req: Request, res: Response, next: NextFunction): void => {
    if (!req.is('multipart/form-data')) {
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
