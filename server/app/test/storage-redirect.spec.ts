/**
 * Unit test for the download redirect path: with the `s3` driver the API must
 * answer 302 to a presigned URL (server/docs/mvp-scope.md §6). The presigner
 * is mocked, so no network or real bucket is involved.
 */
import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import request from 'supertest';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { BeatmapsController } from '../src/beatmaps/beatmaps.controller';
import { BeatmapsService } from '../src/beatmaps/beatmaps.service';
import { AuthService } from '../src/auth/auth.service';
import { AuthGuard } from '../src/auth/auth.guard';
import { APP_CONFIG, AppConfig } from '../src/config';
import { configureApp } from '../src/app.setup';
import { STORAGE } from '../src/storage/storage';
import { S3Storage } from '../src/storage/s3-storage';

jest.mock('@aws-sdk/s3-request-presigner', () => ({
  getSignedUrl: jest.fn(),
}));

const setRow = {
  id: 1,
  user_id: 1,
  title: 'Fixture Song',
  title_unicode: 'Fixture Song',
  artist: 'Fixture Artist',
  artist_unicode: 'Fixture Artist',
  creator: 'KairoPhos',
  source: '',
  tags: '',
  status: 1,
  bpm: 120,
  genre_id: 1,
  language_id: 1,
  nsfw: false,
  video: false,
  storyboard: false,
  play_count: 0,
  favourite_count: 0,
  submitted_date: new Date(),
  ranked_date: new Date(),
  last_updated: new Date(),
  cover_key: null,
  archive_key: 'beatmapsets/1.osz',
};

describe('download endpoint with the s3 driver', () => {
  let app: INestApplication;

  beforeAll(async () => {
    (getSignedUrl as jest.Mock).mockResolvedValue('https://objects.example/signed-url');

    const config: AppConfig = {
      port: 3000,
      databaseUrl: '',
      trustProxy: 'none',
      websiteUrl: '',
      oauth: {
        clientId: 'kan-client',
        clientSecret: 'secret',
        accessTokenTtlSeconds: 86400,
        refreshTokenTtlSeconds: 2592000,
        scoreTokenTtlSeconds: 1800,
      },
      storage: {
        driver: 's3',
        localDir: '/tmp',
        s3: {
          endpoint: '',
          region: 'auto',
          bucket: 'kan-bucket',
          accessKeyId: 'key',
          secretAccessKey: 'secret',
          forcePathStyle: true,
        },
        downloadUrlExpirySeconds: 300,
      },
    };

    const moduleRef = await Test.createTestingModule({
      controllers: [BeatmapsController],
      providers: [
        AuthGuard,
        {
          provide: BeatmapsService,
          useValue: {
            getSetRow: jest.fn().mockResolvedValue(setRow),
          },
        },
        {
          provide: AuthService,
          useValue: {
            authenticate: jest.fn().mockResolvedValue({ id: 1, username: 'tester' }),
          },
        },
        {
          provide: STORAGE,
          useValue: new S3Storage(config.storage.s3),
        },
        { provide: APP_CONFIG, useValue: config },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    configureApp(app);
    await app.init();
  });

  afterAll(async () => {
    await app.close();
  });

  it('redirects to a presigned URL with a short expiry', async () => {
    const response = await request(app.getHttpServer())
      .get('/api/v2/beatmapsets/1/download')
      .set('Authorization', 'Bearer whatever')
      .redirects(0);

    expect(response.status).toBe(302);
    expect(response.headers.location).toBe('https://objects.example/signed-url');
    expect(getSignedUrl).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ input: expect.objectContaining({ Bucket: 'kan-bucket', Key: 'beatmapsets/1.osz' }) }),
      { expiresIn: 300 },
    );
  });
});
