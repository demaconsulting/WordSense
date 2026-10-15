# WordSenseTable Verification

## Verification Approach

Tests in `WordSenseTableTests.cs` build small tables in memory, serialize and parse them, and damage
the bytes systematically. The embedded table is checked for its seed lemmas.

## Test Scenarios

- **WordSenseTable_RoundTrip_PreservesAllValues**, **WordSenseTable_ToArray_CalledTwice_ReturnsIdenticalBytes**,
  **WordSenseTable_Write_Stream_MatchesToArray**: lossless and deterministic serialization.
- **WordSenseTable_Constructor_InconsistentInput_Throws**, **WordSenseTable_LemmaModel_BadShapes_Throw**:
  structural validation.
- **WordSenseTable_Read_TruncatedAtEveryLength_Throws**, **WordSenseTable_Read_AnyByteFlipped_Throws**,
  **WordSenseTable_Read_Garbage_Throws**: integrity checks.
- **WordSenseTable_Score_Binary_UsesActivationSign**, **WordSenseTable_Score_OneVersusRest_PicksBestRow**:
  scoring.
- **WordSenseTable_Read_SensesWithSameTag_ShareTagInstance**, **..._ReadOldVersion_Throws**,
  **WordSenseTable_RoundTrip_PreservesGroups**,
  **WordSenseTable_LemmaModel_BadSenseDefinitions_Throw**: tagged senses are shared, old format and bad
  definitions are rejected.
- **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**: the embedded resource loads.

## Requirements Coverage

`WordSense-Table-Format`, `WordSense-Table-Senses`, `WordSense-Table-Integrity`, `WordSense-Table-Score`, `WordSense-Table-Embedded`.
