import { Module } from '@nestjs/common';
import { QuietController } from './quiet.controller';

@Module({
  controllers: [QuietController],
})
export class QuietModule {}
