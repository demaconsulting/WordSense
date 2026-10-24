### BasicClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/BasicClassifierTests.cs` call the classifier with word lists, a toy
statistical table (`TestTables`) and a toy lexicon with analyses and sense inventories, so the expected results follow
from the table numbers. The tagger, cue model and fusion are real; nothing is mocked. Their own behavior is verified in
their unit documents. The scenarios check the level of every answer together with the details, the group and sense ids
and the confidence.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word** (`BasicClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails`,
  `..._UnknownWord_HasNoAnalyses`): a word with no lexicon entry is not known, has no analyses, no details, no group or
  sense id and confidence 0.
- **Unknown word level** (`..._UnknownWord_AnswersLevelNone`): the same answer states the level none.
- **Number word** (`..._NumberWord_ReturnsDetails`): a number word such as `one` or `twenty` has a lexicon entry and is
  answered with details, never as unknown.
- **Single-sense word** (`..._SingleSenseWord_ReturnsEntryWithFullConfidence`): a word with one analysis whose lemma
  and part of speech have one sense, such as a determiner, gives that analysis, that sense (with no group) and
  confidence 1, whatever the case of the word, and the word is returned as written.
- **Single-sense part answers at the sense level** (`..._SingleSensePart_AnswersSenseLevel`): the answer for that word
  states the level sense and carries the sense id and no group id.
- **Single analysis with many senses** (`..._SingleAnalysisWithManySenses_AnswersItWithConfidenceOne`): a word with one
  analysis and several senses is answered with that analysis at the part of speech level, without group or sense id,
  and confidence 1.
- **Single analysis shares its array** (`..._SingleAnalysisWord_SharesItsAnalysesArray`): two consecutive results for
  the same word with one analysis carry the same analyses array (a cache hit). This checks the allocation
  optimization of the design; it is not a requirement, because the cache may evict an entry.
- **Ambiguous word always answers** (`..._AmbiguousWord_AlwaysAnswersWithProbability`): a word with several analyses
  has details and a confidence greater than 0, at least one over the number of lemmas and parts of speech and at most 1.
- **Ambiguous word level** (`..._AmbiguousWord_AnswersPartOfSpeechLevel`): the answer states the level part of speech,
  has neither a group id nor a sense id, and its confidence is the probability of the lemma and part of speech of the
  selected analysis (the sum over its feature variants).
- **Ambiguous word lists analyses and no senses** (`..._AmbiguousWord_ListsAnalysesAndNoSenses`): the analyses of a
  word with several analyses are all listed, sorted by the probability of their lemma and part of speech and inside one
  by their own, their probabilities sum to
  one, and no analysis lists senses.
- **Single-sense analysis lists its sense** (`..._SingleSenseAnalysis_ListsThatSenseWithProbabilityOne`): an analysis
  whose lemma and part of speech have exactly one sense lists that sense with probability 1, also when the word has
  other analyses, and the answer is at the sense level only when that analysis is the selected one.
- **Ties keep the lexicon order** (`..._TiesKeepLexiconOrder`): analyses with equal probabilities are listed in the order
  of the lexicon, and the first of them is the selected analysis.
- **Variants of a slot** (`..._VariantsOfASlot_ShareItsProbabilityAndStayTogether`,
  `..._VariantsOfTheOnlySlot_AreAnsweredWithConfidenceOne`): the two variants of a verb beside the one analysis of a
  noun, with no evidence, have 1/4 each and the noun 1/2, are listed together before the noun (the slot that comes first
  in the lexicon wins the tie) and the confidence is 1/2, the probability of the slot; a word whose analyses are the
  singular and the plural of one noun with one sense is answered at the sense level with confidence 1, not 1/2.
- **Most probable analysis is selected** (`..._Context_SelectsTheMostProbableAnalysis`): the selected analysis
  (`Details`) is the first of the
  sorted analyses, that is, the best analysis of the most probable lemma and part of speech.
- **Never the group level** (`..._NeverAnswersGroupLevel`): over unknown, single-sense, single-analysis and ambiguous
  words, no answer has the level group.
- **Context chooses the part of speech** (`..._Context_ChoosesPartOfSpeech`): the sentence context decides between a
  noun and a verb analysis of the same word.
- **Same part of speech analyses use cues** (`..._SamePartOfSpeechAnalyses_UsesCues`): analyses that share a part of
  speech are told apart by the words around the target.
- **Segments are independent** (`..._Segments_AreIndependent`): a sentence-ending token separates the text into
  segments, and a word gets the same result whatever the other segments contain.
- **Segment bounds** (`BasicClassifier_SegmentOf_FindsBounds`): the segment of a position ends after a `.`, `!` or `?`
  token and starts after the previous one.
- **Case and apostrophe normalization** (`..._MixedCaseAndCurlyApostrophe_AreNormalized`): upper-case words and the
  curly apostrophe resolve like their lower-case straight forms.
- **Terms of the case scenarios** (as in `WordSense-Core-BasicClassifier-Case`): the tests use the tie table (no tagger
  weight, no cue score), so every analysis of the toy word `polish` (`Polish/Noun`, `polish/Noun`, `polish/Verb`, in that
  lexicon order) starts at 1/3 and only the written case moves the probabilities. The weight of an analysis whose lemma
  has the other case than the evidence is 0.1, applied after the fusion, and the probabilities are then scaled to sum to
  one. A segment ends after `.`, `!` or `?`; the first word of a segment is its first token with a letter; a neighbor is
  the token directly before or after; a punctuation token is never a capitalized neighbor and the first word of the
  segment never is; a heading is not a separate kind of input.
