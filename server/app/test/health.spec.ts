/**
 * Unit test for the API root: tools and the deployment checks get the JSON
 * health answer; a browser is redirected to kan!'s website when WEBSITE_URL is
 * set, and gets the JSON when it is not.
 */
import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import request from 'supertest';
import { HealthController } from '../src/common/health.controller';
import { APP_CONFIG, loadConfig } from '../src/config';

const BROWSER_ACCEPT = 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8';

async function appWith(env: NodeJS.ProcessEnv): Promise<INestApplication> {
  const moduleRef = await Test.createTestingModule({
    controllers: [HealthController],
    providers: [{ provide: APP_CONFIG, useValue: loadConfig(env) }],
  }).compile();
  const app = moduleRef.createNestApplication({ logger: false });
  await app.init();
  return app;
}

describe('API root', () => {
  let app: INestApplication;

  afterEach(async () => {
    await app?.close();
  });

  it('answers JSON to tools (curl sends */*)', async () => {
    app = await appWith({ WEBSITE_URL: 'https://kan.example.org', TRUST_PROXY: 'cloudflare' });
    const response = await request(app.getHttpServer()).get('/').set('Host', 'kan-api.example.org');
    expect(response.status).toBe(200);
    expect(response.body).toEqual({ name: 'kan-server', api: 'https://kan-api.example.org/api/v2' });
  });

  it('answers JSON to a client that asks for JSON', async () => {
    app = await appWith({ WEBSITE_URL: 'https://kan.example.org' });
    const response = await request(app.getHttpServer()).get('/').set('Accept', 'application/json');
    expect(response.status).toBe(200);
    expect(response.body.name).toBe('kan-server');
  });

  it('redirects a browser to the website', async () => {
    app = await appWith({ WEBSITE_URL: 'https://kan.example.org' });
    const response = await request(app.getHttpServer()).get('/').set('Accept', BROWSER_ACCEPT);
    expect(response.status).toBe(302);
    expect(response.headers['location']).toBe('https://kan.example.org');
  });

  it('sends the game\'s forgot-password link to the website', async () => {
    app = await appWith({ WEBSITE_URL: 'https://kan.example.org' });
    const response = await request(app.getHttpServer()).get('/home/password-reset');
    expect(response.status).toBe(302);
    expect(response.headers['location']).toBe('https://kan.example.org/#password');
  });

  it('keeps the JSON for a browser when no website is configured', async () => {
    app = await appWith({});
    const response = await request(app.getHttpServer()).get('/').set('Accept', BROWSER_ACCEPT);
    expect(response.status).toBe(200);
    expect(response.body.name).toBe('kan-server');
  });
});
