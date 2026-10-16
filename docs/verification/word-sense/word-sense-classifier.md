# WordSenseClassifier Verification

## Verification Approach

Tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseEncoder` that returns deterministic
hidden states and a small in-memory table with trained and dictionary entries, so behavior is verified
without the model. The exceptions are
`WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory`,
`WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory` and
`WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel`, which are in `WordSenseTests.cs`.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**: concatenated tokens equal the input, token starts
  are contiguous, each token's analysis word equals its text, and a whitespace token has no details and a zero
  margin (unknown).
- **WordSenseToken_Tiles_FollowSegmentSpans**: the text overload `ClassifyAsync(text)` on `They bat it, don't.`
  returns tokens with contiguous spans whose texts concatenate to the input and whose analysis word equals the
  token text; only `bat` (start 5) carries details, and it is the shared candidate of the lemma; the first token
  has none.
- **WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode**: no encoder call without targets.
- **WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **..._Words_ResolvesForms**, **..._Words_EdgeCases**, **..._ManyTargets_EncodesOnce**: lemma resolution, an
  array parallel to the input and a single encoder pass. **..._InfiniteAbstainMarginScale_AlwaysAbstains**: a
  positive-infinity scale is accepted and every word abstains. **..._Words_EdgeCases** also covers unknown words,
  which have no details, are not abstained and have zero margins.
- **WordSenseClassifier_ClassifyAsync_DictionaryWord_AnswersWithoutEncoder**: a word whose entry has one sense and
  no weights is answered from the dictionary with basis `Dictionary`, margin 0, no runner-up, not abstained, and
  the encoder is never called.
- **WordSenseClassifier_ClassifyAsync_DictionaryMultiSense_ReturnsCandidates**: a word whose entry has several
  senses and no weights has no details, is abstained, has basis `Dictionary` and lists every candidate.
- **WordSenseClassifier_ClassifyAsync_UnknownWord_HasBasisNone**: a word that does not resolve has basis `None`,
  no candidates (an empty list, never null), no abstention and zero margins.
- **WordSenseClassifier_ClassifyAsync_DerivedPlural_AnswersFromDictionary**: with the words `a`, `cats` and a
  dictionary entry `cat`, `cats` is answered as the noun `cat` with basis `Dictionary` and one candidate; the word
  given is kept as `cats`.
- **WordSenseClassifier_ClassifyAsync_ContextAnswer_HasContextBasisAndCandidates**: for `the bat` with an abstain
  scale of 0 the answer has basis `Context`, is not abstained and has details among the candidates; with a scale of
  10 the same word abstains (no details); in both cases the candidates are the list `GetCandidates("bat")` returns
  and contain the runner-up.
- **WordSenseClassifier_ClassifyAsync_NoContextWords_NeverEncodes**: a list and a long text (maximum sequence length
  8) made only of dictionary, candidates-only and unknown words never call the encoder; 62 tokens of the text have
  basis `Dictionary`.
- **WordSenseClassifier_ClassifyAsync_LongTextWithDictionaryWords_EncodesOnlyContextTargets**: a long text (maximum
  sequence length 8) of dictionary words with one `the bat` calls the encoder exactly once, for the one context
  target.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_AppliesToDictionaryWords**: the case policy of a dictionary word
  over a list of cases (start of input, after `.`, `!`, `?`, `:`, a quote, a bracket or a typographic quote:
  answered; after a word or a comma, or in all capitals: unknown; `I` and lower case: answered); the encoder is
  never called.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_TrainedLemmasAndTextOverload**: in `a cat. Cat and a Cat` the
  second `cat` (after a full stop) is answered and the third unknown, which shows the text overload uses the previous
  non-space tile; the trained lemma `BAT` in capitals is still answered from the context.
- **WordSenseClassifier_GetCandidates_DictionaryLemma_ReturnsSenses**: `ContextLemmas` lists only the trained lemma;
  `GetCandidates` returns the senses of a dictionary lemma in table order (noun and verb for `run`, one noun for
  `cat`) and an empty list for an unknown lemma.
- **WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown**: a trained word whose text yields no
  word pieces, and one whose centered feature is zero, are unknown (basis `None`, no details, not abstained, no
  candidates); `batted` and `batly`, which would derive from a trained lemma, are unknown without an encoder call.
  A trained lemma always has at least two senses (the table rejects fewer; see the table verification), so a
  runner-up always exists for a scored word.
