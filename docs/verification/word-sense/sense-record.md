# SenseRecord Verification

## Verification Approach

Tests in `SenseRecordTests.cs` exercise the records, the tag scorer and the text splitter directly with
hand-built inputs; no model is needed.

## Test Scenarios

- **WordSense_HasTag_ReportsMembership**, **WordSense_HasTag_NullTag_ThrowsArgumentNullException**,
  **WordSense_Tags_AreImmutable**, **WordSense_LemmaModel_SharesOneRecordPerSense**: record behavior.
- **SenseDefinition_IsValidTag_ChecksSyntax**, **SenseDefinition_IsValidTag_LengthLimit**,
  **SenseDefinition_Validate_InvalidDefinitions_Throw**: tag syntax (including a trailing newline and null) and
  rejection of an empty name, no tags, more than 16 tags, unsorted, duplicated and invalid tags or groups.
- **WordSense_Group_DefaultsToSenseAndValidates**: the group defaults to the sense label and is validated.
- **TagScorer_Classify_BestScoringTagWins**, **..._EqualTagScores_TiesAndIsBelowAnyPositiveThreshold**,
  **..._SensesOfSameTag_DoNotReduceMargin**, **..._BelowThreshold_Abstains**, **..._OtherWins_ReturnsNull**,
  **..._OtherAsRunnerUp_LimitsMargin**, **..._AbsentTag_ReturnsNull_SinglePresentTag_Answers**,
  **TagScorer_IsTag_AnswersBoolOrNull** (which includes the tie rule: the tag wins a tie): the tag rule.
- **TagScorer_Abstains_NaNMarginOrThreshold_Abstains**: a NaN margin or threshold abstains.
- **TextSplitter_SplitSegments_TileTheText**, **TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets**:
  segmentation. The exact-tiling invariant of `WordSenseToken` sequences is verified at the splitter level by
  **TextSplitter_SplitSegments_TileTheText** and at the classifier level by
  **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput** (see the classifier verification), which
  concatenates the token texts and compares them with the input.
- **TextSplitter_SplitWords_InternalPunctuationAndMarks_StayOneWord**,
  **TextSplitter_SplitWords_SurrogatePairAndTrailingApostrophe_AreSeparateTiles**,
  **TextSplitter_SplitWords_TrailingHyphen_IsOwnTile**: `don't`, `well-known`,
  `x1`, U+2019, combining marks, surrogate pairs, a trailing apostrophe and a trailing hyphen as its own tile.

## Requirements Coverage

`WordSense-Sense-Record`, `WordSense-Sense-Tags`, `WordSense-Sense-TagScoring` and `WordSense-Sense-Words`.
