/** Bearer-token guard: 401 only for genuinely invalid tokens (§0 of the inventory). */
import { CanActivate, createParamDecorator, ExecutionContext, Injectable } from '@nestjs/common';
import { Request } from 'express';
import { apiError } from '../common/http';
import { AuthenticatedUser, AuthService } from './auth.service';

export function bearerToken(request: Request): string | undefined {
  const header = request.headers['authorization'];
  if (typeof header !== 'string') return undefined;
  const match = /^Bearer\s+(.+)$/i.exec(header.trim());
  return match ? match[1] : undefined;
}

@Injectable()
export class AuthGuard implements CanActivate {
  constructor(private readonly auth: AuthService) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<Request & { user?: AuthenticatedUser }>();
    const user = await this.auth.authenticate(bearerToken(request));
    if (!user) throw apiError(401, 'Unauthorized');
    request.user = user;
    return true;
  }
}

/** Current user for guarded routes. */
export const CurrentUser = createParamDecorator(
  (_data: unknown, context: ExecutionContext): AuthenticatedUser => {
    return context.switchToHttp().getRequest<Request & { user: AuthenticatedUser }>().user;
  },
);

/** Resolves the user for routes where authentication is optional. */
export async function optionalUser(
  auth: AuthService,
  request: Request,
): Promise<AuthenticatedUser | null> {
  return auth.authenticate(bearerToken(request));
}
