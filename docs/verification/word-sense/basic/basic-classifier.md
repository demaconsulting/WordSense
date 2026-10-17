### BasicClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs` call the classifier with word lists, a toy
statistical table (`TestTables`) and a toy lexicon, so the expected results follow from the table numbers. The tagger,
cue model and fusion are real; nothing is mocked. Their own behavior is verified in their unit documents.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word** (`BasicClassifier_ClassifyAsync_UnknownWord_ReturnsNoDetails`): a word with no lexicon entry gives
  `(word, null, 0)`.
- **Single-sense word** (`..._SingleSenseWord_ReturnsEntryWithFullConfidence`): a word with one entry gives
  `(word, entry, 1.0)` and the word is returned as written.
- **Ambiguous word always answers** (`..._AmbiguousWord_AlwaysAnswersWithProbability`): a word with several senses has
  details and a confidence of at least one over the number of senses and below 1.
- **Context chooses the part of speech** (`..._Context_ChoosesPartOfSpeech`): the sentence context decides between a
  noun and a verb sense of the same word.
- **Same part of speech uses cues** (`..._SamePartOfSpeech_UsesCues`): senses that share a part of speech are told apart
  by the words around the target.
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
