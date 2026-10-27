### OnnxClassifier Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxClassifier` is the internal helper of the encoder models: it resolves words from the lexicon, gives every analysis
of a word its probability, decides the fine sense, the group or the part of speech of ambiguous words from the
encoder's view of their context, and answers the words whose senses it does not score from the lexicon alone, so that
a composite model can hand them to its fallback model.

The classifier performs the whole decision of the encoder models: the lexicon resolution and the lexicon-only answer
(steps 1, 2 and 8 below) and, for the words whose lemma has trained weights, the encoder run and the scoring (steps 3
to 7), without changing the lifecycle code of `OnnxWordSenseModel`.

#### Data Model

An internal sealed class that holds no mutable state; it holds the weights table (`WeightedLemmaTable`) and the
encoder as a delegate (`PooledEncode`; the model supplies its shared `OnnxEncoder`, tests supply a fake, and a classifier
without an encoder answers every word from the lexicon). It uses no other model: it never uses the Basic
model. Per call it builds one result per word and work lists: words answered at once, words pending an encoder
decision and words answered from the lexicon alone.

A result is a core `ClassifyResult(Word, IsKnown, Analyses, Details, Level, Confidence, GroupId, SenseId)`; the
`Analyses` are `AnalysisCandidate(Details, Probability, Senses)` with `SenseCandidate(SenseId, Probability)` senses.
Per level:

<!-- markdownlint-disable MD013 -->

| Level | Details | GroupId | SenseId | Confidence |
| --- | --- | --- | --- | --- |
| `None` | null (not known, no analyses) | null | null | 0 |
| `PartOfSpeech` | the selected analysis | null | null | the probability of the slot of the selected analysis |
| `Group` | the selected analysis | set | null | the probability of the slot times the summed probability of the senses of the group within the slot |
| `Sense` | the selected analysis | set if in a group | set | the probability of the slot times the probability of the sense within the slot, or 1 |

<!-- markdownlint-enable MD013 -->

A **slot** is a lemma and a part of speech: the unit that owns the senses. The analyses of a form that share a slot and
differ only in features (`cut` as an infinitive and as a past tense, `deer` as a singular and a plural) are the
_variants_ of the slot. The probability of a slot is the sum of the probabilities of its variants, and the confidence
is built on it, so the split among variants never lowers it.

`Analyses` lists every analysis of the word, the probabilities summing to one, the slots sorted by their probability
(ties in the lexicon order of their first analysis) and inside a slot the variants sorted by their probability (ties in
lexicon order), and inside each analysis the `Senses` described in step 6 (empty when they were not scored, except the
one sense with probability 1 of the only analysis of a word whose lemma and part of speech have exactly one sense).
The first analysis is the selected one: the best variant of the most probable slot.

The probabilities are computed per slot, never per variant:

1. The probability of a sense given its slot comes from the softmax of the fine scores, renormalized in the slot.
2. The probability of a slot is the mass of its senses in that softmax; for a form with a chooser it is the share of the
   probability of the chooser's labels that the slot's analyses carry (a label shares its probability among its slots in
   proportion to the mass of their senses).
3. The variants of a slot split its probability: by the labels of the chooser when it has one per variant (`read` as a
   past and a present tense), otherwise equally, the lexicon order breaking ties. This prior is documented, not
   learned, and it reaches neither the level nor the confidence.
4. The selected analysis is the best variant of the most probable slot, and the confidence at the sense level is
   `P(slot) × P(sense | slot)`, whatever the number of variants.

#### Key Methods

`ClassifyAsync(words, cancellationToken)`:

