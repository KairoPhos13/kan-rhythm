/**
 * Shared database plumbing: a small `pg` pool wrapper used by the Nest
 * services, the migration runner and the import CLI alike.
 */
import { Inject, Injectable, OnModuleDestroy } from '@nestjs/common';
import { Pool, QueryResult, QueryResultRow } from 'pg';
import { APP_CONFIG, AppConfig } from '../config';

export function createPool(databaseUrl: string): Pool {
  return new Pool({
    connectionString: databaseUrl,
    // Small pool: the target host runs PostgreSQL with max_connections=20.
    max: 10,
    idleTimeoutMillis: 30_000,
    connectionTimeoutMillis: 10_000,
  });
}

@Injectable()
export class DbService implements OnModuleDestroy {
  readonly pool: Pool;

  constructor(@Inject(APP_CONFIG) config: AppConfig) {
    this.pool = createPool(config.databaseUrl);
  }

  query<T extends QueryResultRow = QueryResultRow>(
    text: string,
    params?: unknown[],
  ): Promise<QueryResult<T>> {
    return this.pool.query<T>(text, params);
  }

  async onModuleDestroy(): Promise<void> {
    await this.pool.end();
  }
}
