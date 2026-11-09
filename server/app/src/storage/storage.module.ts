import { Global, Module } from '@nestjs/common';
import { APP_CONFIG, AppConfig } from '../config';
import { createStorage } from './create-storage';
import { STORAGE, Storage } from './storage';

@Global()
@Module({
  providers: [
    {
      provide: STORAGE,
      inject: [APP_CONFIG],
      useFactory: (config: AppConfig): Storage => createStorage(config),
    },
  ],
  exports: [STORAGE],
})
export class StorageModule {}
