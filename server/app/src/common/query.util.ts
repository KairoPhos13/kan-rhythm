/**
 * Helpers for the query strings the osu!lazer client sends.
 *
 * Depending on the Express query parser the same parameter arrives either
 * nested (`cursor[_id]` → `{ cursor: { _id } }`, `mods[]` → `{ mods: [...] }`)
 * or flat (`{ 'cursor[_id]': ... }`, `{ 'mods[]': ... }`). Both shapes are
 * accepted here so behaviour does not depend on parser configuration.
 */

export type QueryParams = Record<string, unknown>;

function flatten(value: unknown): string | undefined {
  if (Array.isArray(value)) return flatten(value[0]);
  if (value == null) return undefined;
  if (typeof value === 'object') return undefined;
  return String(value);
}

/** Single query parameter value by name. */
export function queryValue(query: QueryParams, name: string): string | undefined {
  return flatten(query[name]);
}

/** Query parameter that may repeat (`mods[]`, `ids[]`), returned as strings. */
export function queryArray(query: QueryParams, name: string): string[] {
  const values: string[] = [];
  const push = (value: unknown): void => {
    if (Array.isArray(value)) {
      for (const item of value) push(item);
      return;
    }
    if (value == null || typeof value === 'object') return;
    values.push(String(value));
  };
  push(query[name]);
  push(query[`${name}[]`]);
  return values;
}

/**
 * Reconstructs the cursor object the client echoes back as `cursor[key]=value`
 * query parameters. The cursor is opaque to the client
 * (`lazer-protocol.md` §4.1).
 */
export function extractCursor(query: QueryParams): Record<string, string> {
  const cursor: Record<string, string> = {};

  const nested = query['cursor'];
  if (nested && typeof nested === 'object' && !Array.isArray(nested)) {
    for (const [key, value] of Object.entries(nested as QueryParams)) {
      const flat = flatten(value);
      if (flat != null) cursor[key] = flat;
    }
  }

  for (const [key, value] of Object.entries(query)) {
    const match = /^cursor\[(.+)\]$/.exec(key);
    if (!match) continue;
    const flat = flatten(value);
    if (flat != null) cursor[match[1]] = flat;
  }

  return cursor;
}

/** Integer parameter with a default and bounds. */
export function queryInt(query: QueryParams, name: string, fallback: number, min: number, max: number): number {
  const raw = queryValue(query, name);
  const parsed = Number.parseInt(raw ?? '', 10);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}
