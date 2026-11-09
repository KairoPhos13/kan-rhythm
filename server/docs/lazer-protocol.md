# osu!lazer client protocol inventory (kan! server MVP)

Status: implementation reference. Written clean-room from the **client's MIT source** per
ADR-0012; no osu-web (AGPL-3.0) code or text was consulted. The client code is the spec.

Source roots referenced below (repo-relative paths):

- `game/client/` — the osu!lazer fork (`osu.Game`), read-only.
- `game/ruleset/` — the kan! ruleset (`Kan.Ruleset`).

Scope: the requests that matter for the four MVP goals — (1) register + log in from inside the
game, (2) browse/search/download levels in the beatmap listing overlay, (3) submit scores and see
per-level leaderboards, (4) basic user profile. Chat/notifications/websocket/SignalR behaviour is
documented as **client behaviour only** (what happens without it); hubs are not to be implemented.

All line numbers were checked against the client sources in this worktree.

---

## 0. Transport conventions (apply to every request)

| Aspect | Behaviour | Evidence |
|---|---|---|
| API base | `{APIUrl}/api/v2/{Target}` | `game/client/osu.Game/Online/API/APIRequest.cs:75` |
| Token endpoint | `POST {APIUrl}/oauth/token` (not under `/api/v2`) | `game/client/osu.Game/Online/API/OAuth.cs:48-49` |
| Registration endpoint | `POST {APIUrl}/users` (not under `/api/v2`) | `game/client/osu.Game/Online/API/APIAccess.cs:506-513` |
| Method | GET unless a request class sets `req.Method` | e.g. `CreateSoloScoreRequest.cs:29` |
| Headers sent | `Accept-Language` (culture code), `x-api-version` (int), `Authorization: Bearer {token}` when a token exists | `game/client/osu.Game/Online/API/APIRequest.cs:137-141` |
| User agent | `kan!` | `game/client/osu.Game/Online/API/OsuJsonWebRequest.cs:19` |
| Request params | `AddParameter` on POST/PUT/PATCH/DELETE = **form body** (`application/x-www-form-urlencoded`); on GET = query string. Some requests use `AddRaw` + `ContentType: application/json` | `game/client/osu.Game/Online/Solo/CreateSoloScoreRequest.cs:30-32`, `game/client/osu.Game/Online/Rooms/SubmitScoreRequest.cs:29-36` |
| JSON | Newtonsoft. Unknown response fields are ignored; missing fields keep C# defaults. Deserialisation of a typed response that yields `null` is treated as request failure | `game/client/osu.Game/Online/API/APIRequest.cs:94-106` |
| Error body (any API request) | `{"error": "<displayable message>"}` — decoded on failure into `APIException` | `game/client/osu.Game/Online/API/APIRequest.cs:251-255`, `:216-231` |
| Error body (token endpoint) | `{"error": "...", "hint": "...", "message": "..."}` — user sees `hint` if set, else `error` | `game/client/osu.Game/Online/API/OAuth.cs:217-229` |
| 401 semantics | Any HTTP 401 (`WebException` "Unauthorized") logs the user out. There is **no** retry-with-refresh on 401; the refresh token is only used proactively when the local access token is expired | `game/client/osu.Game/Online/API/APIAccess.cs:619-643`, `game/client/osu.Game/Online/API/OAuth.cs:127-138` |
| Guest access | `Authorization` header is omitted when no token; search/lookup/downloads work logged out (download UI is hidden for guests but the endpoint is the same) | `game/client/osu.Game/Online/API/APIRequest.cs:139-141`, `game/client/osu.Game/Overlays/BeatmapSet/Buttons/HeaderDownloadButton.cs:170` |
| Offline mode | With an empty `APIUrl` (current `KanEndpointConfiguration` default) the client makes **zero** network calls | `game/client/osu.Game/Online/KanEndpointConfiguration.cs:14-28`, `game/client/osu.Game/Online/API/APIAccess.cs:73-75,198-203` |

Client-side request gating: while `APIState.Offline`, every queued request fails immediately with
`WebException("User not logged in")` (`APIAccess.cs:659-673`).

---

## 1. OAuth token — `POST /oauth/token`

Client class: `game/client/osu.Game/Online/API/OAuth.cs` (`AccessTokenRequest*`, `:160-215`),
response model `game/client/osu.Game/Online/API/OAuthToken.cs`.

Auth needed: none (the client credentials are in the body). Body is form-encoded.

| Form field | Password grant | Refresh grant | Notes |
|---|---|---|---|
| `grant_type` | `password` (`OAuth.cs:187`) | `refresh_token` (`OAuth.cs:167`) | |
| `client_id` | yes (`OAuth.cs:208`) | yes | from `KanEndpointConfiguration.APIClientID` |
| `client_secret` | yes (`OAuth.cs:209`) | yes | from `KanEndpointConfiguration.APIClientSecret` |
| `scope` | `*` (`OAuth.cs:211`) | `*` | literal `*` |
| `username` | yes (`OAuth.cs:193`) | — | |
| `password` | yes (`OAuth.cs:194`) | — | |
| `refresh_token` | — | yes (`OAuth.cs:172`) | |

Response fields the client reads (`OAuthToken.cs`):

| JSON field | Required | Notes |
|---|---|---|
| `access_token` | **required** | `:18-19` |
| `expires_in` | **required** | seconds; client converts to an absolute expiry (`:21-26`). `IsValid` requires > 30 s remaining (`:28`), so anything ≤ 30 s makes the client immediately re-authenticate on every request |
| `refresh_token` | **required** | `:35-36`; persisted client-side in `"{access_token}|{expiry_unix}|{refresh_token}"` (`:38-57`) |

Notes for the server:

- Expiry policy can be simple (e.g. access 24 h, refresh 30 d rotated on use) — the client handles
  refresh proactively (`OAuth.cs:127-138`); on non-network refresh failure it clears the token and
  the user must re-enter credentials (`OAuth.cs:114-119`).
- Wrong password must not look like a server error: return 4xx with `{"error": "...", "hint": "..."}`
  (`OAuth.cs:60-78`). During login any such failure ends the login attempt and returns the client to
  `Offline` (`APIAccess.cs:360-368`).

