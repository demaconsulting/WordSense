# WordSenseBuilder Verification

## Verification Approach

Tests in `test/WordSense.Builder.Tests/BuilderTests.cs` use temporary directories and synthetic data;
the full corpus build is exercised by the accuracy gate of the system tests against the committed
table.

## Test Scenarios

- **WordSenseBuilder_Corpus_ReadSentences_ParsesRow**, **..._RejectsMalformedRow**,
  **WordSenseBuilder_Corpus_DirectoryAndDisjointness**, **WordSenseBuilder_Corpus_ReadForms_ParsesLabels**:
  corpus rules.
- **WordSenseBuilder_LogisticTrainer_SeparatesAndIsDeterministic**: separable data is separated and two
  runs agree exactly.
- **WordSenseBuilder_BatchImporter_ImportsValidatesAndIsIdempotent**: imported, invalid and missing
  batches; a second import changes nothing.
- **WordSenseBuilder_Program_Run_ReportsErrors**: usage and data errors return exit code 1.

## Requirements Coverage

`WordSense-Builder-Corpus`, `WordSense-Builder-Trainer` and `WordSense-Builder-Importer`.
