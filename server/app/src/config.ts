/**
 * Runtime configuration, read from the environment (a local `.env` is loaded by
 * dotenv in `main.ts` / the CLIs). No secrets live in the repository.
 */
import { parseTrustProxy, TrustProxyMode } from './common/client-ip';

export type StorageDriverName = 'local' | 's3';

/** Nest injection token for the resolved {@link AppConfig}. */
export const APP_CONFIG = 'APP_CONFIG';

/** Default of `SUBMISSION_MAX_PACKAGE_BYTES`: 100 MiB per level package. */
export const DEFAULT_MAX_PACKAGE_BYTES = 100 * 1024 * 1024;

export interface S3Config {
  endpoint: string;
  region: string;
  bucket: string;
  accessKeyId: string;
  secretAccessKey: string;
  forcePathStyle: boolean;
}

export interface AppConfig {
  port: number;
  databaseUrl: string;
  /**
   * `TRUST_PROXY=cloudflare` when a Cloudflare Tunnel (or another proxy we
   * control) fronts the app: forwarded client IPs are then trusted for rate
   * limiting and absolute URLs use https. Never trusted otherwise.
   */
  trustProxy: TrustProxyMode;
  /**
   * `WEBSITE_URL`: kan!'s public website. A browser that opens the API root is
   * redirected there; empty keeps the plain JSON answer for everyone.
   */
  websiteUrl: string;
  oauth: {
    clientId: string;
    clientSecret: string;
    accessTokenTtlSeconds: number;
    refreshTokenTtlSeconds: number;
    scoreTokenTtlSeconds: number;
  };
  storage: {
    driver: StorageDriverName;
    localDir: string;
    s3: S3Config;
    /** Expiry of the presigned download URLs the download endpoint redirects to. */
    downloadUrlExpirySeconds: number;
  };
  submission: {
    /** Largest accepted level package, in bytes (per upload). */
    maxPackageBytes: number;
    /** Package uploads allowed per creator per hour. */
    uploadsPerHour: number;
    /**
     * Unranked (pending/WIP) sets a creator may hold: `unrankedSetsBase` plus
     * one per ranked set, never more than `unrankedSetsMax` (osu!'s rule).
     */
    unrankedSetsBase: number;
    unrankedSetsMax: number;
  };
}

function int(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(value ?? '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function bool(value: string | undefined, fallback: boolean): boolean {
  if (value == null || value === '') return fallback;
  return value === 'true' || value === '1' || value === 'yes';
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const driver = env.STORAGE_DRIVER === 's3' ? 's3' : 'local';
  return {
    port: int(env.PORT, 3000),
    databaseUrl: env.DATABASE_URL ?? 'postgres://kan:kan@localhost:55432/kan',
    trustProxy: parseTrustProxy(env.TRUST_PROXY),
    websiteUrl: (env.WEBSITE_URL ?? '').trim(),
    oauth: {
      // The game ships these as constants (public by nature: like osu!lazer's
      // client, it is distributed to everyone; the real protection is user
      // passwords and bearer tokens). They must match the client's values.
      clientId: env.OAUTH_CLIENT_ID ?? 'kan-client',
      clientSecret: env.OAUTH_CLIENT_SECRET ?? 'kan-public-client',
      accessTokenTtlSeconds: int(env.ACCESS_TOKEN_TTL_SECONDS, 86400),
      refreshTokenTtlSeconds: int(env.REFRESH_TOKEN_TTL_SECONDS, 2592000),
      scoreTokenTtlSeconds: int(env.SCORE_TOKEN_TTL_SECONDS, 1800),
    },
    storage: {
      driver,
      localDir: env.STORAGE_LOCAL_DIR ?? './data',
      s3: {
        endpoint: env.S3_ENDPOINT ?? '',
        region: env.S3_REGION ?? 'auto',
        bucket: env.S3_BUCKET ?? '',
        accessKeyId: env.S3_ACCESS_KEY_ID ?? '',
        secretAccessKey: env.S3_SECRET_ACCESS_KEY ?? '',
        forcePathStyle: bool(env.S3_FORCE_PATH_STYLE, true),
      },
      downloadUrlExpirySeconds: int(env.DOWNLOAD_URL_EXPIRY_SECONDS, 300),
    },
    submission: {
      maxPackageBytes: int(env.SUBMISSION_MAX_PACKAGE_BYTES, DEFAULT_MAX_PACKAGE_BYTES),
      uploadsPerHour: int(env.SUBMISSION_UPLOADS_PER_HOUR, 10),
      unrankedSetsBase: int(env.SUBMISSION_UNRANKED_SETS_BASE, 4),
      unrankedSetsMax: int(env.SUBMISSION_UNRANKED_SETS_MAX, 8),
    },
  };
}
