### WordLexicon Verification

#### Verification Approach

The unit is verified with the real embedded word image. Tests in
`test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs` use the public `WordLexicon` lookup APIs (word lookup, sense
enumeration and class-id resolution). The image format itself (determinism, UTF-8 ordering,
corruption) is verified in the builder test project (`ImageTests.cs`) because the compiler lives in the builder, so the
shipped assembly has no writer. The committed image is compared with a fresh compilation of `data/` and with the
legacy word table (`BuilderTests.Words_Committed_*`). Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Known lemma has its analyses** (`Lookup_KnownWord_ReturnsItsAnalyses`): `Lookup("read")` holds analyses that
  share the lemma `read`; a single-sense word holds one.
- **Inflections are present** (`Lookup_InflectedForm_ReturnsEntryLemmaAndFeatures`,
  `Lookup_InflectedFormOfAnyDictionaryWord_ResolvesToLemma`): inflected forms such as `records` and `recorded` carry
  the lemma `record` and their features, and so do the forms of every dictionary word; `went`, `children` and
  `happier` are known; the wrong regular forms of irregular words (`goed`, `childs`) are absent
  (`Contains_IrregularFormInDictionary_IsKnown`, `Contains_WrongRegularFormOfIrregularWord_IsAbsent`).
- **Closed-class words are present** (`Lookup_ClosedClassWord_HasItsPartOfSpeechAlone`,
  `Lookup_SoAndWill_ListTheirParts`, `Lookup_FormWithSeveralLemmas_ListsEach`): `the`, `a`, `an`, `his` and `my` are
  single determiner entries, `so` and `will` list all their parts of speech, and a form with several lemmas lists
  each.
- **Auxiliaries end with an Auxiliary analysis** (`Lookup_Auxiliaries_HaveTrailingAuxiliaryAnalysis`): `will` is Verb,
  Noun, Auxiliary; `is` and `having` end with Auxiliary under the lemma `be`; `wills` and `record` have none.
- **Listed forms have only their labeled senses** (`Lookup_FormOfHeteronym_HasOnlyItsSenses`,
  `Lookup_ReadForms_SplitBySense`, `Lookup_RestrictedForm_NeverOffersExcludedSense`): `bowed` has only the bend verb
  (plus an untrained own extra of the form), `bows` its labeled senses, `reads` the present verb, and `read` the past
  and present verbs; no sense outside the labels appears.
- **Public lookup of forms with a different entry lemma** (`Lookup_FormsWithTrainedLemmaDifferentFromEntry_UseFixedLemmas`):
  `Lookup("wicks")` gives the verb lemma `wick` and never `wicked`, `Lookup("re-signed")` gives the lemma `re-sign` and
  `Lookup("axes")` is found; only the public lookup APIs are used.
- **Possessives and facts** (`Lookup_PossessiveOfKnownWord_AddsPossessiveFeature`,
  `Lookup_StoredContractionOrUnknownStem_IsNotPossessive`, `Lookup_Fact_Holds`, `LexiconFacts_File_CoversEveryCategory`,
  `Lookup_FormsLostByEarlierRebuild_ResolveToTheirLemma`): the fallback, and the fixed fact set in
  `Fixtures/lexicon-facts.tsv` (irregulars, participles and gerunds, possessives, contractions, lost words).
- **Forms are lower case** (`FormsOf_Forms_AreLowerCase`) and **case folds** (`Lookup_Case_FoldsToLowerCase`): an
  upper-case spelling finds the lower-case entry; an unknown word is absent (`Lookup_UnknownWord_IsAbsent`); a null
  word throws (`Lookup_NullWord_Throws`).
- **Forms of a lemma** (`FormsOf_Lemma_ListsItsForms`): the forms of `record` are listed.
- **Embedded image and lazy loading** (`Count_EmbeddedImage_LoadsAndIsNotEmpty`,
  `Lookup_ConcurrentAccess_ReturnsSameAnswers`): the image loads on first use and many tasks that access it at the
  same moment receive the same answers.
- **Image order and lookup** (`Image_Spellings_AreSortedByUtf8Bytes`, `Image_Find_ReturnsOwnIndex`): the spelling index
  is sorted by UTF-8 bytes and every spelling is found at its own index.
- **Corrupt image is rejected** (`Image_Corruption_IsRejected`): a wrong magic number, an unsupported version, a
  flipped byte and a truncated image each throw `InvalidDataException`.
- **Image failure matrix** (`Image_TruncatedAtEverySectionBoundary_IsRejected`, `Image_WrongHash_IsRejectedByContentHashOnly`,
  `Image_CountMismatch_IsRejected`): an image cut at the header, the directory and each section boundary is rejected
  with and without the hash check; a wrong content hash is rejected while the source data hash is informational; a
  section whose directory length disagrees with its content, or a directory with too many sections, is rejected.
- **Complete dictionary**: the builder test `Words_Committed_HoldsEveryDictionaryWord` shows that no dictionary word
  is absent from the image.
