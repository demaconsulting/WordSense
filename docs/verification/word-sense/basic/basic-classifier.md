### BasicClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs` call the classifier with word lists, a toy
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
- **Single analysis shares its array** (`..._SingleAnalysisWord_SharesItsAnalysesArray`): two results for the same word
  with one analysis carry the same analyses (equal contents; the array is built once and shared).
- **Ambiguous word always answers** (`..._AmbiguousWord_AlwaysAnswersWithProbability`): a word with several analyses
  has details and a confidence greater than 0, at least one over the number of analyses and at most 1.
- **Ambiguous word level** (`..._AmbiguousWord_AnswersPartOfSpeechLevel`): the answer states the level part of speech
  and has neither a group id nor a sense id.
- **Ambiguous word lists analyses and no senses** (`..._AmbiguousWord_ListsAnalysesAndNoSenses`): the analyses of a
  word with several analyses are all listed, sorted by probability with the largest first, their probabilities sum to
  one, and no analysis lists senses.
- **Single-sense analysis lists its sense** (`..._SingleSenseAnalysis_ListsThatSenseWithProbabilityOne`): an analysis
  whose lemma and part of speech have exactly one sense lists that sense with probability 1, also when the word has
  other analyses, and the answer is at the sense level only when that analysis is the selected one.
- **Ties keep the lexicon order** (`..._TiesKeepLexiconOrder`): analyses with equal probabilities are listed in the order
  of the lexicon, and the first of them is the selected analysis.
- **Most probable analysis is selected** (`..._Context_SelectsTheMostProbableAnalysis`): the selected analysis
  (`Details`) is the first of the
  sorted analyses, that is, the analysis of the largest probability, and its probability is the confidence.
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
- **Punctuation** (`..._Punctuation_IsUnknown`): a token without a letter is unknown.
- **Empty list** (`..._EmptyList_ReturnsEmpty`): an empty list gives an empty array.
- **Results parallel the input** (`..._ResultsAreParallelToInput`): the result at an index describes the word at that
  index.
- **Deterministic and thread-safe** (`..._IsDeterministicAndThreadSafe`): the same text gives the same results every
  time, also from many threads at once.
- **Argument validation** (`..._NullWords_Throws`, `..._NullElement_Throws`): a null word list and a null word in the
  list throw.
- **Cancelled token** (`..._Cancelled_Throws`): a cancelled token throws `OperationCanceledException`.
