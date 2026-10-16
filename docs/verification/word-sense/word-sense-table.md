# WordSenseTable Verification

## Verification Approach

Tests in `WordSenseTableTests.cs` build small tables in memory, serialize and parse them, and damage
the bytes systematically. The embedded table is checked for its lemma count, tags, forms and dimension.

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
- **WordSenseTable_Score_Binary_UsesActivationSign**, **WordSenseTable_Score_OneVersusRest_PicksBestRow**:
  scoring.
- **WordSenseTable_Scores_Binary_ReturnsNegatedAndActivation**,
  **WordSenseTable_Scores_OneVersusRest_ReturnsPerRowActivation**,
  **WordSenseTable_Scores_LengthMismatch_Throws**: the public per-sense `Scores` output for binary and
  one-versus-rest models, and rejection of a wrong feature or scores length.
- **WordSenseTable_Read_SensesWithSameTag_ShareTagInstance**, **WordSenseTable_Read_OldVersion_Throws**,
  **WordSenseTable_RoundTrip_PreservesGroups**,
  **WordSenseTable_LemmaModel_BadSenseDefinitions_Throw**: tagged senses are shared, old format and bad
  definitions are rejected.
- **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**: the embedded resource loads with at least 344
  lemmas, every sense tagged, dimension 384, and every form resolving to a lemma with a model.
- **WordSenseTable_WriteAndTryGetLemma_EdgeCases**: writing a string over 65,535 bytes throws
  `InvalidOperationException`; `TryGetLemma` is false for an unknown lemma and throws for null.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0 with no setup beyond it. The embedded
table is a resource of the library; the unit tests need no model files, no network access and no external services.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

`WordSense-Table-Format`, `WordSense-Table-Senses`, `WordSense-Table-Integrity`, `WordSense-Table-Score`, `WordSense-Table-Embedded`.
