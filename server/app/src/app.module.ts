import { Module } from '@nestjs/common';
import { BeatmapsModule } from './beatmaps/beatmaps.module';
import { ConfigModule } from './config.module';
import { AuthModule } from './auth/auth.module';
import { HealthController } from './common/health.controller';
import { DbModule } from './db/db.module';
import { QuietModule } from './quiet/quiet.module';
import { ScoresModule } from './scores/scores.module';
import { StorageModule } from './storage/storage.module';
import { UsersModule } from './users/users.module';

@Module({
  imports: [
    ConfigModule,
    DbModule,
    StorageModule,
    AuthModule,
    UsersModule,
    BeatmapsModule,
    ScoresModule,
    QuietModule,
  ],
  controllers: [HealthController],
})
export class AppModule {}
