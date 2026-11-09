/**
 * Error handling. The client decodes `{"error": "<displayable message>"}` from
 * any failed API request (`lazer-protocol.md` §0); stack traces must never
 * reach it.
 */
import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
  HttpStatus,
  Logger,
} from '@nestjs/common';
import { Response } from 'express';

/** An API error with the JSON body the client expects: `{"error": "..."}`. */
export function apiError(status: number, message: string): HttpException {
  return new HttpException({ error: message }, status);
}

/**
 * OAuth token endpoint errors (`lazer-protocol.md` §1): the client shows
 * `hint` when set, otherwise `error`.
 */
export function oauthError(status: number, error: string, hint: string): HttpException {
  return new HttpException({ error, hint, message: hint }, status);
}

/**
 * Reads a scalar field from a parsed request body. Form fields arrive as
 * strings; repeated fields arrive as arrays (first value wins).
 */
export function bodyField(body: unknown, name: string): string | undefined {
  if (body == null || typeof body !== 'object') return undefined;
  const value = (body as Record<string, unknown>)[name];
  if (Array.isArray(value)) return bodyField({ [name]: value[0] }, name);
  if (value == null || typeof value === 'object') return undefined;
  return String(value);
}

@Catch()
export class AllExceptionsFilter implements ExceptionFilter {
  private readonly logger = new Logger('http');

  catch(exception: unknown, host: ArgumentsHost): void {
    const response = host.switchToHttp().getResponse<Response>();

    if (exception instanceof HttpException) {
      const status = exception.getStatus();
      const body = exception.getResponse();
      if (typeof body === 'object' && body !== null) {
        response.status(status).json(body);
        return;
      }
      response.status(status).json({ error: String(body) });
      return;
    }

    // Log the failure server-side only; the client gets a flat message.
    this.logger.error(
      `unhandled error: ${exception instanceof Error ? exception.stack : String(exception)}`,
    );
    response.status(HttpStatus.INTERNAL_SERVER_ERROR).json({ error: 'Internal server error' });
  }
}
