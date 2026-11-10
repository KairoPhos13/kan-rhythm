/**
 * Ops health endpoint (not part of the lazer protocol: the game client never
 * calls it). It exists for deployment checks ("Check it works" in
 * deploy/DEPLOY.md) and exercises absolute URL building behind a tunnel.
 * A browser (one that prefers HTML) is sent to kan!'s website instead, when
 * `WEBSITE_URL` is set: people who type the API address find the game page.
 */
import { Controller, Get, Inject, Req, Res } from '@nestjs/common';
import { Request, Response } from 'express';
import { APP_CONFIG, AppConfig } from '../config';
import { absoluteUrl, publicBaseUrl } from './url';

@Controller()
export class HealthController {
  constructor(@Inject(APP_CONFIG) private readonly config: AppConfig) {}

  @Get()
  health(
    @Req() request: Request,
    @Res({ passthrough: true }) response: Response,
  ): Record<string, unknown> | undefined {
    if (this.config.websiteUrl !== '' && request.accepts(['json', 'html']) === 'html') {
      response.redirect(302, this.config.websiteUrl);
      return undefined;
    }
    const base = publicBaseUrl(request, this.config.trustProxy);
    return {
      name: 'kan-server',
      api: absoluteUrl(base, 'api/v2'),
    };
  }
}
