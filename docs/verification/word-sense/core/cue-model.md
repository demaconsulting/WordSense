### CueModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/CueModelTests.cs` call `CueModel.Extract` on small token lists and
`CueModel.Score` on the cue table of a toy form with analyses that share a part of speech (the past and the present of
a verb) built by `TestTables`, so the expected scores are sums of visible quantized weights. Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Window and neighbors** (`CueModel_Extract_WindowAndNeighbors`): the features are the distinct letter-bearing window
  tokens plus the two immediate neighbors.
- **Dedupe and window limit** (`CueModel_Extract_DedupesAndLimitsWindow`): a repeated window word counts once and the
  window is limited to five words on each side.
- **Edges** (`CueModel_Extract_Edges`): the first and last word of a segment have no left or right feature.
- **Scores follow weights** (`CueModel_Score_UsesWeightsAndMarksMissingAnalyses`): the scores of the candidate analyses
  follow the quantized weights and priors, and an analysis the table lacks scores `NaN`.
- **Unknown feature** (`CueModel_Score_UnknownFeature_GivesPrior`): features without a table entry add nothing, so the
  score is the prior.
- **Invalid arguments** (`CueModel_Score_NullTableOrDefaultAnalyses_Throws`): a null table and a default analysis
  array throw `ArgumentNullException`.
