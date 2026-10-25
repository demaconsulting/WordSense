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
every sense has one unique label and a frequency, the groups are sets of some of the senses of one part of speech, project
senses have no key and an authored gloss, and closed-class analyses (the conjunction `or` and the auxiliaries included)
have a project sense. No network and no OEWN file
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

`CommittedData_IsValid` validates the real tables; `CommittedData_HasNoRetiredClassTables` checks that the class tables
and the form scopes of the retired inventory are gone; `Attribution_NamesLicenseAndSource` checks that README, the data
README and the provenance record name Open English WordNet and CC BY 4.0, and
`Attribution_NamesWordNetCountsAndSemCorUse` checks that they also name WordNet 3.0 as the source of the counts and
SemCor as an evaluation source only.

#### Senses

`CommittedData_HasSenseLabelsAndGroups` checks that the real tables carry labels and the few curated groups, with the
two group kinds;
`CommittedData_EverySenseHasOneUniqueLabel` checks one label per sense and no label twice within a lemma;
`CommittedData_PublishedLabelsAreNeverChangedOrReused` checks the published sense labels against the id map (no release
has published labels yet, so the data is its own baseline, and no retired id of the id map may be a current sense id;
group ids are not part of this check, because they are versioned per data release).

#### Groups

`CommittedData_GroupsAreSetsOfSomeSensesOfOnePartOfSpeech` checks that every group has at least two, but not all, senses
of its lemma and part of speech, only senses of that lemma and part of speech, and that no sense is
in two groups; `CommittedData_GroupsHaveAMarkerIdAKindAndCuratedEvidence` checks that every group id is
`lemma#group:label` with a slug label and is never a sense id, that every kind is `equivalent` or `related`, and that the
curation file carries the same kind and non-empty evidence for the group; `CommittedData_SensesNeedNoGroup` checks that
almost every sense is in no group, that only the lemmas of the curation have groups and that no group has the name of a
retired default group (`-group`).

#### Frequency

`CommittedData_EveryKeyedSenseHasAFrequency` checks that every sense key has a count, 0 where the counts file has none,
with
the source named.

#### Project Senses

`CommittedData_ProjectSensesHaveNoKeyAndAGloss` checks that a project sense has no sense key and an authored gloss;
`CommittedData_ClosedClassAnalysesHaveAProjectSense` checks that every determiner, pronoun, preposition, conjunction,
interjection and auxiliary analysis has one; `CommittedData_OrAndTheAuxiliariesHaveClosedClassAnalyses` checks that the
spelling `or` leads with the conjunction `or#conjunction` and that every auxiliary of
`corpus/lexicon/auxiliaries.tsv` has its entry, the project sense `<word>#auxiliary` and an Auxiliary analysis.

The scenarios are detailed in the unit documents.
