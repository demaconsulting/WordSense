### BasicClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs` call the classifier with word lists and the
embedded lexicon. The rules are real; nothing is mocked, because the expected results follow from the lexicon and the
documented rules. The rule order and each rule are verified in the `Rules` unit document.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word**: a word with no lexicon entry, and a token with no letter such as `.`, gives `(word, null, 0)`.
- **Single-sense word**: a word with one entry gives `(word, entry, 1.0)` and the word is returned as written.
- **Ambiguous word with a firing rule**: a word with several senses after a modal verb gives the verb sense with the
  confidence of the rule that fired.
- **Abstains when no rule fires**: a word with several senses at the start of the text, or between words that trigger
  no rule, gives `(word, null, 0)`.
- **Abstains on same-part-of-speech senses**: when the selected part of speech matches more than one sense (the senses
  of a heteronym that share a part of speech), the result is `(word, null, 0)`, because a rule cannot choose among them.
- **Case and apostrophe normalization**: upper-case words and the curly apostrophe resolve like their lower-case
  straight forms.
- **Punctuation neighbors**: a neighboring token without a letter counts as no neighbor, so no rule uses it.
- **Argument validation**: a null word list and a null word in the list throw.
- **Cancelled token**: a cancelled token throws `OperationCanceledException`.
