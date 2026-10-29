### BasicWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/BasicWordSenseModelTests.cs` use the public type with the embedded
lexicon and the embedded statistics table. The model has no files and no state, so nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **No preparation step** (`BasicWordSenseModel_IsNotDownloadable`): the model is an `IWordSenseModel` but not an
  `IDownloadableWordSenseModel`, by type check and by reflection, and has no `EnsureModelAsync` member, so a caller that
  checks for the capability finds none; it touches neither the file system nor the network.
- **ClassifyAsync works without setup** (`BasicWordSenseModel_ClassifyAsync_WithoutPreparation_Works`,
  `BasicWordSenseModel_ClassifyAsync_ReturnsParallelResults`): `ClassifyAsync` works on a new model with no call before
  it and answers a word list parallel to the input; lexicon-only words need no preparation at all.
- **Classification is thread-safe**: parallel calls on one model give the same results as sequential calls.
- **Part of speech answers with the embedded statistics**
  (`BasicWordSenseModel_ClassifyAsync_AuxiliaryContext_AnswersAuxiliary`, `..._CopulaBe_AnswersMainVerb`,
  `..._NounContext_StaysNoun`, `..._NeedAsMainVerb_IsNotAuxiliary`): with the embedded lexicon and table, a modal or a
  helper in its context is answered as an auxiliary analysis, the copula `be` as a verb, a noun after an article or
  possessive stays a noun, and `need` as a main verb is not an auxiliary; each answer is at the part of speech level.
- **The tense of a same-spelling verb follows its context**
  (`BasicWordSenseModel_ClassifyAsync_SameSpellingVerb_TenseFollowsTheContext`, theory): with the embedded lexicon and
  table, `read`, `put`, `cut` and `set` select a non-past verb analysis in `I will read that tomorrow`, `Please put the
  books on the shelf`, `They will cut the budget next year` and `We usually set the table before dinner`, and a past one
  in `I have read that already`, `She had put the keys in her bag before she left`, `He cut the cake into six equal
  pieces` and `The workers had already set up the stage`, with the probabilities of the word summing to one.
- **A possessive token is read as the stem and the marker**
  (`BasicWordSenseModel_ClassifyAsync_PossessiveToken_ReadsTheNextWordAsNoun`): with the embedded lexicon and table, the
  word after a joined possessive (`the sergeant's rank`) is a noun, as after `the rank`, while the possessive itself is
  answered as one token with its own noun analysis.
- **A noun after a modifier noun in a clause that has a verb**
  (`BasicWordSenseModel_ClassifyAsync_NounAfterNounInClauseWithVerb_IsNoun`): with the embedded table, the word after a
  determiner and a noun in a clause that already has a verb (`Please open the door handle`) is a noun, answered with a
  confidence above 0.75.
- **A verb after a clause break** (`BasicWordSenseModel_ClassifyAsync_VerbAfterClauseBreak_IsVerb`): with the embedded
  table, the word after a determiner and a noun is a verb after a comma or a conjunction that starts a new clause
  (`He nodded , the pump runs`, `He nodded and the pump runs`).
- **A gerund at the start is still a verb** (`BasicWordSenseModel_ClassifyAsync_GerundAtTheStart_IsStillAVerb`): with the
  embedded lexicon and table, a first word that is listed only as a gerund and is followed by a determiner
  (`Resolving this issue requires care`, `Checking this valve takes time`) is answered `Verb`.
- **Cancelled token is honored**: `ClassifyAsync` with an already cancelled token throws `OperationCanceledException`.
  A null word list is rejected.
- **Disposal**: `Dispose` is idempotent, thread-safe and releases nothing, so a classification that is already
  running completes normally. After it, `ClassifyAsync` throws `ObjectDisposedException`, whatever the words (including
  an empty list).
