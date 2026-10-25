### BasicClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/BasicClassifierTests.cs` call the classifier with word lists, a toy
statistical table (`TestTables`) and a toy lexicon with analyses and sense inventories, so the expected results follow
from the table numbers; the usage prior is verified in `BasicClassifierUsagePriorTests.cs` and `UsagePriorTests.cs` with
the embedded lexicon (the same table, no toy lexicon). The tagger, cue model and fusion are real; nothing is mocked.
Their own behavior is verified in
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
- **Possessive token** (`BasicClassifier_MakeToken_Possessive_HasTheStem`): a word whose analyses all carry `Poss=Yes`
  and whose key ends in `'s` or `'` (also with the typographic apostrophe and a capital letter) is made into a
  possessive tagger token that carries its stem, the case of the written word and the mask of its analyses.
- **Not a possessive** (`BasicClassifier_MakeToken_NotPossessive_IsAWholeToken`): a contraction, a word with an analysis
  that is not a possessive, a word without the ending and an unknown word stay whole tokens that are not flagged.
- **Possessive keeps the caller's tokens** (`BasicClassifier_ClassifyAsync_Possessive_KeepsTheTokensOfTheCaller`): the
  results of a sentence with a possessive stay parallel to the words, the possessive answered with its own analysis at
  its own position. The effect on the next word with the embedded table is verified at the subsystem level
  (`BasicWordSenseModel_ClassifyAsync_PossessiveToken_ReadsTheNextWordAsNoun`).
- **Non-finite verb** (`BasicClassifier_MakeToken_NonFiniteVerb_IsFlagged`): a word whose verb analyses are all gerunds
  or participles without a past tense (`resolving`, `broken`) is flagged non-finite on its tagger token.
- **Finite verb or no verb** (`BasicClassifier_MakeToken_FiniteOrNoVerb_IsNotFlagged`): an infinitive (`remove`), a past
  tense (`walked`), a word that is a past tense and a present (`read`), a word without a verb analysis (`table`) and an
  unknown word are not flagged.
- **Number word** (`..._NumberWord_ReturnsDetails`): a number word such as `one` or `twenty` has a lexicon entry and is
  answered with details, never as unknown.
- **Single-sense word** (`..._SingleSenseWord_ReturnsEntryWithFullConfidence`): a word with one analysis whose lemma
  and part of speech have one sense, such as a determiner, gives that analysis, that sense (with no group) and
  confidence 1, whatever the case of the word, and the word is returned as written.
- **Single-sense part answers at the sense level** (`..._SingleSensePart_AnswersSenseLevel`): the answer for that word
  states the level sense and carries the sense id and no group id.
- **Single analysis with many senses** (`..._SingleAnalysisWithManySenses_AnswersItWithConfidenceOne`): a word with one
  analysis and several senses is answered with that analysis at the part of speech level, without group or sense id,
  and confidence 1 (on the toy lexicon, whose classifier has no ranking function, the analysis lists no senses).
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
  one, and no analysis lists senses (the toy lexicon has no ranking function).
- **Usage prior** (`UsagePriorTests.cs`): with the embedded lexicon, the ranking of a lemma and part of speech follows the
  WordNet 3.0 tag counts with add-k smoothing.
  - The probability of a sense is `(count + k) / sum` with k = 1 (`UsagePrior_Rank_ProbabilityIsTheCountPlusKOverTheSum`,
    `UsagePrior_RankOf_UsesTheSmoothingConstantOne`) and the probabilities sum to one
    (`UsagePrior_Rank_ProbabilitiesSumToOne`).
  - The senses are sorted by probability, the largest first (`UsagePrior_Rank_SortsByProbabilityLargestFirst`), and equal
    probabilities keep the inventory order (`UsagePrior_Rank_EqualCounts_KeepInventoryOrder`,
    `UsagePrior_Rank_NoCounts_IsUniformInInventoryOrder`).
  - A project sense counts 0 (`UsagePrior_RankOf_ProjectSense_CountsZero`); the smoothing constant never changes the order
    (`UsagePrior_Rank_SmoothingConstant_NeverChangesTheOrder`, theory).
  - A smoothing constant that is not a positive number (`UsagePrior_Rank_SmoothingNotPositive_Throws`, theory), a negative
    count (`UsagePrior_Rank_NegativeCount_Throws`), a missing list of senses (`UsagePrior_Rank_MissingList_Throws`)
    and a
    missing lemma (`UsagePrior_RankOf_MissingLemma_Throws`)
    are rejected.
  - A lemma and part of speech with one sense, or that is not in the catalog, has no ranking
    (`UsagePrior_RankOf_SingleSense_IsEmpty`, `UsagePrior_RankOf_NotInTheCatalog_IsEmpty`); a polysemous lemma ranks every
    sense of its slot (`UsagePrior_RankOf_PolysemousLemma_RanksEverySenseOfTheSlot`) and the same ranking comes on every
    call (`UsagePrior_RankOf_RepeatedCalls_GiveTheSameRanking`).