- **WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce**: a text with a trained word, a dictionary word and an
  unknown word calls the encoder once, answers the trained word with basis `Context` and the dictionary word with
  basis `Dictionary`.
- **WordSenseClassifier_ClassifyAsync_CasePolicy_SentenceStartIsLookedUp**,
  **..._CasePolicy_MidSentenceCapitalIsUnknown**, **..._CasePolicy_PronounIIsLookedUp**: a capitalized dictionary
  word at the start of the input, after a sentence-ending mark, a colon or an opening quotation mark or bracket is
  answered; the same word capitalized in the middle of a sentence is unknown; the word `I` is answered anywhere.
- **WordSenseClassifier_ClassifyAsync_UseLexiconFalse_AnswersOnlyContextLemmas**,
  **..._UseLexiconFalse_DictionaryWordIsUnknown**: with the dictionary option off, a trained word is still answered
  from the context and a dictionary word is unknown with basis `None`; a word derived by morphology is unknown and
  `GetCandidates` of a dictionary-only lemma is empty.
- **WordSenseClassifier_ContextLemmas_And_GetCandidates_AreExposed** (including an unknown lemma): the trained
  lemmas, and the candidate answers of any lemma of the table.
- **WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp**, **..._ThreeSenses_ReportsRunnerUpMargin**,
  **..._LowMargin_Abstains**: the `WordAnalysis` fields. For a two-sense lemma `Margin` is twice the activation,
  `RunnerUp` is the other sense and `RunnerUpMargin` is 0; for a three-sense lemma `RunnerUp` is the second-best
  sense and `RunnerUpMargin` is its score minus the third-best score. A word below the scaled threshold has
  `Abstained` set, no details, and its margin and runner-up still reported, which tells it apart from an unknown
  word.
- **WordSenseClassifier_ClassifyAsync_LongText_UsesWindowPerTarget**: windowing. A long text with two known words
  (`the bat` and `a bat`, separated by long filler) and a maximum sequence length of 8 gives a verb and a noun,
  exactly two encoder calls (one per target) and no encoder input longer than 8.
- **WordSenseClassifier_Constructor_InvalidArguments_Throw** (including a negative or NaN
  `AbstainMarginScale`, and a null encoder, table or tokenizer: `ArgumentNullException`),
  **WordSenseClassifier_Constructor_VocabularyPath_ReadsFileOrThrows** (the public constructor with a vocabulary
  path: a valid file works; a null path throws `ArgumentNullException`, an empty path or a vocabulary without a
  special token `ArgumentException`, a missing file `FileNotFoundException`; a null encoder or table and a
  mismatched table are still rejected), **..._ModelMismatch_Throws**,
  **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory** (missing files),
  **WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory** (files with the wrong digest),
  **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel** (invalid options are reported before the
  model directory is touched): validation.
- **WordSenseClassifier_ClassifyAsync_Cancelled_Throws**, **..._Concurrent_IsConsistent**,
  **WordSenseClassifier_Dispose_OwnedEncoder_IsDisposed**: lifetime.
- **WordSenseClassifier_ClassifyAsync_CancelledDuringEncode_StopsBeforeNextTarget**: the token is cancelled by the
  encoder during its first call, which here encodes the whole text; the text `the bat and a bat` then throws
  `OperationCanceledException` at the check before the first target, and the encoder has been called once.
- **WordSenseClassifier_ClassifyAsync_CancelledBetweenTargets_StopsAfterFirstWindow**: a long text (maximum sequence
  length 8) with two known words far apart; the token is cancelled during the encoding of the first target's window,
  so the call throws `OperationCanceledException` at the check before the second target, the encoder has been called
  once and no input was longer than 8.
- **WordSenseClassifier_Dispose_OwnedEncoder_LaterCallsBehaveAsDocumented**: after an owned classifier is disposed
  (twice, harmlessly) the encoder is disposed; the word-array and text overloads with a known word throw
  `ObjectDisposedException`; both overloads with only unknown words still return answers without details; the
  encoder is never called.

## Requirements Coverage

`WordSense-Classifier-Tiles`, `-Flow`, `-Answers`, `-Encoder`, `-CasePolicy`, `-UseLexicon`, `-Candidates`,
`-Abstain`, `-Analysis`, `-Basis`, `-Unknown`, `-LongText`, `-Validation` and `-Lifetime`.
