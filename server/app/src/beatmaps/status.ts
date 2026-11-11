/**
 * `BeatmapOnlineStatus` values as the client reads them (lazer-protocol.md §7):
 * -4 LocallyModified, -3 None, -2 Graveyard, -1 WIP, 0 Pending, 1 Ranked,
 * 2 Approved, 3 Qualified, 4 Loved.
 *
 * kan!'s admin CLI (src/admin) moves levels between these statuses; the
 * catalogue, the profile and the leaderboards read them here.
 */

/** Statuses that grant a public leaderboard (osu!: `status > Pending`). */
export function grantsLeaderboard(status: number): boolean {
  return status >= 1;
}

/**
 * Statuses that carry a `ranked_date` (the date shown as "ranked"): ranked,
 * approved and loved.
 */
export function hasRankedDate(status: number): boolean {
  return status === 1 || status === 2 || status === 4;
}

/** Display name for the admin CLI and its logs. */
export function statusName(status: number): string {
  switch (status) {
    case -2:
      return 'graveyard';
    case -1:
      return 'wip';
    case 0:
      return 'pending';
    case 1:
      return 'ranked';
    case 2:
      return 'approved';
    case 3:
      return 'qualified';
    case 4:
      return 'loved';
    default:
      return `status ${status}`;
  }
}
