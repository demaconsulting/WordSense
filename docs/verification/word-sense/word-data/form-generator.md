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
- **Reproducibility** (`FormReproducibilityTests.cs`): on the small committed Open English WordNet slice and a small
  inflection list, two runs give identical bytes (`Run_SameInputs_GiveIdenticalBytes`) and a run from a differently
  ordered input gives the same bytes (`Run_ReorderedInput_GivesIdenticalBytes`); an input that does not match its pin
  is refused (`Run_UnpinnedInput_IsRefused`); a reviewed input that repeats an analysis (the same spelling, entry and
  features on two lines) is refused with both line numbers (`Run_RepeatedFrozenAnalysis_IsRefused`), and the committed
  reviewed input is accepted by every run above. No download is needed. Drift of the committed tables against the pinned
  files is seen by rerunning `rebuild-forms` (see `data/PROVENANCE.md`) and reading `git diff`.
- **Own entries kept** (`Check_AllFactsAndIngEntriesHold`): every generated fact holds and every -ing noun or
  adjective entry of the dictionary has its own analysis.
- **Fact generation** (`Generate_IsDeterministicForSeed`, `Generate_MatchesCommittedFile`): the same seed gives the same
  facts and the committed generated fact file is the output of the command.
- **Rules** (`NounPlural_Regular_AddsSuffix`, `VerbThirdPerson_Regular_AddsSuffix`, `VerbRegular_Spellings`,
  `AdjectiveDegrees_ShortAndLong`, `ReverseCandidates_Plural_ContainsSingular`).
