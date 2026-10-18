## WordData Subsystem Verification Design

This document describes the verification of the word data subsystem: the importer, the validator, the schema and
layout code of the builder, and the committed tables under `data/`.

### Verification Approach

The units are verified in `test/WordSense.Builder.Tests/Data` with a small hand-written OEWN slice
(`Fixtures/oewn-slice.xml`: homographs, a satellite adjective, multiword and apostrophe lemmas, non-ASCII and astral
lemmas, `other` relations with subtypes, symmetric relations and a missing inverse). Importer tests import the slice
into a temporary folder; validator tests damage one thing of an imported copy and assert the rule tag. The end-to-end
tests run the commands through the builder entry point and validate the committed `data/` folder. No network and no
OEWN file are needed.

### Test Environment

Standard xUnit v3 runner (builder tests, net10.0). Temporary folders only; the committed data is read, never written.

### Acceptance Criteria

All scenarios pass; the committed `data/` folder validates with no violation.

### Test Scenarios

#### Import and Validate

The slice imports to the expected row counts, validates, and is byte-identical on a second import
(`ImportThenValidate_Succeeds`, `Import_Twice_IsByteIdentical`).

#### Damaged Data Is Reported

Each rule of the validator has a test with a damaged copy (`DataValidatorTests`); the command lists the violations and
returns failure (`Validate_InvalidData_ListsViolations`).

#### Committed Data and Attribution

`CommittedData_IsValid` validates the real tables; `Attribution_NamesLicenseAndSource` checks that README, the data
README and the provenance record name Open English WordNet and CC BY 4.0.

The scenarios are detailed in the unit documents.
