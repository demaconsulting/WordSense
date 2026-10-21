### SenseFusion Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`SenseFusion` combines the part-of-speech posterior of the tagger with the cue scores into one probability per
candidate analysis of a form. The classifier lists every analysis with its probability, so the probabilities are the
analyses of the result, and the probability of the lemma and part of speech (slot) of the best candidate is the
confidence of the result.

#### Data Model

No state. Inputs per word: the 13 tag posteriors, the candidate analyses (`WordDetails`: lemma, part of speech and
features, in lexicon order), one cue score per candidate (`NaN` when the cue table has none), the tagger weight
(`HmmWeight`, from the table header) and the inverse temperature of the cues. The candidates are analyses, never
senses; several candidates may share a lemma and a part of speech (a slot, the unit that owns the senses).

#### Key Methods

`Fuse(posteriors, analyses, cues, tagWeight, invTemperature, probabilities)`:

1. take the posterior of the part of speech of each candidate and renormalize over the candidates (equal shares when the
   tagger gave no mass to any candidate); candidates that share a part of speech get the same share, so only the cues
   separate them;
2. replace a missing cue by the mean of the available cue scores (zero when none is available);
3. compute the score `tagWeight * ln(max(p, 1e-6)) + cue * invTemperature` per candidate;
4. take the softmax with the maximum subtracted first, so extreme scores stay finite, with one correction: the
   candidates of one slot that have no cue score (the cue table has no column for them, or there is no table) are not
   separated by the context, so together they weigh as one candidate, each of them one over their number of the weight
   of one. The probability of a slot is therefore the tagger and cue evidence for its lemma and part of speech, whatever
   the number of its feature variants: a noun with one analysis and a verb with three variants are equally likely when
   the evidence is, not one in four and three in four. Candidates that have cue scores keep their own weights (the
   naive Bayes classes of the cue table already split the prior of a part of speech among their variants). The
   probabilities sum to 1.

`Winner(probabilities)` returns the index of the largest probability and the lowest index on a tie; the classifier
orders the slots by the sum of the probabilities of their analyses and the analyses of a slot by their own probability
with the same tie rule (a tie keeps the lexicon order). The probabilities sum
to 1, and the largest is at least one over the number of candidates and strictly monotone in the score margin. The
confidence that the classifier reports is therefore greater than 0 and at most 1.

#### Error Handling

A null analysis array throws `ArgumentNullException`. No numeric input produces a non-finite probability.

#### Determinism and Thread Safety

The functions are pure; equal input gives equal output, and any number of threads may call them. A small stack buffer is
the only scratch space.

#### Dependencies

`PosTag` and the core `WordDetails`.

#### Callers

`BasicClassifier`.
