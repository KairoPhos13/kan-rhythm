/**
 * Client routes that kan! answers with empty data (server/docs/lazer-protocol.md
 * §6.1, "implemented-empty"). The osu!lazer client calls notifications, chat ack
 * and seasonal backgrounds at startup and every minute; kan! runs none of these
 * services, so the routes answer with the exact empty JSON the client parses
 * instead of 404s. This keeps the client quiet and its network log clean.
 *
 * Response shapes come from the client's Newtonsoft models:
 * `APINotificationsBundle`, `ChatAckResponse`, `APISeasonalBackgrounds`
 * (game/client/osu.Game/Online/API/Requests/Responses/).
 */
import { Controller, Get, HttpCode, Post, UseGuards } from '@nestjs/common';
import { AuthGuard } from '../auth/auth.guard';

@Controller('api/v2')
export class QuietController {
  /**
   * `GET /api/v2/notifications`: `APINotificationsBundle`. An empty
   * `notification_endpoint` tells the client there is no notifications websocket
   * to connect to (fork F-27), so it settles as "not available" and never
   * retries. The endpoint is `""` rather than `null`: the client's field is a
   * non-nullable `string` and a null would fail deserialisation.
   */
  @Get('notifications')
  @UseGuards(AuthGuard)
  notifications(): Record<string, unknown> {
    return { notifications: [], has_more: false, notification_endpoint: '' };
  }

  /**
   * `POST /api/v2/chat/ack`: `ChatAckResponse`. No silences exist.
   */
  @Post('chat/ack')
  @HttpCode(200)
  @UseGuards(AuthGuard)
  chatAck(): Record<string, unknown> {
    return { silences: [] };
  }

  /**
   * `GET /api/v2/seasonal-backgrounds`: `APISeasonalBackgrounds`, empty. The
   * `ends_at` is a past instant rather than `null`: the client parses it as a
   * non-nullable `DateTimeOffset`, and "already ended" is what an empty list
   * means anyway.
   */
  @Get('seasonal-backgrounds')
  @UseGuards(AuthGuard)
  seasonalBackgrounds(): Record<string, unknown> {
    return { ends_at: '1970-01-01T00:00:00.000Z', backgrounds: [] };
  }
}
