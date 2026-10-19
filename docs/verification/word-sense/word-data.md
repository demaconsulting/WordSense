## WordData Subsystem Verification Design

This document describes the verification of the word data subsystem: the importer, the label generator, the validator,
the schema and layout code of the builder, and the committed tables under `data/`.

### Verification Approach

The units are verified in `test/WordSense.Builder.Tests/Data` with a small hand-written OEWN slice
(`Fixtures/oewn-slice.xml`: homographs, a satellite adjective, multiword and apostrophe lemmas, non-ASCII and astral
lemmas, `other` relations with subtypes, symmetric relations and a missing inverse) and a small WordNet 3.0 count file.
Importer tests import the slice into a temporary folder; label generator tests build small tables; validator tests
damage one thing of an imported copy and assert the rule tag. The end-to-end tests run the commands through the builder
entry point and validate the committed `data/` folder, and check on it the properties that only the whole data can show:
every sense has one unique label and a frequency, the groups partition their lemmas, project senses have no key and an
authored gloss, closed-class analyses have a project sense, and the legacy map is complete. No network and no OEWN file
are needed.

### Test Environment

Standard xUnit v3 runner (builder tests, net10.0). Temporary folders only; the committed data is read, never written.

### Acceptance Criteria

All scenarios pass; the committed `data/` folder validates with no violation.

### Test Scenarios

#### Import and Validate

The slice imports to the expected row counts, validates, and is byte-identical on a second import
(`ImportThenValidate_Succeeds`, `Import_Twice_IsByteIdentical`). The count import joins the counts to the slice's
senses and writes the table (`ImportWn30Counts_WritesTheTable`).

#### Damaged Data Is Reported

Each rule of the validator has a test with a damaged copy (`DataValidatorTests`); the command lists the violations and
returns failure (`Validate_InvalidData_ListsViolations`).

#### Committed Data and Attribution

`CommittedData_IsValid` validates the real tables; `Attribution_NamesLicenseAndSource` checks that README, the data
README and the provenance record name Open English WordNet and CC BY 4.0, and
`Attribution_NamesWordNetCountsAndSemCorUse` checks that they also name WordNet 3.0 as the source of the counts and
SemCor as an evaluation source only.

#### Senses

`CommittedData_HasSenseLabelsAndGroups` checks that the real tables carry labels and groups;
`CommittedData_EverySenseHasOneUniqueLabel` checks one label per sense and no label twice within a lemma;
`CommittedData_PublishedLabelsAreNeverChangedOrReused` checks the published labels against the id map.

#### Groups

`CommittedData_GroupsPartitionTheirLemmas` checks that every sense of a curated lemma is in exactly one group of one
part of speech without two respellings, and `CommittedData_OtherLemmasHaveOneDefaultGroupPerPartOfSpeech` that every
other lemma has one default group for each of its parts of speech.

#### Frequency

`CommittedData_EverySenseHasAFrequency` checks that every sense key has a count, 0 where the counts file has none, with
the source named.

#### Project Senses

`CommittedData_ProjectSensesHaveNoKeyAndAGloss` checks that a project sense has no sense key and an authored gloss;
`CommittedData_ClosedClassAnalysesHaveAProjectSense` checks that every determiner, pronoun, preposition, conjunction,
interjection and auxiliary analysis has one.

#### Legacy Map

`CommittedData_LegacyMapCoversEveryRetiredClass` checks that every retired class id has exactly one target of the stated
level in the committed data.

The scenarios are detailed in the unit documents.
