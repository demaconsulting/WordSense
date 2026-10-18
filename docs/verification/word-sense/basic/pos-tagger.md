### PosTagger Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/PosTaggerTests.cs` call `PosTagger.Posteriors` on a toy table (a few
closed-class words, a word that is both noun and verb, and tag counts in which determiners and pronouns are followed by
nouns and verbs) with hand-made tagger tokens. Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **After a determiner** (`PosTagger_Posteriors_AfterDeterminer_PrefersNoun`): a word that could be noun or verb is a
  noun after a determiner.
- **After a pronoun** (`PosTagger_Posteriors_AfterPronoun_PrefersVerb`): the same word is a verb after a pronoun.
- **Rows sum to one** (`PosTagger_Posteriors_RowsSumToOne`): every row sums to one and only the possible tags have
  probability.
- **Single possible tag** (`PosTagger_Posteriors_SingleTag_IsCertain`): a token with one possible tag has posterior 1
  for it.
- **Unknown words** (`PosTagger_Posteriors_UnknownWords_UseCapitalizationAndSuffix`): an unknown capitalized word inside
  a sentence is a noun; at the start its suffix decides.
- **Empty and small buffer** (`PosTagger_Posteriors_EmptyAndSmallBuffer`): an empty sentence needs no result space and
  a result buffer that is too small throws `ArgumentException`.
- **Deterministic** (`PosTagger_Posteriors_IsDeterministic`): the same input gives bit-identical posteriors.
