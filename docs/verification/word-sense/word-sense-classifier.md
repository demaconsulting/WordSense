# WordSenseClassifier Verification

## Verification Approach

Tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseModel` (`FakeWordSenseModel`, model id `fake-model`) that
returns deterministic vectors and counts its calls, a small in-memory lexicon table with context and dictionary
entries and a weights table built for it, so behavior is verified without a real model. The lexicon-only scenarios
use the embedded lexicon through `WordSenseClassifier.Create()` and need no model at all. The factory scenarios that
build a classifier from a model directory belong to the MiniLM system and are verified in the verification documents
of that system (`MiniLMWordSense`).

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0 with no setup beyond it; no model file,
network access or external service is needed.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**: concatenated tokens equal the input, token starts
  are contiguous, each token's analysis word equals its text, and a whitespace token has no details and a zero
  margin (unknown).
- **WordSenseToken_Tiles_FollowSegmentSpans**: the text overload `ClassifyAsync(text)` on `They bat it, don't.`
  returns tokens with contiguous spans whose texts concatenate to the input and whose analysis word equals the
  token text; only `bat` carries details, and it is the shared candidate of the lemma; the first token has none.
- **WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode**: no model call without targets.
- **WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **..._Words_ResolvesForms**, **..._Words_EdgeCases**, **..._ManyTargets_EncodesOnce**: lemma resolution, an
  array parallel to the input and a single model call. **..._InfiniteAbstainMarginScale_AlwaysAbstains**: a
  positive-infinity scale is accepted and every word abstains. **..._Words_EdgeCases** also covers unknown words,
  which have no details, are not abstained and have zero margins.
- **WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutModel**: a word whose entry has one sense is
  answered from the dictionary with basis `Dictionary`, margin 0, no runner-up, not abstained, and the model is
  never called.
- **WordSenseClassifier_ClassifyAsync_DictionaryMultiSense_ReturnsCandidates**: a word whose entry has several
  senses and is not decided by a model has no details, is abstained, has basis `Dictionary` and lists every
  candidate.
- **WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone**: a word that does not resolve has basis `None`,
  no candidates (an empty list, never null), no abstention and zero margins.
- **WordSenseClassifier_ClassifyAsync_DerivedPlural_AnswersFromDictionary**: with the words `a`, `cats` and a
  dictionary entry `cat`, `cats` is answered as the noun `cat` with basis `Dictionary` and one candidate; the word
  given is kept as `cats`.
- **WordSenseClassifier_ClassifyAsync_ContextAnswer_HasContextBasisAndCandidates**: for `the bat` with an abstain
  scale of 0 the answer has basis `Context`, is not abstained and has details among the candidates; with a scale of
  10 the same word abstains (no details); in both cases the candidates are the list `GetCandidates("bat")` returns
  and contain the runner-up.
- **WordSenseClassifier_ClassifyAsync_NoContextWords_NeverEncodes**: a list and a long text made only of dictionary,
  candidates-only and unknown words never call the model.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_AppliesToDictionaryWords**: the case policy of a dictionary word
  over a list of cases (start of input, after `.`, `!`, `?`, `:`, a quote, a bracket or a typographic quote:
  answered; after a word or a comma, or in all capitals: unknown; `I` and lower case: answered); the model is
  never called.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_ContextLemmasAndTextOverload**: in `a cat. Cat and a Cat` the
  second `cat` (after a full stop) is answered and the third unknown, which shows the text overload uses the previous
  non-space tile; the context lemma `BAT` in capitals is still answered from the context.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_SentenceStartIsLookedUp**,
  **..._CasePolicy_MidSentenceCapitalIsUnknown**, **..._CasePolicy_PronounIIsLookedUp**: a capitalized dictionary
  word at the start of the input, after a sentence-ending mark, a colon or an opening quotation mark or bracket is
  answered; the same word capitalized in the middle of a sentence is unknown; the word `I` is answered anywhere.
- **WordSenseClassifier_GetCandidates_DictionaryLemma_ReturnsSenses**: `ContextLemmas` lists only the context lemma;
  `GetCandidates` returns the senses of a dictionary lemma in table order and an empty list for an unknown lemma.
