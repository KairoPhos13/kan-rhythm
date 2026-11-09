/** S3-compatible storage driver (Cloudflare R2 / AWS S3). */
import {
  DeleteObjectCommand,
  GetObjectCommand,
  HeadObjectCommand,
  PutObjectCommand,
  S3Client,
} from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { S3Config } from '../config';
import { isSafeKey, OpenedFile, Storage } from './storage';

export class S3Storage implements Storage {
  private readonly client: S3Client;

  constructor(private readonly config: S3Config) {
    this.client = new S3Client({
      region: config.region,
      endpoint: config.endpoint || undefined,
      forcePathStyle: config.forcePathStyle,
      credentials:
        config.accessKeyId && config.secretAccessKey
          ? { accessKeyId: config.accessKeyId, secretAccessKey: config.secretAccessKey }
          : undefined,
    });
  }

  private key(key: string): string {
    if (!isSafeKey(key)) throw new Error(`unsafe storage key: ${key}`);
    return key;
  }

  async put(key: string, body: Buffer, contentType: string): Promise<void> {
    await this.client.send(
      new PutObjectCommand({
        Bucket: this.config.bucket,
        Key: this.key(key),
        Body: body,
        ContentType: contentType,
      }),
    );
  }

  async remove(key: string): Promise<void> {
    await this.client.send(
      new DeleteObjectCommand({ Bucket: this.config.bucket, Key: this.key(key) }),
    );
  }

  /**
   * Downloads are answered with a 302 to this URL (the client follows
   * redirects transparently — osu! itself ships .osz archives that way,
   * see server/docs/osu-server-feasibility.md §7).
   */
  presign(key: string, expiresInSeconds: number): Promise<string> {
    return getSignedUrl(
      this.client,
      new GetObjectCommand({ Bucket: this.config.bucket, Key: this.key(key) }),
      { expiresIn: expiresInSeconds },
    );
  }

  open(_key: string): Promise<OpenedFile | null> {
    // The s3 driver always presigns; streaming through the API is not used.
    return Promise.resolve(null);
  }

  async exists(key: string): Promise<boolean> {
    try {
      await this.client.send(new HeadObjectCommand({ Bucket: this.config.bucket, Key: this.key(key) }));
      return true;
    } catch {
      return false;
    }
  }
}