## 2. Registration — `POST /users`

Client class: `game/client/osu.Game/Online/API/RegistrationRequest.cs`; call site
`game/client/osu.Game/Online/API/APIAccess.cs:502-556` (`CreateAccount`), UI
`game/client/osu.Game/Overlays/AccountCreation/ScreenEntry.cs:187`.

Auth needed: none. Method POST, form-encoded (`RegistrationRequest.cs:17-19`), header
`Accept: application/json` (`:21`).

| Form field | Notes |
|---|---|
| `user[username]` | |
| `user[user_email]` | |
| `user[password]` | |

Response handling:

- **Success** = the request completes without exception (any 2xx). `CreateAccount` returns `null`
  (`APIAccess.cs:555`) and the account screen proceeds to normal login via `/oauth/token`. The body
  is not read on success.
- **Validation failure** (any 4xx/other): the client parses the body as
  `{"form_error": {"user": {"username": ["..."], "user_email": ["..."], "password": ["..."]}}}`
  (`APIAccess.cs:523` → `RegistrationRequest.RegistrationRequestErrors`, `RegistrationRequest.cs:26-51`).
  All three arrays exist in the model with `Array.Empty<string>()` defaults; `Message` and
  `Redirect` sit next to `User` at the `form_error` level.
- Fallback shape: `{"url": "...", "error": "..."}` maps to `Redirect` / `Message`
  (`APIAccess.cs:532-542`). Anything unparseable rethrows and the UI shows a generic error.

Server must: validate username/email/password and return the `form_error` shape on failure
(never a stack trace), rate-limit per IP (client has no retry logic — a 429 simply shows as an
error).

## 3. Users and profile

### 3.1 `GET /api/v2/me/{ruleset}` — current user

Client class `game/client/osu.Game/Online/API/Requests/GetMeRequest.cs` (`Target => @"me/{Ruleset?.ShortName}"`, `:22`);
login call is `new GetMeRequest()` (no ruleset) → route **`GET /api/v2/me/`** (trailing slash),
`game/client/osu.Game/Online/API/APIAccess.cs:415`. Response model `APIMe` (`game/client/osu.Game/Online/API/Requests/Responses/APIMe.cs`).

| JSON field | Required | Notes |
|---|---|---|
| *(all `APIUser` fields, see §3.3)* | `id`, `username` needed | |
| `session_verification_method` | optional | `APIMe.cs:12-13`. Values `"totp"` / `"mail"` (`:22-31`). **Omit (or null) for MVP** — a value puts the client into 2FA state (`APIAccess.cs:443`) which then expects `POST /api/v2/session/verify` (`game/client/osu.Game/Online/API/Requests/VerifySessionRequest.cs:35,40`) |
| `score_processing_notice_url` | optional | `APIMe.cs:18-19`; empty string default is fine |

Failure handling at login (`APIAccess.cs:417-433`): status < 500 → `Logout()`; "Unauthorized" →
"Login no longer valid" + `Logout()`; anything else (5xx/network) → `APIState.Failing` and the loop
retries. So **a transient 401 kills the session** — the server must only 401 for genuinely invalid
tokens.

The server should accept both `/api/v2/me`, `/api/v2/me/` and `/api/v2/me/{mode}` (mode = ruleset
short name, e.g. `kan`) and return the same `APIUser`/`APIMe` shape (per-mode stats may be omitted
for MVP; see §3.3).

### 3.2 `GET /api/v2/users/{lookup}/{ruleset}?key=id|username` — get user

Client class `game/client/osu.Game/Online/API/Requests/GetUserRequest.cs:63`
(`users/{Lookup}/{Ruleset?.ShortName}?key={id|username}`), lookup is either a numeric id or a
username. With no ruleset the route is `users/42/?key=id`. Used by the profile overlay
(`game/client/osu.Game/Overlays/UserProfileOverlay.cs:133`) and by per-ruleset statistics
(`game/client/osu.Game/Online/LocalUserStatisticsProvider.cs:80`).

Response: `APIUser` (§3.3). Unknown user should be 404 with `{"error": "..."}`.

### 3.3 `APIUser` JSON fields

Model: `game/client/osu.Game/Online/API/Requests/Responses/APIUser.cs`. Newtonsoft ignores missing
fields, so "required" below = the client misbehaves (throws / empty UI) without it; everything else
may be omitted for MVP.

| JSON field | Line | MVP need |
|---|---|---|
| `id` | `:26-27` | **required** |
| `username` | `:32-33` | **required** |
| `join_date` | `:29` | displayed on profile header |
| `country_code` | `:51` | **required** for leaderboard rows/profile flags (e.g. `game/client/osu.Game/Screens/Select/BeatmapLeaderboardScore.cs:280`) |
| `avatar_url` | `:67` | leaderboard/profile avatar (`BeatmapLeaderboardScore.cs:228`); may be empty (falls back to placeholder) |
| `cover_url`, `cover` (`{custom_url,url,id}`) | `:70`, `:77-91` | profile cover |
| `is_supporter`, `support_level` | `:98`, `:101` | gates supporter-only search filters client-side (`game/client/osu.Game/Overlays/BeatmapListing/BeatmapListingFilterControl.cs:258-275`) |
| `is_bot` | `:113-114` | display |
| `is_online`, `last_visit` | `:124`, `:142` | display |
| `title`, `profile_colour` | `:136`, `:64` | display |
| `playmode` | `:210` | display |
| `statistics` | `:239` | profile stats block (`UserStatistics`) |
| `global_rank` (`{rank, ruleset_id}`) | `:254-263` | profile |
| `rank_history` | `:267` | profile graph |
| `statistics_rulesets` (dict) | `:293-295` | profile per-mode tabs |
| `team`, `groups`, `badges`, `user_achievements`, `kudosu`, counts (`scores_best_count` `:187`, `scores_recent_count` `:193`, `post_count` `:154`, `favourite_beatmapset_count` `:166`, `ranked_beatmapset_count` `:175`, …) | various | all optional for MVP; display-only |

### 3.4 `GET /api/v2/users/{id}/scores/{type}` — user scores (profile "best"/"recent")

