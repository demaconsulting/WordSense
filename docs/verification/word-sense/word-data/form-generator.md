### FormGenerator Verification

#### Verification Approach

`FormGeneratorDataTests.cs` reads the committed form table and checks the policy on it (provenance values, regular
forms stay resolved, -ing forms lead with the verb). `InflectionRulesTests.cs` checks the spelling rules directly.
The lexicon facts (irregulars, participles, gerunds, possessives, contractions, previously lost words) are checked
through the shipped image in `LexiconFactTests.cs` of the core test project. The quality of the generated rows is
measured by the `audit-forms` command (see FormAuditor).

#### Test Environment

Standard xUnit v3 runner; the committed data; no network.

#### Acceptance Criteria

All scenarios pass.

#### Test Scenarios

- **Provenance** (`Data_Provenance_IsKnown`): every row has a known source.
- **Regular forms** (`Data_RegularInflection_IsResolved`): tossed, brothers, spilled, others.
- **Gerunds** (`Data_IngForm_LeadsWithVerb`, `Data_IngForm_IsNotPluralNoun`, `Data_IngNounPlural_IsNotTheSingular`).
- **Reproducibility** (`FormReproducibilityTests.cs`): the committed tables are the regeneration of the committed
  inputs (`Verify_CommittedTables_AreTheRegeneration`); other inputs are reported as differences
  (`Verify_OtherInputs_ReportsDifferences`); an input that does not match its pin is refused
  (`Run_UnpinnedInput_IsRefused`); `verify-forms` fails with a message on a missing input
  (`Program_VerifyForms_MissingInput_Fails`). The pinned Open English WordNet and AGID files are not committed: the
  regeneration test reads them from `artifacts/inputs` (or `WORDSENSE_OEWN` and `WORDSENSE_AGID`) and is skipped with
  a message when they are absent, unless `WORDSENSE_REQUIRE_FORM_INPUTS` is `true`, in which case it fails. CI downloads
  the pinned files and runs `verify-forms`, which fails on a missing input or a difference.
- **Own entries kept** (`Check_AllFactsAndIngEntriesHold`): every generated fact holds and every -ing noun or
  adjective entry of the dictionary has its own analysis.
- **Fact generation** (`Generate_IsDeterministicForSeed`, `Generate_MatchesCommittedFile`): the same seed gives the same
  facts and the committed generated fact file is the output of the command.
- **Rules** (`NounPlural_Regular_AddsSuffix`, `VerbThirdPerson_Regular_AddsSuffix`, `VerbRegular_Spellings`,
  `AdjectiveDegrees_ShortAndLong`, `ReverseCandidates_Plural_ContainsSingular`).
