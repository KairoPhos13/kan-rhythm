/** Unit tests for the beatmapset search query builder (§4.1 paging/filters). */
import { buildSearchQuery, splitSort } from '../src/beatmaps/search-query';

const base = {
  category: 'any',
  sort: 'ranked_desc',
  offset: 0,
  limit: 20,
};

describe('buildSearchQuery', () => {
  it('falls back to ranked_desc ordering and passes limit/offset last', () => {
    const built = buildSearchQuery(base);
    expect(built.text).toContain('ranked_date DESC NULLS LAST, id DESC');
    expect(built.params).toEqual([20, 0]);
  });

  it('sorts by title ascending when asked', () => {
    const built = buildSearchQuery({ ...base, sort: 'title_asc' });
    expect(built.text).toContain('lower(title) ASC NULLS LAST, id DESC');
    expect(splitSort('artist_desc')).toEqual(['artist', 'desc']);
    expect(splitSort('bogus')).toEqual(['ranked', 'desc']);
  });

  it('applies the free-text filter across the metadata columns', () => {
    const built = buildSearchQuery({ ...base, query: 'hello' });
    expect(built.text).toContain('title ILIKE $1');
    expect(built.text).toContain('tags ILIKE $1');
    expect(built.params[0]).toBe('%hello%');
  });

  it('filters by mode, genre and language', () => {
    const built = buildSearchQuery({ ...base, mode: 0, genre: 2, language: 3 });
    expect(built.text).toContain('b.mode_int = $1');
    expect(built.text).toContain('genre_id = $2');
    expect(built.text).toContain('language_id = $3');
    expect(built.params.slice(0, 3)).toEqual([0, 2, 3]);
  });

  it('scopes "mine" to the requesting user and empties "favourites"', () => {
    const mine = buildSearchQuery({ ...base, category: 'mine', ownerUserId: 12 });
    expect(mine.text).toContain('user_id = $1');
    expect(mine.params[0]).toBe(12);

    const favourites = buildSearchQuery({ ...base, category: 'favourites' });
    expect(favourites.text).toContain('FALSE');
  });

  it('maps category names to status filters', () => {
    expect(buildSearchQuery({ ...base, category: 'ranked' }).text).toContain('status = 1');
    expect(buildSearchQuery({ ...base, category: 'loved' }).text).toContain('status = 4');
    // unknown categories fall back to the default one
    expect(buildSearchQuery({ ...base, category: 'unknown-category' }).text).toContain(
      'status IN (1, 2, 4)',
    );
  });

  it('keeps pending and WIP levels out of the default categories', () => {
    for (const category of ['any', 'leaderboard']) {
      expect(buildSearchQuery({ ...base, category }).text).toContain('status IN (1, 2, 4)');
      expect(buildSearchQuery({ ...base, category }).text).not.toContain('status IN (0, -1)');
    }
    // the client labels its pending category "Pending & WIP" (SearchCategory.cs)
    expect(buildSearchQuery({ ...base, category: 'pending' }).text).toContain('status IN (0, -1)');
    expect(buildSearchQuery({ ...base, category: 'wip' }).text).toContain('status = -1');
    expect(buildSearchQuery({ ...base, category: 'graveyard' }).text).toContain('status = -2');
  });

  it('hides sets whose package has not landed yet', () => {
    expect(buildSearchQuery(base).text).toContain('EXISTS (SELECT 1 FROM beatmaps listed');
  });

  it('puts the cursor offset into OFFSET', () => {
    const built = buildSearchQuery({ ...base, offset: 40 });
    expect(built.text).toContain('LIMIT $1 OFFSET $2');
    expect(built.params).toEqual([20, 40]);
  });
});
