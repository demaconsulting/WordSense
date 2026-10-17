# WordSenseTable Verification

This document covers the lexicon table (`WordSenseTable`, format `WSNS` version 6), the per-model weights table
(`WordSenseWeightsTable`, format `WSNW` version 1) and their parts: `LemmaEntry`, `SenseWeights` and `LexicalIndex`.

## Verification Approach

Tests in `WordSenseTableTests.cs`, `WordSenseWeightsTableTests.cs`, `SenseWeightsTests.cs`, `LexicalIndexTests.cs`
and `LemmaResolutionTests.cs` build small tables with and without a dictionary in memory, serialize and parse them,
resolve words through the single resolver and damage the bytes systematically. The embedded lexicon is checked for
its lemma count, parts of speech, meanings, forms and dictionary size. The lexicon holds no weights; weights are
verified on the weights table.

## Test Scenarios

- **WordSenseTable_RoundTrip_PreservesAllValues**, **WordSenseTable_ToArray_AndWrite_AreDeterministic**: lossless and
  deterministic serialization, including pronunciations and respellings; the candidates of a read lemma are shared
  (the same list and the same first element on every access) and carry the lemma and part of speech of their sense.
- **WordSenseTable_RoundTrip_WithDictionary_PreservesLemmasAndMorphology**: a table with dictionary entries and
  enabled morphology rules reads back with the same lemmas, senses and rule flags.
- **LexicalIndex_CompressDecompress_RoundTrips**: the front-coded dictionary section survives compression and
  decompression unchanged.
- **WordSenseTable_Constructor_InconsistentInput_Throws**, **WordSenseTable_Constructor_NullArguments_Throw**,
  **WordSenseTable_Constructor_ShadowingAndBadDictionaryWords_Throw**: null arguments, inconsistent input, a
  dictionary word that shadows a lemma or form, and a bad dictionary word are rejected.
- **WordSenseTable_Read_TruncatedAtEveryLength_Throws**, **WordSenseTable_Read_AnyByteFlipped_Throws**,
  **WordSenseTable_Read_Garbage_Throws**: integrity checks.
- **WordSenseTable_Read_WeightsMagic_Throws**: a weights table given to the lexicon reader is rejected by its magic.
- **WordSenseTable_Read_OtherVersion_Throws**: versions other than 6 are rejected with a message that mentions the
  version.
- **WordSenseTable_Read_MutatedBodyWithValidChecksum_ThrowsFormatExceptionOrLoads**: every single-byte
  mutation of the body, re-signed with a valid digest, is either rejected with
  `WordSenseTableFormatException` or loads into a table that serializes; no other exception occurs.
- **WordSenseTable_Read_ResignedBadMagic_Throws**, **WordSenseTable_Read_ResignedBadCounts_Throws**,
  **WordSenseTable_Read_ResignedTrailingDataOrUndefinedFlags_Throws**,
  **WordSenseTable_Read_ResignedUndefinedPos_Throws**, **WordSenseTable_Read_ResignedUnorderedLemmas_Throws**:
  deterministic structural rejections; the body is crafted and re-signed with a valid digest so that only the
  structural check can reject it.
- **WordSenseTable_LemmaEntry_BadSenseDefinitions_Throw**, **WordSenseTable_LemmaEntry_Dictionary_FollowsDictionaryRules**,
  **LemmaEntry_Constructor_InvalidSenseDefinition_Throws**, **LemmaEntry_NonHeteronym_HasNoPronunciation**,
  **LemmaEntry_SenseSet_Rules**, **LemmaEntry_MissingRespelling_IsNull**,
  **LemmaEntry_Candidates_MapSensesToDetails**: bad sense definitions are rejected; a dictionary entry has one
  sense or several senses of distinct parts of speech and never a pronunciation; the sense-set rules, the
  respelling and the candidates mapping of `LemmaEntry`.
- **WordSenseTable_TryGetLemma_EdgeCases**: `TryGetLemma` is false for an unknown lemma, finds a dictionary entry and
  throws for null.
- **LexicalIndex_BuildAndFind_FindsEveryWord**, **LexicalIndex_TryFind_AbsentOrInvalid_ReturnsFalse**: every word of
  a built index is found with its senses; an absent word, an empty word and a word with characters other than a to z
  are reported as absent.
- **LexicalIndex_Build_InvalidInput_Throws**, **LexicalIndex_Decompress_InvalidData_Throws**: unsorted or duplicate
  words and invalid records are rejected when building; damaged compressed data is rejected when reading.
- **TryResolve_FormExactAndDictionary_Resolve**: a form resolves to its lemma, a context lemma resolves to itself and
  a dictionary word resolves to its entry; the form map wins over the dictionary.
- **TryResolve_Possessive_OnlyForWeightedEntries**: a possessive resolves only to a context entry.
- **TryResolve_Plural_GatedOnBase**, **TryResolve_PastTense_GatedOnVerbBase**,
  **TryResolve_Adverb_GatedOnAdjectiveOnlyBase**: each morphology rule applies only when its base satisfies the
  gate, narrows the entry to the derived sense and never replaces a word that is present.
- **TryResolve_DisabledRuleOrNoDictionary_DoesNotApply**: a disabled rule, or a table or call without a dictionary,
  derives nothing.
