### SenseLabelGenerator Verification

#### Verification Approach

Tests in `test/WordSense.Builder.Tests/Data/SenseLabelGeneratorTests.cs` build small tables in a temporary folder (a few
lemmas with one sense, several senses, a synset with synonyms, a synset with only a hypernym, and clashing slugs), run
the generator and read back the `sense_label` table. The pure computation is tested directly on the loaded tables; the
file level is tested through `Run`. Reproducibility tests run the generator twice and from a differently ordered copy of
the same input and compare the bytes. No network and no OEWN file are needed.

#### Test Environment

Standard xUnit v3 runner; temporary files.

#### Acceptance Criteria

All scenarios below pass.

#### Test Scenarios

- **Only sense of a part of speech** (`Generate_OnlySenseOfPartOfSpeech_UsesThePartOfSpeechName`): a lemma and part of
  speech with one sense is labeled with the part of speech name (`lemma#noun`).
- **First synonym** (`Generate_UsesTheFirstSynonym`): a sense of a synset with a synonym takes the slug of the first
  synonym, and a synonym that slugs to the lemma itself is skipped.
- **Hypernym head** (`Generate_NoSynonym_UsesTheHypernymHead`, `Generate_FirstHypernymMemberIsSelf_UsesNextMemberOfThatHypernym`):
  a sense with no usable synonym takes the first usable member of its first hypernym synset; when the first member is
  the lemma itself the next member of the same synset is used, not another hypernym.
- **Duplicates** (`Generate_DuplicateLabels_GetNumericSuffixes`): two senses with the same slug are labeled `slug` and
  `slug-2` in labeling order (the WordNet 3.0 count decides); a third gets `slug-3`.
- **Labeling order** (`Generate_LabelingOrder_FollowsPartOfSpeechCountAndSenseIdNotTheListedOrder`): senses that are listed
  in an order different from the labeling order (verbs first, nouns with the same count in descending id order) are
  labeled in part of speech order, then descending count, then sense id, so the label follows the counts and not the
  position of the sense in the input (the order of Open English WordNet).
- **Well-formed labels** (`Generate_LabelsAreWellFormedAndUniquePerLemma`): spaces, apostrophes, capitals and non-ASCII
  letters in a candidate give a label of lower-case ASCII letters, digits and hyphens, and no two labels of a lemma
  are equal.
- **Existing labels are kept** (`Generate_ExistingLabels_AreKept`, `Run_ExistingLabels_KeepValueAndOrigin`): curated and
  generated rows come back unchanged, with their value and origin, exactly once in the written table.
- **Never a used or retired label** (`Generate_NewSenseOfLemma_NeverTakesAnExistingOrRetiredLabel`): a new sense of a
  lemma whose slug equals a label in use, or the label of an old id of `sense_label_map`, receives a suffix.
- **Source data change** (`Generate_ChangedSourceData_KeepsPublishedLabels`): after the synonym or hypernym of a sense
  has changed, a second run leaves its published label as it was.
- **Project senses and groups** (`Generate_ProjectSenseAndMultiWordLemma_GetTheRightOrigin`,
  `Run_Slice_LabelsOneTokenSensesAndCreatesNoGroup`, `Run_ExistingGroup_IsKeptAndItsLabelStaysAvailable`,
  `Slug_GivesLowerCaseAsciiWords`, `Generate_EqualExistingLabels_Throws`, `Run_MissingInputTable_Throws`,
  `Run_UnreadableRows_ThrowInvalidDataWithLocation`, `Run_NegativeCounts_ThrowInvalidDataWithLocation`): a sense without
  a key gets the origin `project`, a multi-word lemma gets no label, the run creates no group and leaves an existing one
  as it is, the label of a group (`lemma#group:label`, its own namespace) does not stop a sense from having the same
  label, and a damaged input throws (a count that is not a number and a negative frequency or token count throw
  `InvalidDataException` that names the file, the line and the text, and nothing is written).
- **Same inputs, same bytes** (`Run_SameInputs_GiveIdenticalBytes`): two runs give identical files.
- **Reordered input** (`Run_ReorderedInput_GivesIdenticalBytes`): a copy whose input rows are in another order gives
  the same bytes.