1. **Resolve.** Every word is looked up (`WordLexicon.TryLookup`, which finds the lower-case spelling). No analysis gives
   `ClassifyResult.Unknown(word)`: not known, no analyses, no details, level `None`, confidence 0. The fine senses of
   the word are those (`SensesOf`) of the lemma and part of speech of every analysis of the word, in lexicon order; no
   sense of another lemma is ever one of them. The senses of a closed-class analysis (auxiliary, determiner, pronoun,
   preposition, conjunction, interjection; `SenseFusion.IsClosedClass`) are not among the senses that are scored: its
   analysis is listed with probability 0 and without senses, and `FallbackWordSenseModel` gives it the probability of
   the Basic model.
2. **Single analysis and single sense.** A word with one analysis whose lemma and part of speech have exactly one sense
   gives that analysis with probability 1 and that sense with probability 1 at the `Sense` level with confidence 1 and no
   encoder run, in every model.
3. **Weights.** For a word with several senses the table is asked for the weights of the lemma of its
   analyses and, for a form with several lemmas or tenses, for the form's chooser. A sense that has no
   column in the lemma's weights is dropped. When fewer than two senses with a column remain, or the lemma has no
   weights, or a multi-lemma form has no chooser, the word is answered from the lexicon alone (see _Lexicon-only
   answer_). Otherwise the word is pending.
   The rule that selects the scored columns of a lemma for a form (`OpenClassColumns`: the open-class senses of the parts
   of speech that the form has for the lemma) and the plan above (`CandidatesOf`: whether the encoder runs for the form,
   and the columns) have one implementation, which the build tool also calls (`FormColumns`, `CandidatesOf` and
   `ScoreForm`, which returns the scores, margins and result that the word gets before any threshold decides a level), so
   the thresholds, the calibration and the half-precision gate rank the senses that inference ranks.
4. **Encode.** When at least one word is pending, the words are joined by single spaces into one text and the encoder
   runs once for the pending words. Each pooled vector goes through `FeatureExtractor.Center` (subtract the global
   mean, scale to unit length). A word without word pieces, or a vector whose dimension differs from the weights, is
   answered from the lexicon alone.
5. **Form chooser.** For a pending form whose analyses belong to several lemmas (`left`, `bases`, `rows`, `found`) or
   to several tenses of one lemma (`read`, `reread`), the chooser of the form scores its labels with the feature. A
   label stands for the analyses of its lemma, or, written `lemma/features`, for the analysis of that lemma with those
   features (the table checks at load that every analysis of the form has a label and every label an analysis). The
   softmax of the scores gives each label its probability, which is shared among the slots of its analyses in proportion
   to the mass of their senses, and among the analyses of a slot equally; the margin `m` of the best label against the
   threshold `t` of the chooser is checked. If `m >= t`,
   the best analysis of the most probable slot is selected and the fine classifier of its lemma then runs for it. If the
   margin is
   below the threshold the answer is the selected analysis at the `PartOfSpeech` level with the probability of its slot
   as the
   (low) confidence. A selected lemma without weights is answered at the `PartOfSpeech` level in the same way: its
   slots count with the mass 1 when a label shares its probability, so the analyses of an untrained lemma of a form
   share their label's probability equally by slot, list no scored senses, and the confidence is the probability of the
   selected slot (the probability of its label divided among the slots). A form
   without a trained chooser (its chooser rows do not cover every analysis of the form, or it has none) is answered
   from the lexicon (step 8): the builder reports the forms that got no chooser and why.
