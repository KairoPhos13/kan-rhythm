import { Module } from '@nestjs/common';
import { BeatmapsModule } from '../beatmaps/beatmaps.module';
import { RegistrationController } from './registration.controller';
import { UsersController } from './users.controller';
import { UsersService } from './users.service';

@Module({
  imports: [BeatmapsModule],
  controllers: [RegistrationController, UsersController],
  providers: [UsersService],
  exports: [UsersService],
})
export class UsersModule {}
