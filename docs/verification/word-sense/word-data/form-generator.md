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
- **Authored forms** (`WordSenseBuilder_AuthoredForms_*`, `OpenCorpusAttestation_*`,
  `OpenCorpus_ReadFile_TokenWithEmptyField_*`, `FormGenerator_Run_Authored*`, `FormGenerator_Run_AgreedNoPlural_*`,
  `Data_AuthoredForm_ShipsWithTheProvenanceLlm`, `Data_AuthoredPlural_LeavesOutTheRegularSpelling`): the reader keys by
  lowercase lemma and part of speech, refuses a bad level or an unknown rule, authored forms without their sidecar and a
  malformed sidecar (header, empty field, licence, verdict and result, repeated decision) with the path and line, and
  counts only a verdict `agree` with the result `none` as a veto; the attestation refuses a malformed token line of the
  open corpus with its file and line; an end-to-end run on a temporary corpus shows that an authored plural replaces
  the regular spelling, that authored forms join the written forms of the lexicon and the hand list instead of being
  dropped (`Run_AuthoredPlural_JoinsTheWrittenFormsOfTheLexicon`, `Run_AuthoredPlural_JoinsTheHandList`), a variant
  ships as `rule_variant`, an agreed NONE stops the regular plural and the reverse rule of the dictionary
  (`Run_AgreedNoPlural_StopsTheReverseRule`), an authored plural stops the reverse rule of the dictionary, and a token
  of the open corpus corroborates a comparative (`Run_OpenCorpusToken_CorroboratesTheComparative`). The committed table
  has `llm` rows and no `agid` row.
- **Rule defects** (`Data_RuleDefect_IsNotShipped`, `Data_RegularPlural_OfAWordThatOnlyEndsLikeACompound_IsShipped`):
  the spellings that the earlier rules got wrong (`bagmens`, `icewomans`, `obsequys`, `superheros`, `hertzes`,
  `birdiing`) are absent, and the regular plurals of
  `-man` words that are no compounds of man stay.
- **Rules** (`NounPlural_Regular_AddsSuffix`, `VerbThirdPerson_Regular_AddsSuffix`, `VerbRegular_Spellings`,
  `AdjectiveDegrees_ShortAndLong`, `ReverseCandidates_Plural_ContainsSingular`, `NounPlural_ManCompound_EndsInMen`,
  `NounPlural_WordEndingInMan_KeepsTheRegularPlural`, `NounPlural_Compound_TakesThePluralOfItsLastNoun`,
  `NounPlural_QuY_EndsInIes`, `VerbRegular_Doubling_FollowsTheLastSyllable`,
  `VerbRegular_ListedDoubledSpelling_BecomesPrimary`, `VerbRegular_Ie_KeepsTheEOrTurnsToY`).
