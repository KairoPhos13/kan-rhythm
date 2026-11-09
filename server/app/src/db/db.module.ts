import { Global, Module } from '@nestjs/common';
import { DbService } from './db.service';
import { PurgeService } from './purge.service';

@Global()
@Module({
  providers: [DbService, PurgeService],
  exports: [DbService],
})
export class DbModule {}
