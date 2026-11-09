import { Global, Module } from '@nestjs/common';
import { AuthService } from './auth.service';
import { OAuthController } from './oauth.controller';
import { AuthGuard } from './auth.guard';

@Global()
@Module({
  controllers: [OAuthController],
  providers: [AuthService, AuthGuard],
  exports: [AuthService, AuthGuard],
})
export class AuthModule {}
