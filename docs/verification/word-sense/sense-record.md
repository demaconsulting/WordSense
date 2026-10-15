# SenseRecord Verification

## Verification Approach

Tests in `SenseRecordTests.cs` exercise the records, the tag scorer and the text splitter directly with
hand-built inputs; no model is needed.

## Test Scenarios

- **WordSense_HasTag_ReportsMembership**, **WordSense_Tags_AreImmutable**,
  **WordSense_LemmaModel_SharesOneRecordPerSense**: record behavior.
- **SenseDefinition_IsValidTag_ChecksSyntax**, **SenseDefinition_IsValidTag_LengthLimit**: tag syntax.
- **WordSense_Group_DefaultsToSenseAndValidates**: the group defaults to the sense label and is validated.
- **TagScorer_Classify_BestScoringTagWins**, **..._SenseWithBothTags_TiesAndIsBelowAnyPositiveThreshold**,
  **..._SensesOfSameTag_DoNotReduceMargin**, **..._BelowThreshold_Abstains**, **..._OtherWins_ReturnsNull**,
  **..._OtherAsRunnerUp_LimitsMargin**, **..._AbsentTag_ReturnsNull_SinglePresentTag_Answers**,
  **TagScorer_IsTag_AnswersBoolOrNull**: the tag rule.
- **TextSplitter_SplitSegments_TileTheText**, **TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets**:
  segmentation.

## Requirements Coverage

`WordSense-Sense-Record`, `WordSense-Sense-Tags`, `WordSense-Sense-TagScoring` and `WordSense-Sense-Words`.
