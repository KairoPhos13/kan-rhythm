/** kan! server MVP entry point: one NestJS process (see app/deploy/). */
import 'dotenv/config';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app.module';
import { configureApp } from './app.setup';
import { loadConfig } from './config';

async function bootstrap(): Promise<void> {
  const app = await NestFactory.create(AppModule, {
    bodyParser: false,
    logger: ['error', 'warn', 'log'],
  });
  configureApp(app);

  const config = loadConfig();
  await app.listen(config.port);
  console.log(`kan! server listening on port ${config.port}`);
}

bootstrap().catch((error) => {
  console.error('failed to start:', error instanceof Error ? error.stack : error);
  process.exitCode = 1;
});
