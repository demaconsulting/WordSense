### PosTagger Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/PosTaggerTests.cs` call `PosTagger.Posteriors` on a toy table (a few
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
- **Clause with a verb** (`PosTagger_Posteriors_ClauseWithVerb_PrefersNounAfterDeterminerAndNoun`): on tag counts in
  which a verb follows a determiner and a noun only in a clause without a verb, a word that could be noun or verb after
  determiner and noun is a verb at the start of a sentence and a noun after an earlier verb.
- **Clause break** (`PosTagger_Posteriors_ClauseBreak_RestartsTheClause`, comma and conjunction): after a comma or a
  conjunction the same word is a verb again, as at the start of a sentence.
- **Non-finite start** (`PosTagger_Posteriors_NonFiniteStart_DoesNotSetTheClauseState`): on counts in which a verb
  follows a determiner and a noun only in a clause without a verb, a first token flagged non-finite leaves the clause
  without a verb, so the word after a determiner and a noun is a verb, while the same first token not flagged
  makes that word a noun.
- **Possessive is read as stem and marker** (`PosTagger_Posteriors_Possessive_IsReadAsStemAndMarker`): on counts in which
  a noun follows a noun and the marker but a verb follows a bare initial noun, the word after a possessive token is a
  noun and the word after the bare stem is a verb.
- **Several possessives keep one row per token** (`PosTagger_Posteriors_SeveralPossessives_KeepOneRowPerToken`): a
  sentence with two possessives and other tokens gives one row for each token, each a distribution, with the fixed tags
  where the token has only one, and a possessive token is its stem noun with probability 1.
