# WordSenseTable Verification

## Verification Approach

Tests in `WordSenseTableTests.cs` build small tables in memory, serialize and parse them, and damage
the bytes systematically. The embedded table is checked for its lemma count, parts of speech, meanings, forms and dimension.

## Test Scenarios

- **WordSenseTable_RoundTrip_PreservesAllValues**, **WordSenseTable_ToArray_CalledTwice_ReturnsIdenticalBytes**,
  **WordSenseTable_Write_Stream_MatchesToArray**: lossless and deterministic serialization.
- **WordSenseTable_Constructor_InconsistentInput_Throws**, **WordSenseTable_LemmaModel_BadShapes_Throw**:
  structural validation.
- **WordSenseTable_Read_TruncatedAtEveryLength_Throws**, **WordSenseTable_Read_AnyByteFlipped_Throws**,
  **WordSenseTable_Read_Garbage_Throws**: integrity checks.
- **WordSenseTable_Read_MutatedBodyWithValidChecksum_ThrowsFormatExceptionOrLoads**: every single-byte
  mutation of the body, re-signed with a valid digest, is either rejected with
  `WordSenseTableFormatException` or loads into a table that serializes; no other exception occurs.
- **WordSenseTable_Read_ResignedBadMagic_Throws**, **WordSenseTable_Read_ResignedTrailingData_Throws**,
  **WordSenseTable_Read_ResignedDimensionAboveCap_Throws**,
  **WordSenseTable_Read_ResignedUnknownModelKind_Throws**: deterministic structural rejections; the body is
  crafted and re-signed with a valid digest so that only the structural check can reject it.
- **WordSenseTable_Read_ResignedLemmaCountAboveCap_Throws** (100,001 lemmas),
  **WordSenseTable_Read_ResignedFormCountAboveCap_Throws** (2,000,001 forms),
  **WordSenseTable_Read_ResignedSenseCountOutOfRange_Throws** (1 and 65 senses),
  **WordSenseTable_Read_ResignedUndefinedPos_Throws** (a part of speech that is not defined),
  **WordSenseTable_Read_ResignedInvalidMeaning_Throws** (a meaning that is not a valid identifier) and
  **WordSenseTable_Read_ResignedBadFormEntry_Throws** (a lemma index out of range, and a duplicated form): the
  remaining caps and structural checks, with crafted bodies re-signed in the same way.
- **WordSenseTable_Constructor_NullArguments_Throw**, **WordSenseTable_Constructor_InvalidValues_Throw**: null
  arguments, a malformed digest, a non-finite or wrong-length mean, duplicate lemmas and a dimension mismatch are
  rejected.
- **WordSenseTable_LemmaModel_NullAndNonFiniteArguments_Throw**, **WordSenseTable_LemmaModel_UndefinedKind_Throws**:
  null arguments, non-finite bias or abstain margin, a bias of the wrong length and an undefined model kind are
  rejected.
- **WordSenseTable_Scores_OddDimension_MatchesScalarReference**: dimensions 19 and 37 (not a multiple of any SIMD
  width) give the same scores as a scalar reference.
- **WordSenseTable_Score_Binary_UsesActivationSign**, **WordSenseTable_Score_OneVersusRest_PicksBestRow**:
  scoring.
- **WordSenseTable_Score_BinaryZeroActivation_SelectsSecondSense**: a binary activation of exactly 0 selects sense
  index 1 with a margin of 0. **WordSenseTable_Score_OneVersusRestTie_SelectsFirstHighest**: with equal best
  activations the first highest row wins and the margin is 0.
- **WordSenseTable_Scores_Binary_ReturnsNegatedAndActivation**,
  **WordSenseTable_Scores_OneVersusRest_ReturnsPerRowActivation**,
  **WordSenseTable_Scores_LengthMismatch_Throws**: the public per-sense `Scores` output for binary and
  one-versus-rest models, and rejection of a wrong feature or scores length.
- **WordSenseTable_Read_OldVersion_Throws**, **WordSenseTable_RoundTrip_PreservesPronunciations**,
  **WordSenseTable_LemmaModel_BadSenseDefinitions_Throw**: other format versions and bad sense definitions are
  rejected, and parts of speech, meanings and respellings survive a round trip.
- **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**: the embedded resource loads with at least 344
  lemmas, the expected parts of speech and meanings for `record`, dimension 384, and every form resolving to a
  lemma with a model.
- **WordSenseTable_WriteAndTryGetLemma_EdgeCases**: writing a string over 65,535 bytes throws
  `InvalidOperationException`; `TryGetLemma` is false for an unknown lemma and throws for null.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0 with no setup beyond it. The embedded
table is a resource of the library; the unit tests need no model files, no network access and no external services.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

Each requirement is mapped to the scenarios above as follows.

- `WordSense-Table-Format`: the round-trip, determinism and stream scenarios,
  **WordSenseTable_Constructor_InconsistentInput_Throws**,
  **WordSenseTable_LemmaModel_BadShapes_Throw** and **WordSenseTable_WriteAndTryGetLemma_EdgeCases**.
- `WordSense-Table-Senses`: the pronunciation round-trip, old-version, bad sense definition, null or non-finite
  lemma model argument and undefined-kind scenarios.
- `WordSense-Table-Integrity`: the truncation, byte-flip, garbage, mutated-body and every re-signed scenario
  (magic, trailing data, caps, sense count, part of speech, meaning, form entries, model kind) and the table
  constructor null and invalid-value scenarios.
- `WordSense-Table-Score`: the binary and one-versus-rest scoring scenarios, the zero-activation and tie scenarios,
  the per-sense `Scores` scenarios, the length-mismatch scenario and the odd-dimension scalar-reference scenario.
- `WordSense-Table-Embedded`: **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**.
