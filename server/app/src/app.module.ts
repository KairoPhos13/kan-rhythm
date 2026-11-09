import { Module } from '@nestjs/common';
import { BeatmapsModule } from './beatmaps/beatmaps.module';
import { ConfigModule } from './config.module';
import { AuthModule } from './auth/auth.module';
import { DbModule } from './db/db.module';
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
  ],
})
export class AppModule {}
