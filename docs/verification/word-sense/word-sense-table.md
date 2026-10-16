# WordSenseTable Verification

## Verification Approach

Tests in `WordSenseTableTests.cs`, `LexicalIndexTests.cs` and `LemmaResolutionTests.cs` build small tables with and
without a dictionary in memory, serialize and parse them, resolve words through the single resolver and damage the
bytes systematically. The embedded table is checked for its lemma count, parts of speech, meanings, forms, dimension
and dictionary size.

## Test Scenarios

- **WordSenseTable_RoundTrip_PreservesAllValues**, **WordSenseTable_ToArray_CalledTwice_ReturnsIdenticalBytes**,
  **WordSenseTable_Write_Stream_MatchesToArray**: lossless and deterministic serialization.
- **WordSenseTable_RoundTrip_WithDictionary_PreservesLemmasAndMorphology**: a table with weight-less entries and
  enabled morphology rules reads back with the same lemmas, senses and rule flags.
- **LexicalIndex_CompressDecompress_RoundTrips**: the front-coded dictionary section survives compression and
  decompression unchanged.
- **WordSenseTable_Constructor_InconsistentInput_Throws**, **WordSenseTable_Weights_BadShapes_Throw**:
  structural validation.
- **WordSenseTable_Read_TruncatedAtEveryLength_Throws**, **WordSenseTable_Read_AnyByteFlipped_Throws**,
  **WordSenseTable_Read_Garbage_Throws**: integrity checks.
- **WordSenseTable_Read_MutatedBodyWithValidChecksum_ThrowsFormatExceptionOrLoads**: every single-byte
  mutation of the body, re-signed with a valid digest, is either rejected with
  `WordSenseTableFormatException` or loads into a table that serializes; no other exception occurs.
- **WordSenseTable_Read_ResignedBadMagic_Throws**, **WordSenseTable_Read_ResignedTrailingData_Throws**,
  **WordSenseTable_Read_ResignedDimensionAboveCap_Throws**,
  **WordSenseTable_Read_ResignedUnknownModelKind_Throws**: deterministic structural rejections; the body is
  crafted and re-signed with a valid digest so that only the structural check can reject it.
- **WordSenseTable_Read_ResignedLemmaCountAboveCap_Throws**,
  **WordSenseTable_Read_ResignedFormCountAboveCap_Throws**,
  **WordSenseTable_Read_ResignedSenseCountOutOfRange_Throws**,
  **WordSenseTable_Read_ResignedUndefinedPos_Throws**,
  **WordSenseTable_Read_ResignedInvalidMeaning_Throws** and
  **WordSenseTable_Read_ResignedBadFormEntry_Throws**: the remaining caps and structural checks, with crafted bodies
  re-signed in the same way.
- **WordSenseTable_Read_ResignedValidDictionary_Loads**: a crafted, correctly signed table with a dictionary
  section loads.
- **WordSenseTable_Read_ResignedBadDictionaryRecords_Throws**,
  **WordSenseTable_Read_ResignedBadDictionarySection_Throws**: a dictionary record that is not valid (unsorted,
  empty, with bad characters or an invalid sense set) and a damaged section (wrong lengths, a decompressed size above
  the cap, trailing bytes) are rejected.
- **WordSenseTable_Read_ResignedShadowing_Throws**, **WordSenseTable_Read_ResignedUndefinedMorphologyRule_Throws**:
  a dictionary word that is also a trained lemma or a form is rejected, and so is a rule flag that is not defined.
- **WordSenseTable_Constructor_NullArguments_Throw**, **WordSenseTable_Constructor_InvalidValues_Throw**,
  **WordSenseTable_Constructor_ShadowingAndBadDictionaryWords_Throw**: null arguments, a malformed digest, a
  non-finite or wrong-length mean, duplicate lemmas, a dimension mismatch, a shadowing dictionary word and a bad
  dictionary word are rejected.
- **WordSenseTable_Weights_NullAndNonFiniteArguments_Throw**, **WordSenseTable_Weights_UndefinedKind_Throws**:
  null arguments, non-finite bias or abstain margin, a bias of the wrong length and an undefined model kind are
  rejected by the weights type.
- **LexicalIndex_BuildAndFind_FindsEveryWord**, **LexicalIndex_TryFind_AbsentOrInvalid_ReturnsFalse**: every word of
  a built index is found with its senses; an absent word, an empty word and a word with characters other than a to z
  are reported as absent.
- **LexicalIndex_Build_InvalidInput_Throws**, **LexicalIndex_Decompress_InvalidData_Throws**: unsorted or duplicate
  words and invalid records are rejected when building; damaged compressed data is rejected when reading.
- **TryResolve_FormExactAndDictionary_Resolve**: a form resolves to its lemma, a trained lemma resolves to itself and
  a dictionary word resolves to its entry; the form map wins over the dictionary.
- **TryResolve_Possessive_OnlyForWeightedEntries**: a possessive resolves only to an entry with weights.
- **TryResolve_Plural_GatedOnBase**, **TryResolve_PastTense_GatedOnVerbBase**,
  **TryResolve_Adverb_GatedOnAdjectiveOnlyBase**: each morphology rule applies only when its base satisfies the
  gate, narrows the entry to the derived sense and never replaces a word that is present.
- **TryResolve_DisabledRuleOrNoDictionary_DoesNotApply**: a disabled rule, or a table or call without a dictionary,
  derives nothing.
