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
  `Evaluate_SkippedRows_AreNotChecked`).
- **Adjudications** (`ReadAdjudications_SkipsComments`, `ReadAdjudications_NoFile_IsEmpty`).