Client class `game/client/osu.Game/Online/API/Requests/GetUserScoresRequest.cs:37`
(`users/{userId}/scores/{type}`), consumed by the profile Ranks section
(`game/client/osu.Game/Overlays/Profile/Sections/Ranks/PaginatedScoreContainer.cs:64`).

- `type` ∈ `best | firsts | recent | pinned` (`ScoreType`, `GetUserScoresRequest.cs:40-46`)
- Query params: `mode` = ruleset short name (`GetUserScoresRequest.cs:31-34`), `offset`, `limit`
  (every paginated request sends both: `game/client/osu.Game/Online/API/Requests/PaginatedAPIRequest.cs:22-23`)
- Response: a **top-level JSON array** of `SoloScoreInfo` (§5.3), each item carrying nested
  `beatmap` and `beatmapset` (the profile row dereferences `Score.Beatmap` unconditionally,
  `game/client/osu.Game/Overlays/Profile/Sections/Ranks/DrawableProfileScore.cs:91,101`).
  `ruleset_id` in each item must resolve to a locally installed ruleset or the row throws
  (`DrawableProfileScore.cs:55`).

## 4. Beatmaps

### 4.1 `GET /api/v2/beatmapsets/search` — beatmapset search

Client class `game/client/osu.Game/Online/API/Requests/SearchBeatmapSetsRequest.cs`
(`Target => @"beatmapsets/search"`, `:120`; params built `:80-118`). All GET query parameters:

| Param | Sent when | Value | Meaning / enum |
|---|---|---|---|
| `q` | `query != null` | free text | supports `keyword="term"` quoting (`game/client/osu.Game/Overlays/BeatmapListingOverlay.cs:109-110`) |
| `c` | any general filter set | `.`-joined snake_case | `SearchGeneral` (`game/client/osu.Game/Overlays/BeatmapListing/SearchGeneral.cs:10-30`): `recommended`, `converts`, `follows`, `spotlights`, `featured_artists` |
| `m` | ruleset chosen | `ruleset.OnlineID` int | mode int 0=osu, 1=taiko, 2=fruits, 3=mania (see §7); omitted for "Any" |
| `s` | **always** | category, lowercase | `SearchCategory` (`game/client/osu.Game/Overlays/BeatmapListing/SearchCategory.cs:10-44`): `any`, `leaderboard`, `ranked`, `qualified`, `loved`, `favourites`, `pending`, `wip`, `graveyard`, `mine` |
| `g` | genre ≠ Any | int | `SearchGenre` (`game/client/osu.Game/Overlays/BeatmapListing/SearchGenre.cs:10-55`): 0 any, 1 unspecified, 2 video game, 3 anime, 4 rock, 5 pop, 6 other, 7 novelty, 9 hip hop, 10 electronic, 11 metal, 12 classical, 13 folk, 14 jazz (8 unused) |
| `l` | language ≠ Any | int | `SearchLanguage` ordinals (`SearchLanguage.cs:11-72`): 0 any, 1 unspecified, 2 english, 3 japanese, 4 chinese, 5 instrumental, 6 korean, 7 french, 8 german, 9 swedish, 10 spanish, 11 italian, 12 russian, 13 polish, 14 other |
| `sort` | **always** | `{criteria}_{asc\|desc}` | `SortCriteria` (`game/client/osu.Game/Overlays/BeatmapListing/SortCriteria.cs:9-40`): `title`, `artist`, `difficulty`, `updated`, `ranked`, `rating`, `plays`, `favourites`, `relevance`, `nominations`. Example: `ranked_desc` (`SearchBeatmapSetsRequest.cs:47`) |
| `e` | any extra set | `.`-joined | `SearchExtra`: `video`, `storyboard` (`SearchExtra.cs:10-19`) |
| `r` | any rank filter set | `.`-joined | `ScoreRank` values `D.C.B.A.S.SH.X.XH` (F excluded) — see §7 |
| `played` | ≠ Any | `played` / `unplayed` | supporter-only filter; the client enforces this itself and discards results for non-supporters (`BeatmapListingFilterControl.cs:258-275`) |
| `nsfw` | **always** | `true` / `false` | `SearchExplicit.Show` → `true` (`SearchBeatmapSetsRequest.cs:112`) |
| `cursor[{key}]` | next page | one query param per key of the response cursor object | e.g. response `"cursor": {"_id": 123}` → next request `cursor[_id]=123` (`game/client/osu.Game/Extensions/WebRequestExtensions.cs:17-23`, `game/client/osu.Game/Online/API/Requests/Cursor.cs:14-19`) |