- **Table_CountLemmasAndContextLemmas_Differ**: the count of all entries includes the dictionary, the count of
  context lemmas does not.
- **SenseWeights_Score_Binary_UsesActivationSign**, **SenseWeights_Score_OneVersusRest_PicksBestRow**,
  **SenseWeights_Score_TiesAndZero_SelectDocumentedSense**: scoring; a binary activation of exactly 0 selects sense
  index 1 with margin 0, and with equal best activations the first highest row wins with margin 0.
- **SenseWeights_Scores_Binary_ReturnsNegatedAndActivation**,
  **SenseWeights_Scores_OneVersusRest_ReturnsPerRowActivation**, **SenseWeights_Scores_LengthMismatch_Throws**: the
  per-sense `Scores` output for binary and one-versus-rest models, and rejection of a wrong feature or scores
  length.
- **SenseWeights_Scores_OddDimension_MatchesScalarReference**: dimensions 19 and 37 (not a multiple of any SIMD
  width) give the same scores as a scalar reference.
- **SenseWeights_Constructor_BadShapes_Throw**: null, non-finite or wrongly shaped weights are rejected.
- **WordSenseWeightsTable_RoundTrip_PreservesAllValues**, **WordSenseWeightsTable_RoundTrip_Empty_Works**: lossless
  serialization of the model id, model and lexicon digests, dimension, mean and per-lemma weights, including a table
  with no lemmas.
- **WordSenseWeightsTable_Constructor_NormalizesDigestsAndCopiesMean**,
  **WordSenseWeightsTable_Constructor_InvalidArguments_Throw**, **WordSenseWeightsTable_ComputeSha256_ChangesWithContent**:
  digests are normalized and the mean is copied; invalid arguments are rejected; the digest of a table changes with
  its content.
- **WordSenseWeightsTable_Read_TruncatedAtEveryLength_Throws**, **WordSenseWeightsTable_Read_AnyByteFlipped_Throws**,
  **WordSenseWeightsTable_Read_Garbage_Throws**: integrity checks on the weights table.
- **WordSenseWeightsTable_Read_ResignedBadHeaderOrTrailingData_Throws**,
  **WordSenseWeightsTable_Read_ResignedBadCounts_Throws**, **WordSenseWeightsTable_Read_ResignedNonFiniteMean_Throws**,
  **WordSenseWeightsTable_Read_ResignedUnorderedLemmas_Throws**: crafted, correctly signed bodies that only a
  structural check can reject.
- **WordSenseWeightsTable_EnsureMatches_MatchingTable_DoesNotThrow**,
  **WordSenseWeightsTable_EnsureMatches_OtherLexicon_Throws**,
  **WordSenseWeightsTable_EnsureMatches_LemmaOrSenseCountMismatch_Throws**: a weights table is accepted by the lexicon
  it was trained for and refused when the lexicon digest, the lemma list or a sense count differs.
- **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**: the embedded resource loads with at least 344 lemmas, the
  expected parts of speech and meanings for `record`, a `read` lemma that has a past verb sense (meaning `past`, part
  of speech verb), and every form resolving to a context lemma.
- **WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations**: the embedded table has at least one
  heteronym, and every candidate of every heteronym has a pronunciation.
- **WordSenseTable_LoadEmbedded_ContainsDictionaryWords**: the embedded table has at least 100,000 dictionary words,
  common words such as `table` resolve, and no dictionary word has a pronunciation.
- **WordSenseTable_LoadEmbedded_RoundTrips**: the embedded lexicon loads and serializes back to the same content.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0 with no setup beyond it. The embedded
lexicon is a resource of the library; the unit tests need no model files, no network access and no external services.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

Each requirement is mapped to the scenarios above as follows.

- `WordSense-Table-Format`: the round-trip (with and without dictionary), determinism and compression scenarios and
  **WordSenseTable_Constructor_InconsistentInput_Throws**.
- `WordSense-Table-Senses`: **WordSenseTable_Read_OtherVersion_Throws**, the round-trip scenario, and the
  `LemmaEntry` scenarios.
- `WordSense-Table-Lexicon`: the lexical index build, find, absent-word, invalid-input and compression scenarios
  and the dictionary round trip.
- `WordSense-Table-Resolve`: the form, exact, dictionary, possessive and no-dictionary resolution scenarios, the
  count scenario and the `TryGetLemma` scenario.
- `WordSense-Table-Morphology`: the plural, past-tense and adverb gate scenarios and the disabled-rule scenario.
- `WordSense-Table-Integrity`: the truncation, byte-flip, garbage, weights-magic, mutated-body and every re-signed
  scenario, the damaged-compression scenario and the table constructor null and shadowing scenarios.
- `WordSense-Table-Score`: the `SenseWeights` scoring, `Scores`, tie, odd-dimension and bad-shape scenarios.
- `WordSense-Table-WeightsFormat`: the weights round-trip, empty, normalization, invalid-argument and digest
  scenarios.
- `WordSense-Table-WeightsIntegrity`: the weights truncation, byte-flip, garbage and re-signed scenarios.
- `WordSense-Table-WeightsMatch`: the three `EnsureMatches` scenarios.
- `WordSense-Table-Embedded`: **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**,
  **WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations**,
  **WordSenseTable_LoadEmbedded_ContainsDictionaryWords** and **WordSenseTable_LoadEmbedded_RoundTrips**.
