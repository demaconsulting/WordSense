### WordLexicon Verification

#### Verification Approach

The unit is verified with the real embedded word image. Tests in
`test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs` use the public `WordLexicon` lookup APIs (word lookup, sense
catalog, key lookup and group lookup). The image format itself (determinism, UTF-8 ordering, corruption, the
sense and group sections) is verified in the builder test project (`ImageTests.cs`) because the compiler lives in the
builder, so the shipped assembly has no writer. The committed image is compared with a fresh compilation of `data/`
(`BuilderTests.Words_Committed_*`). Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Known lemma has its analyses** (`Lookup_KnownWord_ReturnsItsAnalyses`): `Lookup("read")` holds analyses that
  share the lemma `read` (the past and the present); a single-analysis word holds one.
- **One analysis per lemma, part of speech and features**
  (`Lookup_FormWithManySenses_ReturnsOneAnalysisPerLemmaPartAndFeatures`): `Lookup("bank")` lists a noun and a verb
  analysis once each, however many senses each has; no analysis is repeated per sense.
- **Capitalized and inflected forms behave like their base**
  (`Lookup_CapitalizedAndInflectedForms_BehaveLikeTheirBase`): a capitalized spelling with no entry of its own gives
  the analyses of its lower-case base, and an inflected form (`using`, `left`, `paid`) gives the analyses generated for
  it.
- **Inflections are present** (`Lookup_InflectedForm_ReturnsEntryLemmaAndFeatures`,
  `Lookup_InflectedFormOfAnyDictionaryWord_ResolvesToLemma`): inflected forms such as `records` and `recorded` carry
  the lemma `record` and their features, and so do the forms of every dictionary word; `went`, `children` and
  `happier` are known; the wrong regular forms of irregular words (`goed`, `childs`) are absent
  (`Contains_IrregularFormInDictionary_IsKnown`, `Contains_WrongRegularFormOfIrregularWord_IsAbsent`).
- **Closed-class words are present** (`Lookup_ClosedClassWord_HasItsPartOfSpeechAlone`,
  `Lookup_SoAndWill_ListTheirParts`, `Lookup_FormWithSeveralLemmas_ListsEach`): `the`, `a`, `an`, `his` and `my` are
  single determiner entries, `so` and `will` list all their parts of speech, and a form with several lemmas lists
  each.
- **Read forms list their tenses** (`Lookup_ReadForms_HaveThePastAndThePresentAnalysis`): `read` has a past and a
  present analysis, `reads` the present one.
- **Auxiliaries end with an Auxiliary analysis** (`Lookup_Auxiliaries_HaveAnAuxiliaryAnalysis`): `will` is Verb,
  Noun, Auxiliary; `is` and `having` end with Auxiliary under the lemma `be`; `wills` and `record` have none.
- **Possessives and facts** (`Lookup_PossessiveOfKnownWord_AddsPossessiveFeature`,
  `Lookup_StoredContractionOrUnknownStem_IsNotPossessive`, `Lookup_Fact_Holds`, `LexiconFacts_Files_CoverEveryCategoryAndMode`,
  `Lookup_FormsLostByEarlierRebuild_ResolveToTheirLemma`): the fallback, and the fixed fact set in
  `Fixtures/lexicon-facts.tsv` (hand-observed rows) and `Fixtures/lexicon-facts-generated.tsv` (rows derived
  mechanically from the sources with a fixed seed: irregular lists, sampled regular verbs and nouns, possessives,
  contractions, -ing entries). A fact asserts that an analysis exists; the order of the analyses is not asserted. No
  fact is dropped for failing: a form is left out of the file only when the generator names it with a reason code.
  `Lookup_GerundWithOwnEntry_HasBothAnalyses` checks that a gerund has the verb analysis and the analysis of its own
  noun or adjective entry.
- **Forms are lower case** (`FormsOf_Forms_AreLowerCase`) and **case folds** (`Lookup_Case_FoldsToLowerCase`): an
  upper-case spelling finds the lower-case entry; an unknown word is absent (`Lookup_UnknownWord_IsAbsent`); a null
  word throws (`Lookup_NullWord_Throws`).
- **Tokens without a letter are unknown** (`Lookup_TokenWithoutLetter_IsAbsent`, `Lookup_WordWithALetter_IsStillFound`):
  `3`, `1999`, `0`, `24/7`, `.22`, `3.5` and `-` are absent from `TryLookup`, `Lookup`, `Contains` and `FormsOf`
  (the image holds entries such as `3` that must not show), while `three`, `Twenty` and `3rd` are found.
