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
import { multipartFields } from './common/multipart';
import { websiteCors } from './common/website-cors';
import { loadConfig } from './config';

export function configureApp(app: INestApplication): void {
  const express = app.getHttpAdapter().getInstance();
  // Express 5 defaults to the "simple" query parser; the lazer client's
  // `cursor[key]=value` parameters parse into nested objects with qs.
  express.set('query parser', 'extended');
  express.disable('x-powered-by');

  app.use(websiteCors(loadConfig().websiteUrl));
  app.use(json({ limit: '256kb' }));
  app.use(urlencoded({ extended: true, limit: '64kb' }));
  // The game posts its forms as multipart/form-data (text fields only).
  app.use(multipartFields());
  app.useGlobalFilters(new AllExceptionsFilter());
}
