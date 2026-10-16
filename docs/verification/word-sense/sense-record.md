# SenseRecord Verification

## Verification Approach

Tests in `SenseRecordTests.cs` exercise the answer values, the sense definition validation, the abstention rule
and the text splitter directly with hand-built inputs; no model is needed.

## Test Scenarios

- **LemmaEntry_Candidates_MapSensesToDetails**, **ResultRecords_HaveValueEquality**: one shared `WordDetails` per
  sense with the lemma, part of speech and pronunciation of its definition, and value equality of the answer and
  analysis records. **ResultRecords_HaveValueEquality** also covers the `AnswerBasis` and the candidate list of an
  analysis.
- **WordAnalysis_NoneAndDictionaryShapes_KeepDocumentedValues**: an unknown word (basis `None`) has no details and
  empty candidates; a one-sense dictionary answer holds that sense with a null pronunciation; a several-sense
  dictionary word has basis `Dictionary`, no details, `Abstained` true and every sense as candidates.
- **AnswerBasis_Values_AreStable**: `None`, `Context` and `Dictionary` keep the values 0, 1 and 2, and
  `default(WordAnalysis).Candidates` is null.
- **LemmaEntry_NonHeteronym_HasNoPronunciation**, **LemmaEntry_MissingRespelling_IsNull**,
  **LemmaEntry_SenseSet_Rules**: a lemma without meanings is not a heteronym and its answers have no
  pronunciation; a meaning without respelling gives a pronunciation with a null respelling; the sense-set rules
  (all or none, two distinct meanings, unique part of speech and meaning, consistent and distinct respellings)
  reject invalid sets.
- **SenseDefinition_Validate_InvalidDefinitions_Throw**, **SenseDefinition_IsValidMeaning_ChecksSyntax**,
  **SenseDefinition_IsValidRespelling_ChecksSyntaxAndLength**: syntax (including a trailing newline, null and
  empty) and length limits, an undefined part of speech and a respelling without a meaning. The invalid
  definitions assert the exception message: an undefined part of speech mentions the part of speech; an
  upper-case or empty meaning gives `Meaning 'Weapon' is invalid` and `Meaning '' is invalid`, and the message says
  the meaning must start with a lower-case letter and have at most 32 characters; a meaning with a
  trailing newline says it is invalid; a respelling without a meaning, with a space or empty mentions the
  respelling. Valid definitions pass.
- **LemmaEntry_Constructor_InvalidSenseDefinition_Throws**: the lemma entry constructor validates each sense
  definition; an undefined part of speech in the first of two senses throws `ArgumentException` mentioning the
  part of speech.
- **PartOfSpeech_Values_ArePinned**: the eight parts of speech keep the numeric values 0 (noun) to 7
  (interjection) used by the table format, and the enumeration has exactly eight members.
- **AbstainRule_Abstains_NaNMarginOrThreshold_Abstains**: a NaN margin or threshold abstains.
- **TextSplitter_SplitSegments_TileTheText**, **TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets**:
  segmentation. **TextSplitter_SplitWords_ReturnsNonSpaceTilesWithOffsets** splits `a  bat.` into the tiles
  `a`, `bat` and `.` starting at 0, 3 and 6. The exact-tiling invariant is verified at the splitter level by
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
