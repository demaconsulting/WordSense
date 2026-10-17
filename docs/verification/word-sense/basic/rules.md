### Rules Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/RulesTests.cs` call the internal `Rules.Evaluate` with a normalized
previous word, next word and a set of candidate parts of speech. The neighbor-lookup rule `before-noun` reads the real
embedded lexicon; everything else is closed-class word lists inside the unit. Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Rule order**: `Rules.RuleNames` lists exactly eight rules in the order `after-modal`, `after-to`,
  `after-subject-pronoun`, `after-determiner`, `after-degree-adverb`, `after-preposition`, `before-auxiliary` and
  `before-noun`, and every rule has a confidence between 0 and 1.
- **after-modal**: a modal or auxiliary such as `will` or `didn't` before the word selects the verb.
- **after-to**: `to` before the word selects the verb.
- **after-subject-pronoun**: a subject pronoun such as `they` before the word selects the verb.
- **after-determiner**: a determiner before the word selects the noun, or the adjective when the word cannot be a noun.
- **after-degree-adverb**: a degree adverb such as `very` before the word selects the adjective.
- **after-preposition**: a preposition before the word selects the noun.
- **before-auxiliary**: an auxiliary such as `is` after the word selects the noun.
- **before-noun**: a word after the target that the lexicon knows only as a noun selects the adjective.
- **Rule only fires for an available part of speech**: when the part of speech a rule would select is not among the
  candidates, the rule does not fire and the next rule is tried.
- **First rule wins**: when two rules could fire (for example a modal before the word and an auxiliary after it), the
  earlier rule in the order decides.
- **No rule fires**: with no previous and next word, or with neighbors that trigger no rule, the result is null.
- **Confidences are fixed**: the confidence of a match equals the documented constant of its rule.