- **Forms of a lemma** (`FormsOf_Lemma_ListsItsForms`): the forms of `record` are listed.
- **Senses of a lemma and part of speech** (`SensesOf_LemmaAndPartOfSpeech_ListsInOewnOrder`,
  `SensesOf_SingleSenseLemma_ListsOneSense`, `SensesOf_UnknownLemma_IsEmpty`, `SensesOf_NullLemma_Throws`): the senses
  of `bank` as a noun are listed in the order of Open English WordNet (sloping land first), a lemma and part of speech
  with one sense lists one, an
  unknown lemma lists none and a null lemma throws.
- **Catalog is complete** (`SensesOf_EverySingleTokenOewnSense_IsInTheCatalog`,
  `SensesOf_ProjectSenses_AreInTheCatalog`): every single-token OEWN sense and every project sense of the data is
  found in the catalog through its lemma and part of speech.
- **Sense lookup by id and by key** (`TryGetSense_ById_FindsSense`, `TryGetSense_UnknownId_IsFalse`,
  `TryGetSenseByKey_FindsTheSameSenseAsItsId`, `TryGetSenseByKey_UnknownKey_IsFalse`,
  `TryGetSenseByKey_ProjectSense_IsNeverFound`): a sense found by its id equals the sense found by its OEWN key; an
  unknown id and an unknown key are absent; a project sense has no key and is only found by its id.
- **Groups** (`TryGetGroup_ById_ListsMemberSenses`, `TryGetGroup_UnknownId_IsFalse`,
  `Groups_EverySense_BelongsToAtMostOneGroup`, `Groups_EveryGroup_HasOneLemmaAndOnePartOfSpeech`,
  `Groups_EveryGroup_HasAtLeastTwoSensesAndNotAllOfThem`, `Groups_EveryGroupId_HasGroupMarkerAndNeverEqualsASenseId`): a
  group lists its senses in inventory order; every sense of the catalog is in at
  most one group (a sense in no group has a null group id), every group has one lemma and one part of speech, every
  group has at least two senses and never all the senses of its lemma and part of speech, and every group id has the
  `#group:` marker and is never equal to a sense id.
- **Version** (`Version_IsStableLowerCaseHex`): `WordLexicon.Version` is the same on every call and is 16 lower-case
  hexadecimal digits that equal the first eight bytes of the inventory hash of the opened image.
- **Embedded image and lazy loading** (`Count_EmbeddedImage_LoadsAndIsNotEmpty`,
  `Lookup_ConcurrentAccess_ReturnsSameAnswers`): the image loads on first use and many tasks that access it at the
  same moment receive the same answers.
- **Image order and lookup** (`Image_Spellings_AreSortedByUtf8Bytes`, `Image_Find_ReturnsOwnIndex`): the spelling index
  is sorted by UTF-8 bytes and every spelling is found at its own index.
- **Corrupt image is rejected** (`Image_Corruption_IsRejected`): a wrong magic number, an unsupported version, a
  flipped byte and a truncated image each throw `InvalidDataException`.
- **Image failure matrix** (`Image_TruncatedAtEverySectionBoundary_IsRejected`, `Image_WrongHash_IsRejectedByContentHashOnly`,
  `Image_CountMismatch_IsRejected`, `Image_SenseWithUnknownGroup_IsRejected`,
  `Image_GroupOfOneSenseOrAllSenses_IsRejected`, `Image_GroupIdWithoutMarker_IsRejected`,
  `Image_DuplicateSenseId_IsRejected`): an
  image cut at the header, the directory and each section boundary is rejected with and without the hash check; a wrong
  content hash is rejected while the source data hash is informational; a section whose directory length disagrees
  with its content, or a directory with too many sections, is rejected; a sense that names a group that does not exist
  and a duplicate sense id are rejected; a group id without the `#group:` marker, a group with fewer than two members,
  a group of all the senses of its lemma
  and part of speech, a group that mixes lemmas or parts of speech and group members that disagree with the
  sense-to-group index are rejected.
- **Catalog invariants beyond the structure** (`WordLexicon_Image_GroupListingASenseTwice_IsRejected`,
  `WordLexicon_Image_KeyIndexOutOfOrder_IsRejected`, `WordLexicon_Image_KeyIndexWithDuplicate_IsRejected`,
  `WordLexicon_Image_RebuiltWithoutDamage_IsAccepted`): tests in the core test project rebuild the embedded image with
  one raw section changed. A group that lists the same sense twice, and a key index with two entries swapped or with a
  repeated entry, are rejected when the catalog is first read (not only by a validation tool), while the same rebuild
  without damage is accepted.
- **Complete dictionary**: the builder tests `Words_Committed_HoldsEveryDictionaryWord` and
  `Words_Committed_CatalogHoldsEveryOewnAndProjectSense` show that no dictionary word and no sense is absent from the
  image.
