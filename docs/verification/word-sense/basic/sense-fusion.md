### SenseFusion Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/SenseFusionTests.cs` call `SenseFusion.Fuse` and `Winner` with
hand-made posteriors, candidate senses and cue scores. The expected values follow from the formula in the unit design.
Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Posterior decides** (`SenseFusion_Fuse_PosPosteriorDecides`): the probabilities sum to one and the sense of the
  likely part of speech wins.
- **Cues separate same part of speech** (`SenseFusion_Fuse_CuesSeparateSamePartOfSpeech`): candidates that share a part
  of speech are ordered by their cue scores.
- **No posterior mass** (`SenseFusion_Fuse_NoPosMass_IsUniform`): when the tagger gives no mass to any candidate, the
  candidates are equally likely before the cues.
- **Extreme scores** (`SenseFusion_Fuse_ExtremeScores_StayFinite`): extreme scores do not produce non-finite
  probabilities.