6. **Levels, analyses and senses.** `LemmaWeights` scores the senses (one-versus-rest activation per fine sense) and
   derives the group scores for a lemma that has groups (the sum of the normalized fine probabilities per group, a
   sense that is in no group standing alone). The softmax of the fine scores of the senses of the word's analyses
   gives each sense a probability; the probability of a slot is the mass of its senses (or the chooser probability of a
   multi-lemma form, shared among its slots), the analyses of a slot (feature variants such as `cut` as an infinitive and
   as a past tense) split the probability of the slot equally unless the chooser tells them apart, and the
   probability of a sense given its slot is its mass renormalized within the slot, the same for every variant of the
   slot. Every analysis lists
   its senses ranked by that probability (equal probabilities keep inventory order, nothing is truncated), whatever the
   level answered, so a consumer that maps senses to its own categories can sum `analysis.Probability *
   sense.Probability` per category over all analyses (the probability of the slot times that of the sense) even when the
   answer is at the `PartOfSpeech` or the `Group` level. A sense without
   a column in the weights is never scored, so a slot whose senses all lack a column has probability 0. The level
   is then decided for the selected analysis (the best variant of the most probable slot). Groups exist only where
   curated, so the levels
   nest as part of speech, group (where one exists) and sense, and a model that is unsure between senses steps up to
   the next level that exists and meets its threshold:
   - the best fine sense is answered at the `Sense` level when its margin is at least `t_fine` of the lemma and it
     lies in the selected slot, with its group id when the sense is in a group;
   - otherwise, for a lemma that has groups and a best sense that is in a group (and lies in the selected slot),
     the group is answered at the `Group` level when the group margin (the probability of the group minus the largest
     probability of any other group or single ungrouped sense) is at least `t_group`;
   - otherwise (the lemma has no groups, the best sense is in no group, or the group margin is below `t_group`) the
     selected analysis is answered at the `PartOfSpeech` level with the probability of its slot as the confidence. The
     ranked
     senses of the analysis still show the likely senses and groups.

   A threshold that is "never" is not reached by any margin. A single remaining sense has an infinite margin.
   The confidence is the probability of the answered level (the table in _Data Model_), so it is between 0 and 1,
   grows with the margin and is comparable only within one result.
7. **Result.** The result carries the analyses, the selected analysis as `Details`, the level, the confidence and the
   ids. A non-empty `Senses` list in the selected analysis, or the level `Group` or `Sense`, is what a
   `FallbackWordSenseModel` reads as "this model scored senses for the word": it then uses this result (and gives the
   variants of a slot that this classifier split equally the proportions of its fallback; this classifier never uses
   Basic itself, and the probability of every slot stays as computed here), and otherwise
   its fallback.
8. **Lexicon-only answer.** For a known word whose senses are not scored (a word without usable weights, a word with a
   single analysis and many senses and no weights, a form of several lemmas without a chooser, a word whose encoder
   vector is unusable) the answer is built from the lexicon: every lemma and part of speech (slot) has probability 1/s
   for s slots (1 for a single slot), shared equally by its variants, the slots are listed in lexicon order of
   their first analysis, the first analysis is the selected one, the senses lists are empty (a word with a
   single analysis whose lemma and part of speech have exactly one sense lists that sense with probability 1), and the
   level is `Sense` for that single-analysis, single-sense word and otherwise `PartOfSpeech`, with the probability of the
   slot of the selected analysis as confidence. No fine sense is ever guessed. The classifier does not choose among the
   analyses
   itself: the model it is composed with does.

`NeedsModel` and `ResolveWithoutModel` let the model answer text without ensuring or opening anything: `NeedsModel`
takes the weights table (the embedded weights, which need no model file) and is true only when a word is pending, that
is, has several senses together with usable weights (or a chooser). Words that are unknown, have one analysis and one
sense, or get the lexicon-only answer are answered without the encoder and without model files.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`; an empty list gives an empty array and the result is parallel to the input. A vector
that cannot be used (no word pieces, or a dimension that differs from the weights) does not fail the call: the word
gets the lexicon-only answer. The dimension case is a defensive guard of the classifier on its own; through
`OnnxWordSenseModel` it cannot occur, because the model rejects an encoder whose hidden size differs from the one of its
description and weights when it opens the encoder. Encoder failures propagate.

#### Dependencies

`WordLexicon`, the core contracts, `OnnxEncoder` (through the `PooledEncode` delegate) and `LemmaWeights` (weights
table, feature computation, scoring, levels). No other model: the classifier does not use the Basic model.

#### Callers

`OnnxWordSenseModel.ClassifyAsync` and the builder (`Evaluator`).