> **Note (differs from the brief's guess):** this client generation has **no `cursor_string`**.
> Paging is the `cursor[key]=value` mechanism above. An implementation may still put an opaque
> scalar inside a cursor key (e.g. `{"_id": <opaque>}`) — the cursor object is opaque to the client.

Response (`game/client/osu.Game/Online/API/Requests/SearchBeatmapSetsResponse.cs` +
`ResponseWithCursor.cs`):

| JSON field | Required | Notes |
|---|---|---|
| `beatmapsets` | **required** | `SearchBeatmapSetsResponse.cs:14-15`; `[]` is valid (shows "no results"); `null`/missing crashes the overlay |
| `cursor` | optional | `ResponseWithCursor.cs:12-13`; **omit or `null` on the last page** — a null cursor means "no further pages" (`game/client/osu.Game/Overlays/BeatmapListing/BeatmapListingFilterControl.cs:248-250`) |
| `total` | optional | deserialised (`:17-18`) but never read by the client |

### 4.2 `GET /api/v2/beatmapsets/{id}` and `GET /api/v2/beatmapsets/lookup?beatmap_id={id}`

Client class `game/client/osu.Game/Online/API/Requests/GetBeatmapSetRequest.cs:19` (route chosen by
`BeatmapSetLookupType`). Response `APIBeatmapSet` (§4.5). `beatmaps` must be a non-null array:
`PostProcess` writes backlinks over it (`GetBeatmapSetRequest.cs:21-31`). Used by the set overlay,
favourite button, editor submission and the realm refresh
(`game/client/osu.Game/Screens/Select/RealmPopulatingOnlineLookupSource.cs:71-115`).

### 4.3 `GET /api/v2/beatmaps/lookup?id=|checksum=|filename=` — beatmap lookup

Client class `game/client/osu.Game/Online/API/Requests/GetBeatmapRequest.cs`
(`Target => @"beatmaps/lookup"`, `:43`; params `id` `:34`, `checksum` `:36`, `filename` `:38`).
Response `APIBeatmap` (§4.6) — **must include the nested `beatmapset` object** (see §8.2; also
`game/client/osu.Game/Database/MissingBeatmapNotification.cs:47-52` dereferences `beatmap.BeatmapSet!`).

Matching semantics on the server: prefer `checksum` (MD5 of the `.osu` file) exact match, then
`id`, then `filename` (approximate — the client itself calls the filename fallback approximate,
`game/client/osu.Game/Beatmaps/BeatmapUpdaterMetadataLookup.cs:47-55`). Return 404 + `{"error": ...}`
when nothing matches; the client treats a failed lookup as "beatmap has no online info" and resets
its online fields (`BeatmapUpdaterMetadataLookup.cs:61`).

### 4.4 `GET /api/v2/beatmaps?ids[]=a&ids[]=b…` — batch lookup

Client class `game/client/osu.Game/Online/API/Requests/GetBeatmapsRequest.cs:23` (≤ 50 ids per
request, `:13-18`). Response `{"beatmaps": [APIBeatmap, …]}` (`GetBeatmapsResponse.cs:14-15`).
Used by online-play screens and `BeatmapLookupCache` (`game/client/osu.Game/Database/BeatmapLookupCache.cs:33-35`).

### 4.5 `APIBeatmapSet` JSON fields

Model `game/client/osu.Game/Online/API/Requests/Responses/APIBeatmapSet.cs`. ★ = read by the
listing overlay / set overlay / download flow (i.e. needed for goals 2–3 to look right).

| JSON key | Line | Need |
|---|---|---|
| ★ `id` | `:19-20` | **required** (links, download route, result dedup) |
| ★ `status` | `:22-23` | **required** — `BeatmapOnlineStatus` enum, see §7 |
| ★ `beatmaps` | `:154-155` | **required**, non-null array |
| ★ `covers` | `:16-17` | object; only `cover@2x`, `card@2x`, `list@2x` keys are read (`game/client/osu.Game/Beatmaps/BeatmapSetOnlineCovers.cs:12-23`); may be `{}` / all-empty |
| ★ `title`, `title_unicode`, ★ `artist`, `artist_unicode` | `:73`,`:75-76`,`:78`,`:80-81` | display everywhere |
| ★ `creator`, ★ `user_id`, ★ `user` | `:127`, `:114-119`, `:102-106` | author display ("mapped by"); `user.id == 1` is treated as deleted user (`:105`) |
| ★ `last_updated`, ★ `ranked_date`, ★ `submitted_date` | `:58-59`, `:55-56`, `:52-53` | date stats |
| ★ `bpm`, ★ `play_count`, ★ `favourite_count`, ★ `ratings` (`int[]`) | `:37`, `:31`, `:34`, `:61-62` | card stats (`ratings[0]` is skipped by the UI) |
| ★ `nsfw`, ★ `spotlight`, ★ `video`, ★ `storyboard` | `:40`,`:43`,`:46`,`:49` | badges/pills |
| ★ `availability` | `:134-135` | `{"download_disabled": bool, "more_information": "url"}` (`game/client/osu.Game/Beatmaps/BeatmapSetOnlineAvailability.cs:8-15`) |
| ★ `genre`, ★ `language` | `:137`, `:140` | objects `{"id": int, "name": "..."}` (`game/client/osu.Game/Beatmaps/BeatmapSetOnlineGenre.cs:6-10`) — the UI reads `name` |
| ★ `source`, ★ `tags` | `:149`, `:151-152` | metadata section |
| ★ `has_favourited`, ★ `track_id` | `:28-29`, `:64-65` | favourite button / featured-artist badge |
| `preview_url` | `:25-26` | **ignored** — preview audio is hardcoded to `https://b.ppy.sh/preview/{setid}.mp3` (`game/client/osu.Game/Audio/PreviewTrackManager.cs:114`) |
| `hype`, `nominations_summary`, `current_nominations`, `related_users`, `converts`, `related_tags` | various | optional; fork-only `related_tags` is user-tags |

### 4.6 `APIBeatmap` JSON fields

Model `game/client/osu.Game/Online/API/Requests/Responses/APIBeatmap.cs`.

| JSON key | Line | Need |
|---|---|---|
| ★ `id` | `:16-17` | **required** |
| ★ `beatmapset_id` | `:19-20` | **required** (import attaches online set id) |
| ★ `status` | `:22-23` | **required** (leaderboard gating: scores shown only when `status > Pending`, `game/client/osu.Game/Overlays/BeatmapSet/Scores/ScoresContainer.cs:255`) |
| ★ `checksum` | `:25-26` | **required** — MD5 of the `.osu`; client stores it as `MD5Hash` (`:165`) and later sends it as `beatmap_hash` on score submission |
| ★ `beatmapset` | `:31` | **required** (nested set, at least `status`, `ranked_date`, `submitted_date`) |
| ★ `mode_int` | `:43-44` | 0–3 (§7). **Must stay within 0–3**: `APIBeatmap.APIRuleset.ShortName` throws `ArgumentOutOfRangeException` outside that range (`:188-199`) |
| ★ `difficulty_rating` | `:46-47` | star display/order |
| ★ `version` | `:97-98` | difficulty name |
| ★ `drain`, `cs`, `ar`, `accuracy` | `:53-63` | advanced stats (OD is the `accuracy` key) |
| ★ `total_length` | `:68-73` | seconds |
| ★ `max_combo`, ★ `bpm`, ★ `last_updated`, ★ `user_id` | `:109`, `:115`, `:112-113`, `:28-29` | scores table / stats / import |
| `hit_length`, `count_circles`, `count_sliders`, `count_spinners`, `convert`, `playcount`, `current_user_playcount`, `passcount`, `failtimes` (`{"fail": int[], "exit": int[]}`), `top_tag_ids`, `current_user_tag_ids`, `owners` | various | optional |

### 4.7 `GET /api/v2/beatmapsets/{id}/download[?noVideo=1]` — download

Client class `game/client/osu.Game/Online/API/Requests/DownloadBeatmapSetRequest.cs:28`
(route; `:26` extension `.osz`; `:22` 60 s timeout), transport `game/client/osu.Game/Online/API/APIDownloadRequest.cs:33`
(`FileWebRequest`: response body streamed raw to a temp file; local filename is a client temp name,
not derived from headers). It is a normal API request, so it carries the `Authorization` header.

- Response: raw `.osz` bytes (zip). No JSON wrapper, no `Content-Disposition` requirement.
- `availability.download_disabled: true` disables the buttons client-side
  (`game/client/osu.Game/Beatmaps/BeatmapSetOnlineAvailability.cs:8-15`);
  HTTP failures are shown generically except `WebException("TooManyRequests")` which shows a
  rate-limit notification (`game/client/osu.Game/Database/ModelDownloader.cs:114-118`) — return
  429 when rate-limited.
- The download button is disabled entirely for guest users
  (`game/client/osu.Game/Overlays/BeatmapSet/Buttons/HeaderDownloadButton.cs:170`), so downloads
  may require auth in practice.
- Note: `noVideo=1` is a preference toggle; the MVP server can ignore it (serve the same archive).

---

## 5. Scores

### 5.1 Create score token — `POST /api/v2/beatmaps/{beatmapId}/solo/scores`

Client class `game/client/osu.Game/Online/Solo/CreateSoloScoreRequest.cs`
(`Target => @"beatmaps/{beatmapInfo.OnlineID}/solo/scores"`, `:36`; POST `:29`). Form body
(`:30-32`):

| Form field | Value |
|---|---|
| `version_hash` | client build hash (MD5 of the version string; `game/client/osu.Game/OsuGameBase.cs:273,279`) |
| `beatmap_hash` | local `.osu` MD5 (`BeatmapInfo.MD5Hash`) |
| `ruleset_id` | `RulesetInfo.OnlineID` int — **0 for kan!** (see §8.1) |

Response: `{"id": <long>}` — the only field read (`game/client/osu.Game/Online/Rooms/APIScoreToken.cs:10-11`).
The `{id}` is echoed back in the submit route as `{scoreTokenId}`.

When the client creates a token (`game/client/osu.Game/Screens/Play/SoloPlayer.cs:37-52`): only if
`BeatmapInfo.OnlineID > 0`, `BeatmapInfo.Status != BeatmapOnlineStatus.LocallyModified` (`-4`),
and `Ruleset.IsLegacyRuleset()`; plus general guards (logged in, no non-user-playable mods,
`game/client/osu.Game/Screens/Play/SubmittingPlayer.cs:103-121`). A failed token does **not** stop
gameplay (`SoloPlayer.cs:54`) — the score is simply not submitted.

### 5.2 Submit score — `PUT /api/v2/beatmaps/{beatmapId}/solo/scores/{token}`

Client class `game/client/osu.Game/Online/Solo/SubmitSoloScoreRequest.cs:19` (route), body from
`game/client/osu.Game/Online/Rooms/SubmitScoreRequest.cs:21,29-36`: `Content-Type: application/json`,
body = `SoloScoreInfo.ForSubmission(scoreInfo)` serialised (see §5.3 for what is emitted).

Response: `MultiplayerScore` (`game/client/osu.Game/Online/Rooms/MultiplayerScore.cs`); the client
reads **`id`** and **`position`** (`game/client/osu.Game/Screens/Play/SubmittingPlayer.cs:323-330`). `position` = the score's rank on the leaderboard (1-based), or
null. Other `MultiplayerScore` fields (`user`, `rank`, `total_score`, `accuracy`, `max_combo`,
`mods`, `statistics`, `maximum_statistics`, `passed`, `ended_at`, `pp`, `has_replay`, `ranked`,
`scores_around`, `ruleset_id`, `beatmap_id`, `position`) are available but not read here.

Failure handling: body `{"error": "..."}` is surfaced to the log; known server error strings are
mapped to user text (`SubmittingPlayer.cs:342-362`): `missing token header`, `invalid client hash`,
`invalid verification hash`, `invalid token`, `outdated client`, `invalid or missing beatmap_hash`,
`expired token`. There is no automatic retry. No checksum header exists — **there is no
`Score-Checksum` in this client** (verified by grep over `game/client`).

### 5.3 `SoloScoreInfo` — submission body and score rows

Model `game/client/osu.Game/Online/API/Requests/Responses/SoloScoreInfo.cs`. On **submission** the
client emits (`ForSubmission`, `:263-277`, with `ShouldSerialize*` gating `:134-154`):

| JSON key | Line | Emitted on submit | Read when displaying |
|---|---|---|---|
| `rank` | `:51` | always | yes — `ScoreRank` **string** (`"S"`, `:49-52`; `game/client/osu.Game/Scoring/ScoreRank.cs:9-39`) |
| `total_score`, `total_score_without_mods`, `accuracy`, `max_combo` | `:33-40`,`:46` | always | yes |
| `ruleset_id` | `:24` | always | **required** — must resolve to a locally installed ruleset (`SoloScoreInfo.cs:196`) |
| `passed` | `:30` | always | yes |
| `mods` | `:60-61` | when non-empty | yes — array of `{"acronym": "...", "settings": {...}}` (`game/client/osu.Game/Online/API/APIMod.cs:20-24,48`) |
| `statistics`, `maximum_statistics` | `:75-78` | always (zero entries dropped `:274-275`) | yes — `HitResult` string keys (§7) |
| `pauses` | `:90-91` | always | no |
| `started_at`, `ended_at` | `:54-57` | when set (`:147-148`) | `ended_at` yes (score date) |
| `build_id`, `user_id`, `beatmap_id` | `:27`,`:42`,`:21` | when > 0 / set (`:152-154`) | optional |
| `legacy_total_score`, `legacy_score_id` | `:84-87` | when non-null (`:149-150`) | optional |
| `id` | `:95` | never (`:134`) | yes (set by server in responses) |
| `user` | `:98` | never (`:135`) | **required in leaderboard/profile rows** (`BeatmapLeaderboardScore.cs:228,280,305`) |
| `beatmap` | `:101` | never (`:136`) | **required in profile rows** (`DrawableProfileScore.cs:91,101`) |
| `beatmapset` | `:104` | never (`:137`) | yes — nested set; **must come after `beatmap` in the JSON object** or the setter throws (`:111-112`) |
| `pp` | `:118` | never (`:138`) | displayed only when the beatmap `status` grants pp (Ranked/Approved) |
| `has_replay`, `ranked`, `preserve`, `processed` | `:121-130` | `ranked` yes; others never | `has_replay` toggles the replay button; `ranked` gates pp display |

### 5.4 Leaderboard — `GET /api/v2/beatmaps/{beatmapId}/scores`

Client class `game/client/osu.Game/Online/API/Requests/GetScoresRequest.cs`
(`Target => @"beatmaps/{beatmapInfo.OnlineID}/scores"`, `:46`; params `:52-58`):

| Query param | Value |
|---|---|
| `type` | `global` \| `country` \| `friend` \| `team` (`BeatmapLeaderboardScope` lowercased; `local` is never sent — `:35-36`) |
| `mode` | ruleset **short name**, e.g. `kan` (`:53`) — not an int |
| `mods[]` | one per selected mod, value = mod acronym (e.g. `HD`) |
| `limit` | 50 normally, 100 when the scope/mods require supporter (`:20-21,43`) |

Request guard: `beatmapInfo.OnlineID > 0` (`:32-33`). Fetch is also gated client-side by
`beatmap.OnlineID > 0 && beatmap.Status > BeatmapOnlineStatus.Pending` and by the ruleset being
legacy (`game/client/osu.Game/Online/Leaderboards/LeaderboardManager.cs:96-106`).

Response `APIScoresCollection` (`game/client/osu.Game/Online/API/Requests/Responses/APIScoresCollection.cs:11-21`):

| JSON field | Required | Notes |
|---|---|---|
| `scores` | **required** | array of `SoloScoreInfo` (with nested `user`/`beatmap`/`beatmapset`) |
| `score_count` | recommended | total number of scores (shown as "of N"); the client re-sorts `scores` by total score and assigns positions 1..n (`LeaderboardManager.cs:139-148`) |
| `user_score` | optional | `{"position": int\|null, "score": SoloScoreInfo}` (`game/client/osu.Game/Online/API/Requests/Responses/APIScoreWithPosition.cs:13-26`) — the logged-in user's own best, shown under the list |

MVP semantics: best score per (user, beatmap, ruleset) ordered by `total_score` desc then date asc,
top 50, `user_score` = the requesting user's best (if any). `type=country|friend|team` can fall back
to `global` for MVP.

### 5.5 Replay download — `GET /api/v2/scores/{scoreId}/download`

Client class `game/client/osu.Game/Online/API/Requests/DownloadReplayRequest.cs:15-17` (`.osr`
bytes). Out of MVP scope; the client only shows the button when `has_replay` is true.

---

## 6. Login-time extras, chat/notifications and hubs (client behaviour; not implemented)

### 6.1 HTTP calls around login

| Request | Route | When | Can it block login? |
|---|---|---|---|
| Token | `POST /oauth/token` | `attemptConnect` | yes — failure logs out (`APIAccess.cs:360-368`) |
| Me | `GET /api/v2/me/` | right after token | yes (`APIAccess.cs:415-433`) |
| Friends | `GET /api/v2/friends` | after `SetLocalUser` (`game/client/osu.Game/Online/API/LocalUserState.cs:75-83`) | no — queued, failures logged |
| Blocks | `GET /api/v2/blocks` | same (`LocalUserState.cs:170`) | no |
| Favourites | `GET /api/v2/me/beatmapset-favourites` | same (`LocalUserState.cs:191`) | no |
| Notifications | `GET /api/v2/notifications` | notifications WS (re)connect | no — but its `notification_endpoint` field is the only source of the chat/notifications WS URL (`game/client/osu.Game/Online/API/Requests/Responses/APINotificationsBundle.cs:17-18`) |
| Chat updates | `GET /api/v2/chat/updates?channel=&since=&includes[]=presence` | on WS connect / channel manager load (`game/client/osu.Game/Online/Chat/WebSocketChatClient.cs:41-51`) | no |
| Channels | `GET /api/v2/chat/channels` | after chat presence (`game/client/osu.Game/Online/Chat/ChannelManager.cs:119-120`) | no |
| Messages | `GET /api/v2/chat/channels/{id}/messages` | on channel join (`ChannelManager.cs:495-507`) | no |
| Chat ack | `POST /api/v2/chat/ack` (`since`, `history_since`) | on every state change + every 60 s (`ChannelManager.cs:126,540-544`) | no |
| Seasonal backgrounds | `GET /api/v2/seasonal-backgrounds` | background loader (`game/client/osu.Game/Graphics/Backgrounds/SeasonalBackgroundLoader.cs:57,64`) | no |

Also on startup: the main-menu banner polls a **hardcoded ppy URL** (`assets.ppy.sh`) but only when
`KanEndpointConfiguration.HasServer` is true (`game/client/osu.Game/Screens/Menu/OnlineMenuBanner.cs:70-72,98`);
it stays disabled unless we flip that flag, and it points at ppy infrastructure either way.

### 6.2 SignalR hubs and the notifications websocket

| Connection | URL property (`EndpointConfiguration`) | Notes |
|---|---|---|
| Spectator hub | `SpectatorUrl` (`game/client/osu.Game/Online/EndpointConfiguration.cs:39`) | SignalR, MessagePack preferred (`game/client/osu.Game/Online/HubClientConnector.cs:56-70`) |
| Multiplayer hub | `MultiplayerUrl` (`EndpointConfiguration.cs:44`) | same connection also serves matchmaking |
| Metadata hub | `MetadataUrl` (`EndpointConfiguration.cs:49`) | |
| Notifications WS | from `GET notifications` → `notification_endpoint` | raw JSON WebSocket, `Authorization: Bearer` header (`game/client/osu.Game/Online/Notifications/WebSocket/WebSocketNotificationsClientConnector.cs:30-46`) |
| Chat | **rides the notifications WS** (`chat.start`, `chat.channel.join`, `chat.channel.part`, `chat.message.new` events) | `game/client/osu.Game/Online/Chat/WebSocketChatClient.cs:37-51,101-119` |
| Beatmap submission (editor) | `BeatmapSubmissionServiceUrl` (`EndpointConfiguration.cs:34`) | plain HTTP; disabled when null (`game/client/osu.Game/Screens/Edit/Editor.cs:1370`) |

**What happens when hub URLs are empty or fail** (evidence:
`game/client/osu.Game/Online/PersistentEndpointClientConnector.cs:54-83,129-155`):

- Connectors only connect while `APIState` is `Online`/`RequiresSecondFactorAuth`; `Offline`/`Failing`
  disconnects them.
- There is **no empty-URL guard** in the hub clients: an empty/invalid URL makes the connect attempt
  throw, which is caught and only logged (`PersistentEndpointClientConnector.cs:129-155`), then
  retried with exponential back-off (3 s → 120 s). **Never** an error dialog, never a state change,
  never a login failure. The same applies when the notifications endpoint is missing/invalid
  (`WebSocketNotificationsClientConnector.cs:36` null deref → same back-off loop).
- With the current kan! defaults (all URLs empty, `APIUrl` empty) nothing ever connects at all
  (`APIAccess.cs:198-203`).
- Consequence for the MVP server: implementing **none** of the hubs is safe. The only user-visible
  loss is chat/notifications and multiplayer/spectator. To avoid a permanent background retry
  noise in the network log, hub URLs should stay empty in `KanEndpointConfiguration` (they are).

---

## 7. Enum / value reference

| Enum | Wire values | Source |
|---|---|---|
| Ruleset (mode) ints | 0 `osu`, 1 `taiko`, 2 `fruits`, 3 `mania` | `game/client/osu.Game/Online/API/Requests/Responses/APIBeatmap.cs:188-199` |
| Ruleset short names | `osu`, `taiko`, `fruits`, `mania`, **`kan`** (kan!) | `game/ruleset/KanRuleset.cs:72` |
| `BeatmapOnlineStatus` | `-4` LocallyModified, `-3` None, `-2` Graveyard, `-1` WIP, `0` Pending, `1` Ranked, `2` Approved, `3` Qualified, `4` Loved (JSON string or int; pp-granting = Ranked\|Approved) | `game/client/osu.Game/Beatmaps/BeatmapOnlineStatus.cs:10-47` |
| `ScoreRank` | `-1` F, 0 D, 1 C, 2 B, 3 A, 4 S, 5 SH, 6 X, 7 XH — serialized as strings `"F" "D" "C" "B" "A" "S" "SH" "X" "XH"` | `game/client/osu.Game/Scoring/ScoreRank.cs:9-39` |
| `HitResult` (statistics keys) | strings: `none`, `miss`, `meh`, `ok`, `good`, `great`, `perfect`, `small_tick_miss`, `small_tick_hit`, `large_tick_miss`, `large_tick_hit`, `small_bonus`, `large_bonus`, `ignore_miss`, `ignore_hit`, `combo_break`, `slider_tail_hit`, `legacy_combo_increase` | `game/client/osu.Game/Rulesets/Scoring/HitResult.cs:16-161` |
| `SessionVerificationMethod` | `"totp"`, `"mail"` | `APIMe.cs:22-31` |
| `SearchCategory` / `SearchGenre` / `SearchLanguage` / `SortCriteria` | see §4.1 | |

---

## 8. Open questions for Claude (answered)

### 8.1 Will the client submit scores for a custom ruleset like kan!? Which ruleset id does it send?

**Yes — for kan! as it exists today.** kan! deliberately claims legacy ruleset id 0:
`public class KanRuleset : osu.Game.Rulesets.Ruleset, osu.Game.Rulesets.ILegacyRuleset` with
`public int LegacyID => 0;` (`game/ruleset/KanRuleset.cs:42,53`) and `ShortName => "kan"` (`:72`).
`RulesetInfo.OnlineID` is taken from `ILegacyRuleset.LegacyID`
(`game/client/osu.Game/Rulesets/Ruleset.cs:235-242`), so kan!'s `OnlineID` is **0**. The client's
guard is `IsLegacyRuleset() => ruleset.OnlineID >= 0 && ruleset.OnlineID <= ILegacyRuleset.MAX_LEGACY_RULESET_ID`
with `MAX_LEGACY_RULESET_ID = 3` (`game/client/osu.Game/Extensions/ModelExtensions.cs:76`,
`game/client/osu.Game/Rulesets/ILegacyRuleset.cs:10`) — kan! passes.

Consequences:

- Token creation and submission run (`game/client/osu.Game/Screens/Play/SoloPlayer.cs:48-49` passes;
  guards in `game/client/osu.Game/Screens/Play/SubmittingPlayer.cs:103-121`), sending
  **`ruleset_id = 0`** in the token body (`CreateSoloScoreRequest.cs:32`) — i.e. the same value
  osu!standard would send. The fork removed the stock modes (fork F-9, see the comment at
  `game/ruleset/KanRuleset.cs:44-52`), so 0 is unambiguous **inside the kan! client**, but the
  server must not assume "0 = osu!standard content"; treat `ruleset_id` as "the kan ruleset" for
  MVP, or store it verbatim.
- Leaderboard fetch runs too (`game/client/osu.Game/Online/Leaderboards/LeaderboardManager.cs:96-106`
  passes) and sends **`mode=kan`** (short name) to `GET /api/v2/beatmaps/{id}/scores`
  (`GetScoresRequest.cs:53`). So the leaderboard route must accept the short name `kan` in `mode`.
- If kan! ever moved to `-1` or a `> 3` id, all online scoring would silently switch off:
  `if (!Ruleset.Value.IsLegacyRuleset()) return null;` (`SoloPlayer.cs:48-49`,
  `RoomSubmittingPlayer.cs:42-43`) and `LeaderboardManager.cs:96-100` short-circuits to
  `RulesetUnavailable`. Also `APIBeatmap.APIRuleset.ShortName` throws for mode ints outside 0–3
  (`APIBeatmap.cs:188-199`) — the server must never return `mode_int` outside 0–3.

### 8.2 How does the client match a local level to an online one? What must the server do at upload?

**By `.osu` MD5 checksum lookup after import.** After every import the client runs a metadata
lookup (`game/client/osu.Game/Beatmaps/BeatmapImporter.cs:226-238` →
`BeatmapUpdater.Process` → `game/client/osu.Game/Beatmaps/BeatmapUpdaterMetadataLookup.cs:116-127`)
which performs `GET /api/v2/beatmaps/lookup?checksum={md5}&filename={path}`
(`game/client/osu.Game/Beatmaps/APIBeatmapMetadataSource.cs:36-41`, `GetBeatmapRequest.cs:33-43`).
The ids written inside the `.osu` file are explicitly **not trusted** for this matching
(`BeatmapUpdaterMetadataLookup.cs:47-55`). On success the client attaches `id` → `BeatmapID`,
`beatmapset_id` → `BeatmapSetID`, `checksum` → `MD5Hash`, `status`, `last_updated`, `user_id`,
`beatmapset.status`, `beatmapset.ranked_date`, `beatmapset.submitted_date`
(`APIBeatmapMetadataSource.cs:56-69`) and writes `OnlineID`/`OnlineMD5Hash`/status into the local
database (`BeatmapUpdaterMetadataLookup.cs:56-94`); on failure it resets all online info (`:61`).
Score submission then sends `beatmap_hash = <local .osu MD5>` (`CreateSoloScoreRequest.cs:31`),
which the server must recognise (error `invalid or missing beatmap_hash` otherwise,
`SubmittingPlayer.cs:353-354`).

Therefore at import time the server must:

1. Rewrite `BeatmapID:` / `BeatmapSetID:` into every `.osu` (good hygiene and what the old design
   expects, even though this client re-derives ids from the lookup response),
2. store each rewritten `.osu`'s **MD5 computed after rewriting** as the beatmap checksum — that is
   exactly the value the client will look up and later send as `beatmap_hash`,
3. return those checksums from `beatmaps/lookup`, and
4. serve the download as the same rewritten archive, so checksums match on re-import.

### 8.3 What does the beatmap listing overlay need to show and download levels? Can cover/preview URLs be empty?

**Empty covers are safe; previews are irrelevant; downloads need no media at all.** The overlay
needs `beatmapsets[]` with `id`, `status`, titles/artists, `creator`/`user`, dates, counts, `bpm`,
`nsfw`/`video`/`storyboard`, `availability`, `genre`/`language`, and `beatmaps[]` per difficulty
(§4.5/§4.6). Cover images: only the keys `cover@2x`, `card@2x`, `list@2x` are bound
(`game/client/osu.Game/Beatmaps/BeatmapSetOnlineCovers.cs:12-23` — the low-res `cover`/`card`/`list`
keys are *not* deserialised by this model), and a null/empty URL just leaves a blank sprite
(`game/client/osu.Game/Beatmaps/Drawables/OnlineBeatmapSetCover.cs:47-48`; the search box fades out
on empty cover, `game/client/osu.Game/Overlays/BeatmapListing/BeatmapListingSearchControl.cs:52-64`).
`preview_url` is parsed but unused — previews are hardcoded to
`https://b.ppy.sh/preview/{setid}.mp3` (`game/client/osu.Game/Audio/PreviewTrackManager.cs:114`),
so preview audio will not come from our server either way.

Two client-side caveats worth knowing before deciding on media hosting:

- Online textures (covers, avatars) only load from hosts whose domain ends in **`.ppy.sh`** —
  `TrustedDomainOnlineStore.GetLookupUrl` blocks everything else with a log line
  (`game/client/osu.Game/Online/TrustedDomainOnlineStore.cs:12-21`), wired in via
  `OsuGameBase.CreateOnlineStore()` (`game/client/osu.Game/OsuGameBase.cs:113,297-298`). On a
  self-hosted server, cover/avatar URLs on our own domain will therefore **silently not render**
  in-game (the web page would show them fine). Serving them empty is the honest MVP behaviour; a
  small client change later can widen the trusted-domain check.
- Downloads are not affected: `.osz` bytes flow through the API request path
  (`APIDownloadRequest.cs:33`), not the trusted-domain store.

---

## 9. MVP endpoint checklist (derived from the above)

| Endpoint | Goal | Priority |
|---|---|---|
| `POST /oauth/token` (password + refresh_token) | 1 | must |
| `POST /users` (registration + `form_error` shape) | 1 | must |
| `GET /api/v2/me`, `/me/`, `/me/{mode}` | 1 | must |
| `GET /api/v2/users/{idOrName}/{mode}?key=` | 4 | must |
| `GET /api/v2/users/{id}/scores/{best\|recent}` | 4 | must (profile sections) |
| `GET /api/v2/beatmapsets/search` (+ `cursor[key]` paging) | 2 | must |
| `GET /api/v2/beatmapsets/{id}`, `/beatmapsets/lookup?beatmap_id=` | 2 | must |
| `GET /api/v2/beatmaps/lookup?id\|checksum\|filename` | 2, 3 | must |
| `GET /api/v2/beatmaps?ids[]=` | 2 | should |
| `GET /api/v2/beatmapsets/{id}/download` | 2 | must |
| `POST /api/v2/beatmaps/{id}/solo/scores` (token) | 3 | must |
| `PUT /api/v2/beatmaps/{id}/solo/scores/{token}` (submit) | 3 | must |
| `GET /api/v2/beatmaps/{id}/scores` (leaderboard) | 3 | must |
| `GET /api/v2/scores/{id}/download` (replay) | 3 | later (`has_replay: false`) |
| `GET /api/v2/friends`, `/blocks`, `/me/beatmapset-favourites` | 1 | should (return empty collections; otherwise silent retry noise) |
| `GET /api/v2/chat/*`, `GET /api/v2/notifications`, `GET /api/v2/seasonal-backgrounds` | — | out of MVP (fail silently client-side) |
| SignalR hubs (spectator/multiplayer/metadata) | — | out of MVP (silent back-off client-side) |
| `POST /api/v2/session/verify*` (2FA) | — | out of MVP (omit `session_verification_method`) |
