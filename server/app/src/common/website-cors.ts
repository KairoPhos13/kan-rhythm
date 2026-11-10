/**
 * CORS for kan!'s website, and only for registration: the sign-up form on
 * `WEBSITE_URL` posts to `POST /users` and must read the answer (the new
 * account, or the `form_error` shape). Every other route keeps CORS off: the
 * game client is not a browser, and no other page may call the API.
 */
import { NextFunction, Request, Response } from 'express';

const ROUTE = '/users';

/** The website's origin (`https://kan.example.org`), or '' when unset or invalid. */
export function websiteOrigin(websiteUrl: string): string {
  try {
    return websiteUrl === '' ? '' : new URL(websiteUrl).origin;
  } catch {
    return '';
  }
}

export function websiteCors(websiteUrl: string) {
  const origin = websiteOrigin(websiteUrl);
  return (req: Request, res: Response, next: NextFunction): void => {
    if (origin === '' || req.path !== ROUTE || req.headers.origin !== origin) {
      next();
      return;
    }
    res.setHeader('Access-Control-Allow-Origin', origin);
    res.setHeader('Vary', 'Origin');
    if (req.method === 'OPTIONS') {
      res.setHeader('Access-Control-Allow-Methods', 'POST');
      res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
      res.setHeader('Access-Control-Max-Age', '86400');
      res.status(204).end();
      return;
    }
    next();
  };
}