- **Table_CountLemmasAndContextLemmas_Differ**: the count of all entries includes the dictionary, the count of
  trained lemmas does not.
- **WordSenseTable_Scores_OddDimension_MatchesScalarReference**: dimensions 19 and 37 (not a multiple of any SIMD
  width) give the same scores as a scalar reference.
- **WordSenseTable_Score_Binary_UsesActivationSign**, **WordSenseTable_Score_OneVersusRest_PicksBestRow**:
  scoring.
- **WordSenseTable_Score_BinaryZeroActivation_SelectsSecondSense**: a binary activation of exactly 0 selects sense
  index 1 with a margin of 0. **WordSenseTable_Score_OneVersusRestTie_SelectsFirstHighest**: with equal best
  activations the first highest row wins and the margin is 0.
- **WordSenseTable_Scores_Binary_ReturnsNegatedAndActivation**,
  **WordSenseTable_Scores_OneVersusRest_ReturnsPerRowActivation**,
  **WordSenseTable_Scores_LengthMismatch_Throws**: the public per-sense `Scores` output for binary and
  one-versus-rest models, and rejection of a wrong feature or scores length.
- **WordSenseTable_Read_OldVersion_Throws**, **WordSenseTable_RoundTrip_PreservesPronunciations**,
  **WordSenseTable_LemmaEntry_BadSenseDefinitions_Throw**: versions 1 to 4 are rejected (only version 5 is
  accepted) with a message that mentions the version, bad sense definitions are rejected, and parts of speech,
  meanings and respellings survive a round trip.
- **WordSenseTable_Read_ResignedNewerVersion_Throws**: a correctly signed table with a newer version is rejected
  with `WordSenseTableFormatException` whose message mentions the version.
  **WordSenseTable_Read_ResignedBinaryKindWithThreeSenses_Throws**: a correctly signed table whose three-sense
  lemma has its kind byte changed to binary is rejected with a message containing "inconsistent".
  **WordSenseTable_Read_ResignedInconsistentSenseSet_Throws**: a correctly signed table in which one meaning is
  altered to equal the other, so one meaning has two respellings, is rejected with a message containing
  "inconsistent" and an `ArgumentException` as inner exception.
- **WordSenseTable_LemmaEntry_WithoutWeights_FollowsDictionaryRules**: an entry without weights has one sense, or
  several senses of distinct parts of speech, and never a pronunciation.
- **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**: the embedded resource loads with at least 344
  lemmas, the expected parts of speech and meanings for `record`, a `read` lemma that has a past verb sense
  (meaning `past`, part of speech verb), dimension 384, and every form resolving to a
  lemma with a model.
- **WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations**: the embedded table has at least one
  heteronym, and every candidate of every heteronym has a pronunciation.
- **WordSenseTable_LoadEmbedded_ContainsDictionaryWords**: the embedded table has at least 100,000 dictionary words,
  common words such as `table` resolve, and no dictionary word has a pronunciation.
- **WordSenseTable_RoundTrip_PreservesAllValues** also asserts that the candidates of a read lemma are shared
  (the same list and the same first element on every access) and carry the lemma and part of speech of their
  sense.
- **WordSenseTable_WriteAndTryGetLemma_EdgeCases**: writing a string over 65,535 bytes throws
  `InvalidOperationException`; `TryGetLemma` is false for an unknown lemma, finds a dictionary entry and throws for
  null.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0 with no setup beyond it. The embedded
table is a resource of the library; the unit tests need no model files, no network access and no external services.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

Each requirement is mapped to the scenarios above as follows.

- `WordSense-Table-Format`: the round-trip (with and without dictionary), determinism, stream and compression
  scenarios, **WordSenseTable_Constructor_InconsistentInput_Throws**,
  **WordSenseTable_Weights_BadShapes_Throw** and **WordSenseTable_WriteAndTryGetLemma_EdgeCases**.
- `WordSense-Table-Senses`: the pronunciation round-trip, old-version, newer-version, bad sense definition,
  weight-less entry, null or non-finite weights argument and undefined-kind scenarios.
- `WordSense-Table-Lexicon`: the lexical index build, find, absent-word, invalid-input and compression scenarios
  and the dictionary round trip.
- `WordSense-Table-Resolve`: the form, exact, dictionary, possessive and no-dictionary resolution scenarios, the
  count scenario and the `TryGetLemma` scenario.
- `WordSense-Table-Morphology`: the plural, past-tense and adverb gate scenarios, the disabled-rule scenario and
  the undefined-rule-flag scenario.
- `WordSense-Table-Integrity`: the truncation, byte-flip, garbage, mutated-body and every re-signed scenario
  (magic, trailing data, caps, sense count, part of speech, meaning, form entries, model kind, binary kind with
  three senses, inconsistent sense set, dictionary records and section, shadowing), the damaged-compression
  scenario and the table constructor null, invalid-value and shadowing scenarios.
- `WordSense-Table-Score`: the binary and one-versus-rest scoring scenarios, the zero-activation and tie scenarios,
  the per-sense `Scores` scenarios, the length-mismatch scenario and the odd-dimension scalar-reference scenario.
- `WordSense-Table-Embedded`: **WordSenseTable_LoadEmbedded_ContainsSeedLemmas**,
  **WordSenseTable_LoadEmbedded_HeteronymSensesHavePronunciations** and
  **WordSenseTable_LoadEmbedded_ContainsDictionaryWords**.
