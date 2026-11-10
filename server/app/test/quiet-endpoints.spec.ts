/**
 * Unit test for the empty realtime-adjacent routes (server/docs/lazer-protocol.md
 * §6.1). kan! runs no notifications, chat or seasonal-background service, so
 * these answer with the exact empty JSON the client parses instead of 404s.
 * Auth is mocked (like test/storage-redirect.spec.ts): no database, no network.
 */
import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import request from 'supertest';
import { QuietController } from '../src/quiet/quiet.controller';
import { AuthService } from '../src/auth/auth.service';
import { AuthGuard } from '../src/auth/auth.guard';
import { configureApp } from '../src/app.setup';

describe('quiet empty routes', () => {
  let app: INestApplication;

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({
      controllers: [QuietController],
      providers: [
        AuthGuard,
        {
          provide: AuthService,
          useValue: {
            // A present bearer token authenticates; anything else is rejected,
            // which is what the real `AuthService.authenticate` does.
            authenticate: jest.fn().mockImplementation((token?: string) =>
              Promise.resolve(token ? { id: 1, username: 'tester' } : null),
            ),
          },
        },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    configureApp(app);
    await app.init();
  });

  afterAll(async () => {
    await app.close();
  });

  it('answers notifications with an empty bundle and no websocket endpoint', async () => {
    const response = await request(app.getHttpServer())
      .get('/api/v2/notifications')
      .set('Authorization', 'Bearer whatever');

    expect(response.status).toBe(200);
    expect(response.body).toEqual({
      notifications: [],
      has_more: false,
      notification_endpoint: '',
    });
  });

  it('answers chat ack with no silences', async () => {
    const response = await request(app.getHttpServer())
      .post('/api/v2/chat/ack')
      .set('Authorization', 'Bearer whatever')
      .field('since', '1')
      .field('history_since', '0');

    expect(response.status).toBe(200);
    expect(response.body).toEqual({ silences: [] });
  });

  it('answers seasonal backgrounds with an empty list and an ended season', async () => {
    const response = await request(app.getHttpServer())
      .get('/api/v2/seasonal-backgrounds')
      .set('Authorization', 'Bearer whatever');

    expect(response.status).toBe(200);
    expect(response.body.backgrounds).toEqual([]);
    // "Already ended": the client parses `ends_at` as a non-nullable
    // DateTimeOffset and an empty list means the season is over anyway.
    expect(new Date(response.body.ends_at).getTime()).toBeLessThan(Date.now());
  });

  it('requires authentication like the other per-user routes', async () => {
    for (const call of [
      request(app.getHttpServer()).get('/api/v2/notifications'),
      request(app.getHttpServer()).post('/api/v2/chat/ack'),
      request(app.getHttpServer()).get('/api/v2/seasonal-backgrounds'),
    ]) {
      const response = await call;
      expect(response.status).toBe(401);
      expect(response.body).toEqual({ error: 'Unauthorized' });
    }
  });
});
