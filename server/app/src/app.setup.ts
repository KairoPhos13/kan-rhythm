/**
 * Shared HTTP application setup used by `main.ts` and the e2e suite: body
 * parsing with size limits, the query parser the client's `cursor[key]`
 * parameters need, and the error filter that keeps stack traces out of
 * responses. CORS stays disabled on purpose (the game client is not a browser),
 * except for the website's sign-up form on `POST /users` (website-cors.ts).
 */
import { INestApplication } from '@nestjs/common';
import { json, urlencoded } from 'express';
import { AllExceptionsFilter } from './common/http';
import {
  multipartFields,
  multipartUpload,
  SUBMISSION_PATH_PREFIX,
} from './common/multipart';
import { websiteCors } from './common/website-cors';
import { loadConfig } from './config';

export function configureApp(app: INestApplication): void {
  const express = app.getHttpAdapter().getInstance();
  // Express 5 defaults to the "simple" query parser; the lazer client's
  // `cursor[key]=value` parameters parse into nested objects with qs.
  express.set('query parser', 'extended');
  express.disable('x-powered-by');

  const config = loadConfig();
  app.use(websiteCors(config.websiteUrl));
  app.use(json({ limit: '256kb' }));
  app.use(urlencoded({ extended: true, limit: '64kb' }));
  // The game posts its forms as multipart/form-data (text fields only), and
  // its level packages as multipart with files (src/common/multipart.ts).
  app.use(multipartFields());
  app.use(
    SUBMISSION_PATH_PREFIX,
    multipartUpload({ maxBytes: config.submission.maxPackageBytes }),
  );
  app.useGlobalFilters(new AllExceptionsFilter());
}
