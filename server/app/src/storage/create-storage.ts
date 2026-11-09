import { AppConfig } from '../config';
import { LocalStorage } from './local-storage';
import { S3Storage } from './s3-storage';
import { Storage } from './storage';

/** Builds the configured storage driver. */
export function createStorage(config: AppConfig): Storage {
  return config.storage.driver === 's3'
    ? new S3Storage(config.storage.s3)
    : new LocalStorage(config.storage.localDir);
}