- **WordSenseClassifier_ContextLemmas_And_GetCandidates_AreExposed** (including an unknown lemma): the context
  lemmas, and the candidate answers of any lemma of the table.
- **WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown**: a context word for which the model
  returns no vector, and one whose centered feature is zero, are unknown (basis `None`, no details, not abstained, no
  candidates); `batted` and `batly`, which would derive from a context lemma, are unknown without a model call.
- **WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce**: a text with a context word, a dictionary word and an
  unknown word calls the model once, answers the context word with basis `Context` and the dictionary word with
  basis `Dictionary`.
- **WordSenseClassifier_ClassifyAsync_UseLexiconFalse_AnswersOnlyContextLemmas**,
  **..._UseLexiconFalse_DictionaryWordIsUnknown**: with the dictionary option off, a context word is still answered
  from the context and a dictionary word is unknown with basis `None`; a word derived by morphology is unknown and
  `GetCandidates` of a dictionary-only lemma is empty.
- **WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp**, **..._ThreeSenses_ReportsRunnerUpMargin**,
  **..._LowMargin_Abstains**: the `WordAnalysis` fields. For a two-sense lemma `Margin` is twice the activation,
  `RunnerUp` is the other sense and `RunnerUpMargin` is 0; for a three-sense lemma `RunnerUp` is the second-best
  sense and `RunnerUpMargin` is its score minus the third-best score. A word below the scaled threshold has
  `Abstained` set, no details, and its margin and runner-up still reported, which tells it apart from an unknown
  word.
- **WordSenseClassifier_Create_LexiconOnly_AnswersSingleSenseWord**: a lexicon-only classifier from `Create()` answers
  a single-sense word from the embedded lexicon with basis `Dictionary`.
- **WordSenseClassifier_Create_LexiconOnly_ContextualWordAbstainsWithCandidates**: with the same classifier a
  contextual multi-sense word has no details, is abstained, has basis `Dictionary` and lists every sense as a
  candidate.
- **WordSenseClassifier_Constructor_TableWithoutModel_ContextLemmaAbstains**: a classifier built over a table with no
  model and no weights answers the dictionary words and lets a context lemma abstain with its candidates.
- **WordSenseClassifier_Dispose_LexiconOnly_IsHarmless**: disposing a lexicon-only classifier, twice, does no harm and
  later calls still answer.
- **WordSenseClassifier_Constructor_Mismatch_Throws**: weights that name another model id, model digest, dimension or
  lexicon are refused with `WordSenseModelMismatchException`.
- **WordSenseClassifier_Constructor_ModelWithoutWeights_Throws**: a model without weights, and weights without a model,
  are refused with `ArgumentException`.
- **WordSenseClassifier_Constructor_InvalidArguments_Throw** (a null table or a negative or NaN `AbstainMarginScale`),
  **WordSenseClassifier_Create_InvalidOptions_Throw**: validation.
- **WordSenseClassifier_ClassifyAsync_Cancelled_Throws**, **..._Concurrent_IsConsistent**,
  **WordSenseClassifier_Dispose_OwnedModel_IsDisposed**: lifetime.
- **WordSenseClassifier_ClassifyAsync_CancelledDuringEncode_Throws**: the token is cancelled by the model during its
  call; the classifier then throws `OperationCanceledException` at the check before the first target is scored.
- **WordSenseClassifier_Dispose_OwnedModel_LaterCallsBehaveAsDocumented**: after an owned classifier is disposed
  (twice, harmlessly) the model is disposed; the word-array and text overloads with a context word throw
  `ObjectDisposedException`; both overloads with only unknown words still return answers without details; the
  model is never called.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

Test names below omit the `WordSenseClassifier_` prefix.

- `WordSense-Classifier-Tiles`: **ClassifyAsync_Text_TokensTileInput**, **WordSenseToken_Tiles_FollowSegmentSpans**
  (full name) and **ClassifyAsync_NoKnownWords_DoesNotEncode**.
