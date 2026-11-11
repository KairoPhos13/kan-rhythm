/**
 * Unit tests for the expired-row purge (review note): the SQL predicates, the
 * purge method and its hourly, unref()ed scheduling.
 */
import { DbService } from '../src/db/db.service';
import { PurgeResult, PurgeService, PURGE_INTERVAL_MS, PURGE_STATEMENTS } from '../src/db/purge.service';

function fakeDb(results: { rowCount: number }[] = []): { query: jest.Mock } {
  return {
    query: jest
      .fn()
      .mockImplementation(() => Promise.resolve(results.shift() ?? { rowCount: 0 })),
  };
}

describe('purge SQL predicates', () => {
  it('deletes oauth tokens whose refresh token has expired', () => {
    expect(PURGE_STATEMENTS.oauthTokens).toContain('DELETE FROM oauth_tokens');
    expect(PURGE_STATEMENTS.oauthTokens).toContain('refresh_expires_at < now()');
  });

  it('deletes score tokens that are used or expired for more than a day', () => {
    expect(PURGE_STATEMENTS.scoreTokens).toContain('DELETE FROM score_tokens');
    expect(PURGE_STATEMENTS.scoreTokens).toContain('used_at IS NOT NULL');
    expect(PURGE_STATEMENTS.scoreTokens).toContain("expires_at < now() - interval '1 day'");
  });

  it('deletes beatmap ids reserved by a submission that never arrived', () => {
    expect(PURGE_STATEMENTS.beatmapIdReservations).toContain(
      'DELETE FROM beatmap_id_reservations',
    );
    expect(PURGE_STATEMENTS.beatmapIdReservations).toContain(
      "created_at < now() - interval '7 days'",
    );
  });
});

describe('PurgeService.purge', () => {
  it('runs every deletion and reports the row counts', async () => {
    const db = fakeDb([{ rowCount: 3 }, { rowCount: 7 }, { rowCount: 2 }]);
    const service = new PurgeService(db as unknown as DbService);

    const result: PurgeResult = await service.purge();

    expect(db.query).toHaveBeenCalledTimes(3);
    expect(db.query).toHaveBeenNthCalledWith(1, PURGE_STATEMENTS.oauthTokens);
    expect(db.query).toHaveBeenNthCalledWith(2, PURGE_STATEMENTS.scoreTokens);
    expect(db.query).toHaveBeenNthCalledWith(3, PURGE_STATEMENTS.beatmapIdReservations);
    expect(result).toEqual({ oauthTokens: 3, scoreTokens: 7, beatmapIdReservations: 2 });
  });

  it('never throws when the database is unavailable', async () => {
    const db = { query: jest.fn().mockRejectedValue(new Error('connection lost')) };
    const service = new PurgeService(db as unknown as DbService);

    await expect(service.purge()).resolves.toEqual({
      oauthTokens: 0,
      scoreTokens: 0,
      beatmapIdReservations: 0,
    });
  });
});

describe('PurgeService scheduling', () => {
  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  it('purges at start-up and then every hour, and stops after destroy', () => {
    jest.useFakeTimers();
    const service = new PurgeService(fakeDb() as unknown as DbService);
    const purge = jest
      .spyOn(service, 'purge')
      .mockResolvedValue({ oauthTokens: 0, scoreTokens: 0, beatmapIdReservations: 0 });

    service.onModuleInit();
    expect(purge).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(PURGE_INTERVAL_MS);
    expect(purge).toHaveBeenCalledTimes(2);
    jest.advanceTimersByTime(PURGE_INTERVAL_MS * 2);
    expect(purge).toHaveBeenCalledTimes(4);

    service.onModuleDestroy();
    jest.advanceTimersByTime(PURGE_INTERVAL_MS * 3);
    expect(purge).toHaveBeenCalledTimes(4);
  });

  it('schedules on an unref()ed timer and clears it on destroy', () => {
    const unref = jest.fn();
    const fakeTimer = { unref } as unknown as NodeJS.Timeout;
    const setIntervalSpy = jest.spyOn(global, 'setInterval').mockReturnValue(fakeTimer);
    const clearIntervalSpy = jest.spyOn(global, 'clearInterval');

    const service = new PurgeService(fakeDb() as unknown as DbService);
    jest
      .spyOn(service, 'purge')
      .mockResolvedValue({ oauthTokens: 0, scoreTokens: 0, beatmapIdReservations: 0 });

    service.onModuleInit();
    expect(setIntervalSpy).toHaveBeenCalledTimes(1);
    expect(setIntervalSpy.mock.calls[0][1]).toBe(PURGE_INTERVAL_MS);
    expect(unref).toHaveBeenCalledTimes(1);

    service.onModuleDestroy();
    expect(clearIntervalSpy).toHaveBeenCalledWith(fakeTimer);
  });
});
