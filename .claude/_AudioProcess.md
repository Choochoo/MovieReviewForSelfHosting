## Audio Processing Workflow Documentation

### Overview
The audio processing system handles files through a multi-stage pipeline that progresses from individual file processing to synchronized collective analysis. Each file tracks its own status through the workflow, and the overall progress is calculated based on all files' combined states.

### Processing Stages

#### Phase 1: Individual File Processing (Parallel)
Each audio file progresses independently through these stages:

1. **Pending** - File has been selected/uploaded but processing hasn't started
2. **Uploading** - File is being uploaded to the server
3. **ConvertingToMp3** - Converting audio file (WAV/other formats) to MP3
4. **FinishedConvertingToMp3** - Conversion complete, file ready for Gladia upload
5. **UploadingToGladia** - Uploading MP3 to Gladia transcription service
6. **FinishedUploadingToGladia** - Upload complete, waiting for transcription
7. **WaitingToDownloadTranscripts** - Gladia is processing the file
8. **DownloadingTranscripts** - Retrieving completed transcript from Gladia
9. **TranscriptsDownloaded** - Transcript successfully retrieved and saved

#### Phase 2: Synchronization Point
10. **WaitingForOtherFiles** - This file is done with individual processing, waiting for all other files to reach this point

#### Phase 3: Collective Processing (Sequential)
Once ALL files reach the synchronization point, they progress together through:

11. **ProcessingTranscriptions** - Combining and processing all transcripts together
12. **SendingToOpenAI** - Sending combined transcript data to OpenAI API
13. **ProcessingWithAI** - OpenAI is analyzing the content
14. **ReadyToProcessAIResponse** - AI response received and saved, ready for local processing
15. **ProcessingAIResponse** - Processing the AI response locally (extracting insights, generating clips, etc.)
16. **Complete** - All processing finished successfully

#### Error States
- **Failed** - General failure (can occur at any stage)
- **FailedMp3** - MP3 conversion specifically failed

### Progress Calculation

**Overall Progress Formula:**
```
Overall Progress = (Sum of all file progress values) / (Number of files × 100) × 100%
```

**Individual File Progress Values:**
- Each status has a base progress value (0-100)
- For in-progress statuses, the actual progress blends the base value with the completion percentage
- Example: If a file is "UploadingToGladia" at 50% complete, and base values are:
  - UploadingToGladia = 30
  - FinishedUploadingToGladia = 40
  - File progress = 30 + (40-30) × 0.5 = 35

**Example with 6 Files:**
- 2 files at "FinishedConvertingToMp3" (20 points each) = 40
- 2 files at "UploadingToGladia" 50% done (35 points each) = 70  
- 2 files at "WaitingForOtherFiles" (70 points each) = 140
- Total: 250 / 600 = 41.6% overall progress

### Key Workflow Rules

1. **Parallel Processing**: Files process independently through Phase 1 (steps 1-9)
2. **Synchronization**: All files must reach "WaitingForOtherFiles" before any can proceed
3. **Collective Processing**: Phase 3 steps (11-16) happen to all files simultaneously
4. **Error Handling**: Failed files can be retried individually without affecting others
5. **Status Transitions**: Each status has a defined next status in the workflow
6. **Progress Tracking**: Both individual file progress (0-100%) and status-based progress are tracked

### UI Requirements

1. **Overall Progress Bar**: Shows combined progress of all files
2. **Individual File Status**: Each file shows current status, progress bar, and action buttons
3. **Bulk Actions**: Buttons to start processing all files at once from specific stages
4. **Status Summary**: Visual summary showing file count at each status
5. **Real-time Updates**: Progress and status updates should reflect immediately

### State Management

- Track both status enum and progress percentage for each file
- Update `LastUpdated` timestamp on each status change
- Persist session state to database at key milestones
- Support resuming from any status after interruption