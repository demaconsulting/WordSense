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
- **Tense features: cue features, never the target**
  (`CueModel_ExtractTense_IncludesTheCueFeaturesAndNeverTheTarget`): for `i have already read the book` the left
  neighbor `already`, the right neighbor `the` and the window token `have` are features, the target `read` is not, and no
  feature is listed twice.
- **Tense features: sides** (`CueModel_ExtractTense_WindowTokensAreToldApartBySide`): in `the have read the book` the
  tokens before the target are left-window features (`have`, `the`), the tokens after it are right-window features
  (`the`, `book`), `the` is both, `have` is not a right-window feature and `the` is a left-window feature once.
- **Tense features: segment edge** (`CueModel_ExtractTense_SegmentEdgesAreNeighbors`): the first word (the first token with
  a letter) has the segment edge as its left neighbor and the last word as its right neighbor, also when a quotation mark
  stands before it (`" put it back . "`: `put` has the edge on the left and the quotation mark as its left neighbor, no
  edge on the right) or the closing punctuation after it (`we will put . "`: `put` has the edge on the right); a word
  with a word on both sides (`we put it .`) has no edge.
- **Tense features: governor** (`CueModel_ExtractTense_GovernorIsTheNearestFunctionWordWithinTheRadius`,
  `CueModel_IsGovernor_ClosedClassOfFunctionWords`): the governor is the nearest function word within six tokens (`not`
  before `will`, none seven tokens away), and only the closed class of function words (`have`, `will`, `to`, `didn't`) is
  a governor, not `read` or `yesterday`.
- **Tense features: capacity** (`CueModel_ExtractTense_FullWindow_FitsTheBuffer`): a target with a full window on both
  sides needs at most the 23 features of the buffer.
- **Tense feature kinds** (`FeatureRef_Hash_TenseKindsHaveOwnPrefixes`, in `BasicTablesTests.cs`, which tests the feature
  hash): `have` as a left-window, right-window and governing feature hashes as `LW:have`, `RW:have` and `NA:have`, the cue
  kinds keep their prefix (`L1:have`), and the four hashes of `have` differ.
- **Tense score** (`CueModel_ScoreTense_SumsPriorAndKnownWeights`): the score is the prior plus the weights of the known
  features (an unknown feature adds nothing, no feature gives the prior).
- **Tense score quantization** (`CueModel_ScoreTense_ResultIsInEighthsOfANat`): a stored weight of 5 and a prior of 3 give
  1 nat, a whole number of eighths, and the scale is 0.125.
- **Tense score, null table** (`CueModel_ScoreTense_NullTable_Throws`): a null table throws `ArgumentNullException` for
  the parameter `cue`.
- **Noun/verb features** (`CueModel_ExtractNounVerb_WordAndClassOfTheTwoNeighborsOnEachSide`): a target with two words
  on each side gives eight features in a fixed order, the word and the class of the first left, the second left, the first
  right and the second right neighbor, and a word the lexicon does not know has the class `<unknown>`.
- **Noun/verb features, edges** (`CueModel_ExtractNounVerb_EdgeAndPunctuationNeighbors`): a neighbor outside the segment
  is the segment edge for the word and for the class; a punctuation neighbor is itself the word and has the class of
  punctuation.
- **Neighbor class** (`CueModel_ContextClassOf_NamesTheClassOfTheAnalyses`, theory): an auxiliary, a verb that can be
  finite, a gerund, a word with several parts of speech with a verb among them and one without, a word with one part of
  speech and an unknown word get the classes `auxiliary`, `verb`, `nonfinite`, `ambiguous-verb`, `ambiguous`, the name of
  the part of speech and `<unknown>`.
- **Noun/verb features never read the target** (`CueModel_ExtractNounVerb_DoesNotReadTheTarget`): two sentences that
  differ only in the target give the same features.
- **Noun/verb score** (`CueModel_ScoreNounVerb_SumsKnownWeightsWithoutPrior`): the score is the sum of the weights of the
  known features in eighths of a nat (an unknown feature adds nothing, no feature gives 0).
- **Noun/verb, null arguments** (`CueModel_ExtractNounVerb_NullLexicon_Throws`, `CueModel_ScoreNounVerb_NullTable_Throws`):
  a null lexicon and a null table each throw `ArgumentNullException` for their parameter.
