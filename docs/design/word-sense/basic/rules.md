### Rules Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`Rules` holds the ordered, hand-written rules that select a part of speech for an ambiguous word from its previous
and next word, together with the word lists the rules use and the confidence of each rule.

#### Data Model

Each rule has a name, a condition on the neighbors, the part of speech it selects and a confidence. The rules are
evaluated in this order and the first rule that fires wins; a rule fires only when the part of speech it selects is
among the candidates of the word.

1. `after-modal`: the previous word is a modal verb or a form of "do"; selects Verb; confidence 0.99.
2. `after-to`: the previous word is "to"; selects Verb; 0.98.
3. `after-subject-pronoun`: the previous word is a subject pronoun; selects Verb; 0.99.
4. `after-determiner`: the previous word is a determiner; selects Noun, else Adjective; 0.95.
5. `after-degree-adverb`: the previous word is a degree adverb (very, too, so, ...); selects Adjective; 0.89.
6. `after-preposition`: the previous word is a preposition; selects Noun; 0.82.
7. `before-auxiliary`: the next word is an auxiliary verb; selects Noun; 0.97.
8. `before-noun`: the next word is a word that is only a noun in the lexicon; selects Adjective; 0.77.
The confidences are fixed constants (the `*Confidence` constants of `Rules`), each calibrated from the per-rule
precision that the builder command `basic-precision` measures on the training sentences (never the holdout); the source
file is authoritative for the current values. The word lists (modals, pronouns, determiners, degree adverbs,
prepositions, auxiliaries) are fixed
sets of lower-case words.

#### Key Methods

`Evaluate(previous, next, candidates)` returns the first firing rule's part of speech and confidence, or no result.
The `before-noun` rule looks the next word up in `WordLexicon`.

#### Error Handling

None; absent neighbors (null) simply make the rules that need them not fire.

#### Dependencies

`WordLexicon` and `PartOfSpeech`.

#### Callers

`BasicClassifier` and the builder (`BasicPrecision` measures each rule).
