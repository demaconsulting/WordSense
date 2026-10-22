### FormAuditor Verification

#### Verification Approach

`FormAuditorTests.cs` feeds hand-made samples and verdicts to `Evaluate` and checks the Wilson interval against known
values. The sampling and checker run are exercised by the `audit-forms` command itself, which is the regression gate
on the committed data (it needs Python and LemmInflect, so it is not part of the unit test run).

#### Test Environment

Standard xUnit v3 runner; temporary files; no network.

#### Acceptance Criteria

All scenarios pass.

#### Test Scenarios

- **Interval** (`Wilson_NoErrors_StartsAtZero`, `Wilson_SomeErrors_BracketsRate`, `Wilson_EmptySample_IsWholeRange`).
- **Gate** (`Evaluate_AllGood_Passes`, `Evaluate_UnadjudicatedDisagreement_Fails`, `Evaluate_ValidAdjudication_Clears`,
  `Evaluate_SkippedRows_AreNotChecked`, `Evaluate_SkippedAndAdjudicatedRow_IsNotCounted`,
  `Evaluate_InvalidAdjudicationOfDisagreement_IsAnError`, `Evaluate_OracleAcceptedRow_IsNotAnError`,
  `Evaluate_FeatureRestrictedVerdict_AppliesToThoseFeaturesOnly`): a row the checker skips is left out of the checked,
  disagreement and error counts even when an adjudication (valid or invalid) lists it, because an adjudication settles
  a disagreement only; an invalid adjudication of a row the checker disagrees with is an error and the failure row says
  so.
- **Adjudications** (`ReadAdjudications_SkipsComments`, `ReadAdjudications_NoFile_IsEmpty`,
  `ReadAdjudications_FeatureColumn_RestrictsTheKey`).
- **Oracle file** (`ReadVerdicts_DuplicateRow_Throws`, `ReadVerdicts_MalformedLine_Throws`): a row with two verdicts is
  rejected; a line with fewer or more than five fields, an empty field or an unknown verdict is rejected with its line
  number.