- `WordSense-Classifier-Flow`: **ClassifyAsync_DictionaryWord_AnswersWithoutModel**,
  **ClassifyAsync_DictionaryMultiSense_ReturnsCandidates**, **ClassifyAsync_UnknownWord_HasBasisNone** and
  **ClassifyAsync_Mixed_EncodesOnce**.
- `WordSense-Classifier-Answers`: **ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **ClassifyAsync_ManyTargets_EncodesOnce**, **ClassifyAsync_Words_ResolvesForms**,
  **ClassifyAsync_Words_EdgeCases** and **ClassifyAsync_InfiniteAbstainMarginScale_AlwaysAbstains**.
- `WordSense-Classifier-Encoder`: **ClassifyAsync_DictionaryWord_AnswersWithoutModel**,
  **ClassifyAsync_NoKnownWords_DoesNotEncode**, **ClassifyAsync_NoContextWords_NeverEncodes**,
  **ClassifyAsync_Mixed_EncodesOnce** and **ClassifyAsync_ManyTargets_EncodesOnce**.
- `WordSense-Classifier-LexiconOnly`: **Create_LexiconOnly_AnswersSingleSenseWord**,
  **Create_LexiconOnly_ContextualWordAbstainsWithCandidates**,
  **Constructor_TableWithoutModel_ContextLemmaAbstains** and **Dispose_LexiconOnly_IsHarmless**.
- `WordSense-Classifier-CasePolicy`: **ClassifyAsync_CasePolicy_SentenceStartIsLookedUp**,
  **ClassifyAsync_CasePolicy_MidSentenceCapitalIsUnknown**, **ClassifyAsync_CasePolicy_PronounIIsLookedUp**,
  **ClassifyAsync_CasePolicy_AppliesToDictionaryWords** and
  **ClassifyAsync_CasePolicy_ContextLemmasAndTextOverload**.
- `WordSense-Classifier-UseLexicon`: **ClassifyAsync_UseLexiconFalse_AnswersOnlyContextLemmas** and
  **ClassifyAsync_UseLexiconFalse_DictionaryWordIsUnknown**.
- `WordSense-Classifier-Basis`: **ClassifyAsync_ContextAnswer_HasContextBasisAndCandidates**,
  **ClassifyAsync_DictionaryWord_AnswersWithoutModel**, **ClassifyAsync_DictionaryMultiSense_ReturnsCandidates**,
  **ClassifyAsync_DerivedPlural_AnswersFromDictionary**, **ClassifyAsync_UnknownWord_HasBasisNone** and
  **ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown**.
- `WordSense-Classifier-Candidates`: **ContextLemmas_And_GetCandidates_AreExposed** and
  **GetCandidates_DictionaryLemma_ReturnsSenses**.
- `WordSense-Classifier-Abstain`: **ClassifyAsync_LowMargin_Abstains**.
- `WordSense-Classifier-Analysis`: **ClassifyAsync_ReportsMarginsAndRunnerUp**,
  **ClassifyAsync_ThreeSenses_ReportsRunnerUpMargin**, **ClassifyAsync_LowMargin_Abstains**,
  **ClassifyAsync_DictionaryWord_AnswersWithoutModel** and **ClassifyAsync_DictionaryMultiSense_ReturnsCandidates**.
- `WordSense-Classifier-Unknown`: **ClassifyAsync_Words_EdgeCases**, **ClassifyAsync_LowMargin_Abstains**,
  **ClassifyAsync_NoKnownWords_DoesNotEncode** and **ClassifyAsync_UnknownWord_HasBasisNone**.
- `WordSense-Classifier-ModelMatch`: **Constructor_Mismatch_Throws** and **Constructor_ModelWithoutWeights_Throws**.
- `WordSense-Classifier-Validation`: **Constructor_InvalidArguments_Throw** and **Create_InvalidOptions_Throw**.
- `WordSense-Classifier-Lifetime`: **ClassifyAsync_Cancelled_Throws**, **ClassifyAsync_Concurrent_IsConsistent**,
  **Dispose_OwnedModel_IsDisposed**, **ClassifyAsync_CancelledDuringEncode_Throws** and
  **Dispose_OwnedModel_LaterCallsBehaveAsDocumented**.
