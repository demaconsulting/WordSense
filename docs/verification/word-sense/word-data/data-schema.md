### DataSchema Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/DataSchemaTests.cs` load the committed `data/schema.yaml`, damaged copies of
it written to a temporary folder, and rewrite a copy's file split line.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Committed schema loads** (`Load_CommittedSchema_DeclaresTables`, `..._DeclaresVocabularies`): the tables and the
  vocabularies are present.
- **Sense inventory tables** (`Load_CommittedSchema_DeclaresSenseGroupAndFrequencyTables`): the sense label, id map,
  group, group sense, frequency, respelling and legacy map tables are declared with their keys, their foreign keys
  and the origin, source and level vocabularies, and the tables of the retired classes and form scopes are not.
- **Damaged schema is rejected** (`Load_Damaged_Throws`): unsupported syntax, an unknown type, a dangling key or foreign
  key, a relation whose inverse stores the same direction; the message names the line.
- **Missing file throws** (`Load_MissingFile_Throws`).
- **Split line rewrite** (`WriteFileSplit_ReplacesOnlyThatLine`, `..._EmptyPlan_RoundTrips`).
