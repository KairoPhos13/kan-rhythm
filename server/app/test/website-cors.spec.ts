/**
 * Unit test for the website's CORS: only the sign-up route answers the
 * website's origin; other origins and other routes get no CORS headers.
 */
import express from 'express';
import request from 'supertest';
import { websiteCors, websiteOrigin } from '../src/common/website-cors';

const SITE = 'https://kan.example.org';

function app(websiteUrl: string) {
  const server = express();
  server.use(websiteCors(websiteUrl));
  server.post('/users', (_req, res) => res.json({ id: 1 }));
  server.get('/api/v2/me', (_req, res) => res.json({ id: 1 }));
  return server;
}

describe('website CORS', () => {
  it('derives the origin from WEBSITE_URL', () => {
    expect(websiteOrigin('https://kan.example.org/')).toBe(SITE);
    expect(websiteOrigin('')).toBe('');
    expect(websiteOrigin('not a url')).toBe('');
  });

  it('lets the website read the sign-up answer', async () => {
    const response = await request(app(SITE)).post('/users').set('Origin', SITE);
    expect(response.status).toBe(200);
    expect(response.headers['access-control-allow-origin']).toBe(SITE);
  });

  it('answers the preflight for the sign-up route', async () => {
    const response = await request(app(SITE)).options('/users').set('Origin', SITE);
    expect(response.status).toBe(204);
    expect(response.headers['access-control-allow-methods']).toBe('POST');
  });

  it('gives no CORS headers to another origin', async () => {
    const response = await request(app(SITE)).post('/users').set('Origin', 'https://evil.example');
    expect(response.headers['access-control-allow-origin']).toBeUndefined();
  });

  it('gives no CORS headers on other routes, even to the website', async () => {
    const response = await request(app(SITE)).get('/api/v2/me').set('Origin', SITE);
    expect(response.headers['access-control-allow-origin']).toBeUndefined();
  });

  it('is off without a website', async () => {
    const response = await request(app('')).post('/users').set('Origin', SITE);
    expect(response.headers['access-control-allow-origin']).toBeUndefined();
  });
});
