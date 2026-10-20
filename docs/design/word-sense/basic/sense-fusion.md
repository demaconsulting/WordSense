### SenseFusion Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`SenseFusion` combines the part-of-speech posterior of the tagger with the cue scores into one probability per
candidate analysis of a form. The classifier lists every analysis with its probability, so the probabilities are the
analyses of the result, and the probability of the largest is the confidence of the result.

#### Data Model

No state. Inputs per word: the 13 tag posteriors, the candidate analyses (`WordDetails`: lemma, part of speech and
features, in lexicon order), one cue score per candidate (`NaN` when the cue table has none), the tagger weight
(`HmmWeight`, from the table header) and the inverse temperature of the cues. The candidates are analyses, never
senses; several candidates may share a part of speech.

#### Key Methods

`Fuse(posteriors, analyses, cues, tagWeight, invTemperature, probabilities)`:

1. take the posterior of the part of speech of each candidate and renormalize over the candidates (equal shares when the
   tagger gave no mass to any candidate); candidates that share a part of speech get the same share, so only the cues
   separate them;
2. replace a missing cue by the mean of the available cue scores (zero when none is available);
3. compute the score `tagWeight * ln(max(p, 1e-6)) + cue * invTemperature` per candidate;
4. take the softmax with the maximum subtracted first, so extreme scores stay finite, and the probabilities sum to 1.

`Winner(probabilities)` returns the index of the largest probability and the lowest index on a tie; the classifier
applies the same rule when it sorts the analyses (largest first, a tie keeps the lexicon order). The probabilities sum
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
