# SenseRecord Verification

## Verification Approach

Tests in `SenseRecordTests.cs` exercise the answer values, the sense definition validation, the abstention rule
and the text splitter directly with hand-built inputs; no model is needed.

## Test Scenarios

- **LemmaModel_Candidates_MapSensesToDetails**, **ResultRecords_HaveValueEquality**: one shared `WordDetails` per
  sense with the lemma, part of speech and pronunciation of its definition, and value equality of the answer and
  analysis records.
- **LemmaModel_NonHeteronym_HasNoPronunciation**, **LemmaModel_MissingRespelling_IsNull**,
  **LemmaModel_SenseSet_Rules**: a lemma without meanings is not a heteronym and its answers have no
  pronunciation; a meaning without respelling gives a pronunciation with a null respelling; the sense-set rules
  (all or none, two distinct meanings, unique part of speech and meaning, consistent and distinct respellings)
  reject invalid sets.
- **SenseDefinition_Validate_InvalidDefinitions_Throw**, **SenseDefinition_IsValidMeaning_ChecksSyntax**,
  **SenseDefinition_IsValidRespelling_ChecksSyntaxAndLength**: syntax (including a trailing newline, null and
  empty) and length limits, an undefined part of speech and a respelling without a meaning.
- **AbstainRule_Abstains_NaNMarginOrThreshold_Abstains**: a NaN margin or threshold abstains.
- **TextSplitter_SplitSegments_TileTheText**, **TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets**:
  segmentation. The exact-tiling invariant is verified at the splitter level by
  **TextSplitter_SplitSegments_TileTheText** and at the classifier level by
  **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput** and **WordSenseToken_Tiles_FollowSegmentSpans**
  (see the classifier verification).
- **TextSplitter_SplitWords_InternalPunctuationAndMarks_StayOneWord**,
  **TextSplitter_SplitWords_SurrogatePairAndTrailingApostrophe_AreSeparateTiles**,
  **TextSplitter_SplitWords_TrailingHyphen_IsOwnTile**: `don't`, `well-known`,
  `x1`, U+2019, combining marks, surrogate pairs, a trailing apostrophe and a trailing hyphen as its own tile.

## Requirements Coverage

`WordSense-Sense-Record`, `WordSense-Sense-Pronunciation`, `WordSense-Sense-Validation`,
`WordSense-Sense-Abstain` and `WordSense-Sense-Words`.
