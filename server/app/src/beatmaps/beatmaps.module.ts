import { Module } from '@nestjs/common';
import { BeatmapsController } from './beatmaps.controller';
import { BeatmapsService } from './beatmaps.service';
import { SubmissionController } from './submission.controller';
import { SubmissionService } from './submission.service';

@Module({
  controllers: [BeatmapsController, SubmissionController],
  providers: [BeatmapsService, SubmissionService],
  exports: [BeatmapsService],
})
export class BeatmapsModule {}
