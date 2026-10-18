### DataLayout Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/DataLayoutTests.cs` call the layout functions directly and write tables to
a temporary folder.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **FileFor** (`FileFor_NoSplit_UsesFirstLetter`, `..._SplitLetter_UsesTwoLetters`, `..._NonAsciiStart_UsesUnderscore`,
  `..._DecomposedLetter_UsesNfc`): the key rule, including NFC input and the `_` file.
- **IsValidKey** (`IsValidKey_SplitPlan_Decides`): keys that the rule can and cannot produce.
- **Byte order** (`CompareUtf8_AstralAfterHighBmp`): an astral character sorts after U+FFFD.
- **Cells and files** (`CheckCell_Separators_AreRejected`, `WriteTable_WritesLfWithoutBom`, `WriteTable_BadCell_Throws`).
