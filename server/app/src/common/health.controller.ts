/**
 * Ops health endpoint (not part of the lazer protocol: the game client never
 * calls it). It exists for deployment checks ("Check it works" in
 * deploy/DEPLOY.md) and exercises absolute URL building behind a tunnel.
 */
import { Controller, Get, Inject, Req } from '@nestjs/common';
import { Request } from 'express';
import { APP_CONFIG, AppConfig } from '../config';
import { absoluteUrl, publicBaseUrl } from './url';

@Controller()
export class HealthController {
  constructor(@Inject(APP_CONFIG) private readonly config: AppConfig) {}

  @Get()
  health(@Req() request: Request): Record<string, unknown> {
    const base = publicBaseUrl(request, this.config.trustProxy);
    return {
      name: 'kan-server',
      api: absoluteUrl(base, 'api/v2'),
    };
  }
}