- **A lower-case word prefers the lower-case lemma** (`..._LowerCaseWordInsideSentence_PrefersTheLowerCaseLemma`,
  `..._LowerCaseWordAtTheStart_PrefersTheLowerCaseLemma`): `polish` inside a sentence, and as the first word, gives the
  capitalized lemma a tenth of the weight (0.1 against 1 and 1, so 0.1/2.1) and selects a lower-case lemma.
- **Tied analyses keep the lexicon order** (`..._LowerCaseWordInsideSentence_ListsTiedAnalysesInLexiconOrder`): once the
  weights are applied the shine and the verb tie at 1/2.1 and are listed in lexicon order, the language (0.1/2.1) last.
- **An isolated capitalized word prefers the capitalized lemma**
  (`..._CapitalizedWordInsideSentence_PrefersTheCapitalizedLemma`): `Polish` after other words, between lower-case
  words, has the lower-case lemmas at a tenth of the weight (0.1/1.2 each) and `Polish/Noun` is selected with 1/1.2.
  A punctuation neighbor does not make a run (`..._CapitalizedWordNextToPunctuation_PrefersTheCapitalizedLemma`), a
  segment with lower-case content words is not title case
  (`..._SegmentWithLowerCaseContentWord_IsNotTitleCase`), and the first word does not make a run, also after a
  quotation mark (`..._SentenceInitialWord_DoesNotMakeARun`,
  `..._SentenceInitialWordAfterQuotationMark_DoesNotMakeARun`): in `The Polish delegation left` the word `Polish` takes
  the evidence (1/1.2).
- **A capitalized word is looked up in lower case**
  (`..._CapitalizedWordInsideSentence_IsLookedUpInLowerCase`): a toy lexicon that has the capitalized lemma `Jack` only
  for the exact spelling `Jack` does not give it as a candidate: `Jack` and `jack` have the same analyses.
- **No evidence at the start of a segment** (`..._SentenceInitialCapital_AtTheStartOfTheText_CarriesNoCaseEvidence`,
  `..._SentenceInitialCapital_AfterQuotationMark_CarriesNoCaseEvidence`,
  `..._SentenceInitialCapital_InLaterSegment_CarriesNoCaseEvidence`): `Polish` as the first word of the text, after a
  quotation mark and as the first word of a later segment keeps the three analyses at 1/3, and the lexicon order
  selects `Polish/Noun`.
- **No evidence inside a run of capitalized words**
  (`..._CapitalizedWordBeforeCapitalizedWord_CarriesNoCaseEvidence`,
  `..._CapitalizedWordAfterCapitalizedWord_CarriesNoCaseEvidence`,
  `..._CapitalizedWordNextToUpperCaseWord_CarriesNoCaseEvidence`,
  `..._CapitalizedWordNextToMixedCaseWord_CarriesNoCaseEvidence`): `Polish` with a capitalized, upper-case or
  mixed-case neighbor (not the first word) before or after it keeps the analyses at 1/3.
- **No evidence in a title-case segment** (`..._TitleCaseSegment_CarriesNoCaseEvidence`): in
  `Silver Polish in Small Tins` (every word capitalized, `in` a small word of a title) `Polish` keeps 1/3 each.
- **No evidence for an upper-case, single-letter or mixed-case word**
  (`..._AllCapsWord_CarriesNoCaseEvidence`, `..._SingleCapitalLetter_CarriesNoCaseEvidence`,
  `..._MixedCaseWord_CarriesNoCaseEvidence`): `POLISH` keeps 1/3 each, the capital letter `I` in the middle of a sentence
  keeps the pronoun `i` and the letter at 1/2 each (the pronoun is selected by lexicon order), and the mixed-case word
  `pOlIsh`, which reads as `polish` in the lookup, keeps the probabilities of the case-neutral result (1/3 each).
- **A lemma with one case only keeps its analyses**
  (`..._OnlyCapitalizedLemmas_KeepEveryAnalysisInLowerCase`, `..._OnlyCapitalizedLemma_StaysKnownInLowerCase`,
  `..._CapitalizedWordWithoutCapitalizedLemma_IsAnsweredLikeLowerCase`): a name with capitalized lemmas only written in
  lower case keeps both analyses at 1/2 and a single capitalized analysis stays known with confidence 1; a capitalized
  word whose lemmas are all lower case has the same analyses and probabilities as its lower-case form.
- **A word that is not in the lexicon in any case** (`..._CapitalizedWordNotInLexicon_IsUnknown`): a capitalized word
  with no lexicon entry is unknown.
- **The embedded lexicon** (`..._EmbeddedLexicon_SelectsTheLemmaOfTheWrittenCase`): with the tie table and the
  embedded lexicon, `August` and `Polish` inside a sentence select the capitalized lemma, `august` and `polish` the
  lower-case one, and `State` in `the State Department` keeps `state`.
- **Punctuation** (`..._Punctuation_IsUnknown`): a token without a letter is unknown.
- **Empty list** (`..._EmptyList_ReturnsEmpty`): an empty list gives an empty array.
- **Results parallel the input** (`..._ResultsAreParallelToInput`): the result at an index describes the word at that
  index.
- **Deterministic and thread-safe** (`..._IsDeterministicAndThreadSafe`): the same text gives the same results every
  time, also from many threads at once.
- **Argument validation** (`..._NullWords_Throws`, `..._NullElement_Throws`): a null word list and a null word in the
  list throw.
- **Cancelled token** (`..._Cancelled_Throws`, `..._CancelledWhileReadingWords_Throws`): a cancelled token throws
  `OperationCanceledException`, also when the token is cancelled while a lazy word sequence is read.