- **Usage prior in the classifier** (`BasicClassifierUsagePriorTests.cs`): every analysis whose lemma and part of speech
  have two or more senses lists them in the order of the prior
  (`BasicClassifier_ClassifyAsync_PolysemousWord_ListsTheSensesRankedByUsage`); the ranking function is asked only for
  such lemmas and parts of speech (`..._PriorIsAskedOnlyForSlotsWithTwoOrMoreSenses`); an analysis whose lemma and part
  of speech have no sense lists none (`..._AnalysisWithoutSenses_ListsNoSenses`); without a ranking function (the toy
  constructor of the tests) an analysis lists none (`..._WithoutAPrior_ListsNoSensesForAPolysemousAnalysis`, a test
  without a requirement); and the same word in another sentence has the same ranking
  (`..._RankingDoesNotDependOnContext`).
- **The ranking is marked** (same file): every analysis whose senses are ranked by the prior has `SensesRankedByUsage`
  true and no other analysis has it (`..._UsagePrior_MarksTheSensesAsRankedByUsage`); without a ranking function nothing
  is marked (`..._WithoutAPrior_MarksNothingAsRankedByUsage`); a ranking function that returns one sense for a
  polysemous slot gives no ranking, no mark and a part-of-speech answer (`..._RankingOfFewerThanTwoSenses_IsNoRanking`).
- **The prior is never an answer** (same file): the analysis probabilities
  (`..._UsagePrior_NeverChangesTheAnalysisProbabilities`), the selected analysis
  (`..._UsagePrior_NeverChangesTheSelectedAnalysis`) and the level, confidence, sense id and group id
  (`..._UsagePrior_NeverChangesTheAnswer`) equal those of a classifier without the prior, and a polysemous word is
  answered at the part-of-speech level with no sense id and no group id
  (`..._PolysemousWord_AnswersAtThePartOfSpeechLevelWithoutSenseOrGroup`).
- **Key of a ranked word** (same file): the key of a polysemous word ends with the label of the top-ranked sense of the
  selected analysis (`..._PolysemousWord_KeyCarriesTheLabelOfTheTopRankedSense`) and is a member of the keys of the
  lexicon (`..._PolysemousWord_KeyIsOneOfTheEnumeratedKeys`).
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
- **Tense split: a past context** (`..._TenseTable_PastContext_SelectsThePastAnalysis`): a toy tense table (no tagger
  weight, no cue table) whose features say past after `had` selects the past analysis of `put` in `they had put`, listed
  first with a probability above the infinitive, although the lexicon lists the infinitive first.
- **Tense split: an infinitive context** (`..._TenseTable_InfinitiveContext_SelectsTheNonPastAnalysis`): the same table
  selects the infinitive in `they to put`.
- **Tense split: participles and the slot** (`..._TenseTable_ParticipleCountsAsPast`): the participle `VerbForm=Part` of
  `come` counts as a past analysis, so `they had come` selects it and `they to come` selects the infinitive.
- **Tense split: the log odds** (`..._TenseTable_ContextFeaturesSetTheLogOdds`): with a prior of zero, the left neighbor
  `had` at +3 nats and `to` at -3 nats, the log of the odds of the past against the non-past variant of `put` is 3 in
  `they had put`, -3 in `they to put` and 0 in `they put`.
- **Tense split: the weight** (`..._TenseTable_WeightScalesTheLogOdds`): with the same table, the weight 2 gives the log
  of the odds 6 and the weight 0.5 gives 1.5.
- **Tense split: bounds** (`..._TenseTable_LargeWeight_KeepsProbabilitiesFinite`,
  `..._TenseTable_LargeLogOdds_AreBoundedTo30Nats`): with the largest weight a table accepts (1e6) every probability of
  `they had put` is finite, the analyses sum to one, the past analysis is selected and the log of the odds is 30; with a
  prior and a left-neighbor weight that make 31.75 nats and the weight 1 the log of the odds is also 30.
- **The split keeps the slot** (`..._TenseTable_KeepsSlotProbabilityLevelAndConfidence`): for `putt` (a noun slot and a
  verb slot with a past and a non-past variant) with the weight 0 and with the weight 1, the sum of the probabilities of
  the verb slot (1/2), the noun slot (1/2, still first), the level, the confidence, the group and the sense are equal,
  and the probability of the past variant differs.
- **The shared model decides alone** (`..._TenseTable_ReplacesTheSplitOfTheFormTable`): with the cue table of `read`
  favoring the present after `tomorrow`, the table with the weight 0 keeps that split (the odds of the past below 1), and
  the table with a prior of one nat and the weight 1 gives the past exactly e times the probability of the present
  whatever the form table said, with the same slot probability and confidence.
- **No tense table** (`..._NoTenseTable_LeavesProbabilitiesUnchanged`): a table without a tense table gives `put` the same
  probabilities, analysis by analysis and in the same order, as a table with the model turned off (1/2 each, the
  infinitive selected by lexicon order).
- **Weight 0** (`..._TenseWeightZero_LeavesProbabilitiesUnchanged`): a table with a strong past context and the weight 0
  gives `they had put` the probabilities of the table without a tense table (1/2 each) and the infinitive.
- **A slot that is not a tense slot** (`..._NonVerbSlot_LeavesProbabilitiesUnchanged`): the noun variants of `deer`
  (singular and plural) keep the probabilities of the table without a tense table (1/2 each) although the tense table has
  a strong past context.
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
