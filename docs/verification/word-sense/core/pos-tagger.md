### PosTagger Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Tests/PosTaggerTests.cs` call `PosTagger.Posteriors` on a toy table (a few
closed-class words, a word that is both noun and verb, and tag counts in which determiners and pronouns are followed by
nouns and verbs) with hand-made tagger tokens. Tests in `test/DemaConsulting.WordSense.Tests/WordSupportTests.cs` call
`WordSupport` with a toy usage function; the tests of `WordSupport.Of` and of the word image read the embedded word image.
Nothing else is mocked.

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
- **Word without counts follows the catalog** (`PosTagger_Posteriors_WordWithoutCounts_FollowsTheCatalogPrior`): on
  single-word sentences with equal noun and verb transitions, a word without counts whose catalog support is noun-heavy
  is a noun and one whose support is verb-heavy is a verb.
- **No support** (`PosTagger_Posteriors_NoSupport_IgnoresThePrior`): a token without catalog support gives equal noun
  and verb posteriors for a word without counts.
- **No share** (`PosTagger_Posteriors_NoShare_IgnoresThePrior`): a table whose prior share is 0 gives equal noun and
  verb posteriors for a word without counts, whatever its catalog support is.
- **Many counts outweigh the catalog** (`PosTagger_Posteriors_ManyCounts_OutweighTheCatalogPrior`): 400 verb counts
  against 4 noun counts keep the verb against a noun-heavy catalog support.
- **Few counts and the catalog** (`PosTagger_Posteriors_FewCounts_TieIsBrokenByTheCatalogPrior`): one count of each
  lets a noun-heavy catalog support decide for the noun.
- **Support totals** (`WordSupport_Compute_NounAndVerb_TotalsPerTag`): the tag counts, senses and lemma and
  part-of-speech pairs are totaled for each open-class tag.
- **One pair counts once** (`WordSupport_Compute_AnalysesOfOneSlot_CountOnce`): the past and the infinitive of a verb
  count as one pair.
- **Closed classes** (`WordSupport_Compute_ClosedClassAnalysis_IsIgnored`): a closed-class analysis adds nothing.
- **No support** (`WordSupport_Compute_NoSenses_ReturnsNull`, `WordSupport_Compute_NoAnalyses_ReturnsNull`,
  `WordSupport_Of_NoAnalyses_ReturnsNull`): a word whose analyses have no sense has no support, and neither has a word
  without analyses.
- **The prior** (`WordSupport_TryFillPrior_NounVerb_SumsToOneAndFollowsTheCounts`): the probabilities sum to one and
  follow the smoothed counts.
- **Uncovered tag** (`WordSupport_TryFillPrior_ClosedClassTag_KeepsItsGenericShare`): a closed-class tag keeps its
  generic share.
- **Exponent** (`WordSupport_TryFillPrior_ExponentBelowOne_FlattensThePrior`): an exponent below one flattens the
  probabilities.
- **Sense weight** (`WordSupport_TryFillPrior_SenseWeight_LiftsAPartOfSpeechWithManySenses`): the sense weight lifts a
  part of speech with many senses.
- **No evidence** (`WordSupport_TryFillPrior_NoPossibleTagHasASlot_ReturnsFalseWithTheGenericShare`): a word with no pair
  among its possible tags reports no evidence and gives the generic share.
- **Large exponent** (`WordSupport_TryFillPrior_LargeExponent_StaysFiniteAndNormalized`, theory with the exponents 16,
  1000 and 1,000,000): the probabilities are finite, sum to one, and the frequent noun takes the larger share.
- **Non-finite weight** (`WordSupport_TryFillPrior_NonFiniteWeight_Throws`, theory): a smoothing that is not a number or
  is infinite, and an exponent that is not a number, throw `InvalidDataException`.
- **Support from the image** (`WordSupport_Of_EmbeddedImage_ReadsTagCounts`): the support of `rank` in the embedded image
  has more noun than verb tag counts.
- **Repeated requests** (`WordSupport_Of_SameWordTwice_ReturnsTheSameSupport`): a second request for the same word
  gives the same instance.
- **Null key** (`WordSupport_Of_NullKey_Throws`): a missing key throws `ArgumentNullException`.
- **Usage totals** (`WordImage_UsageTotalsOf_MatchesTheUsageOfTheSenses`): the usage totals of the word image equal the
  sums over the usage of the senses.
