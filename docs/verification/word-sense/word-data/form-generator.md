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
- **Gerunds** (`Data_IngForm_LeadsWithVerb`, `Data_IngForm_IsNotPluralNoun`).
- **Rules** (`NounPlural_Regular_AddsSuffix`, `VerbThirdPerson_Regular_AddsSuffix`, `VerbRegular_Spellings`,
  `AdjectiveDegrees_ShortAndLong`, `ReverseCandidates_Plural_ContainsSingular`).
