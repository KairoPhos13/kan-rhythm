import { Module } from '@nestjs/common';
import { RegistrationController } from './registration.controller';
import { UsersController } from './users.controller';
import { UsersService } from './users.service';

@Module({
  controllers: [RegistrationController, UsersController],
  providers: [UsersService],
  exports: [UsersService],
})
export class UsersModule {}
