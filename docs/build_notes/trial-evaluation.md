# Trial evaluation: fine senses of the first 20 words (increment I4)

This note records the first measurement of the fine-sense models. The encoder models (MiniLM and BERT base cased)
were trained for the 20 words of the first reviewed batch (`back`, `even`, `first`, `forward`, `go`, `help`, `home`,
`however`, `just`, `little`, `long`, `only`, `own`, `please`, `pretty`, `review`, `right`, `still`, `test` and `well`;
294 senses, 3,488 training sentences) and measured on the 984 held-out sentences of the same words. The numbers come
from `WordSense.Builder evaluate <model>` (the per-level tables) and are not published as a release figure: the held-out
sentences were written by the same generator as the training sentences, so they are **optimistic**, and the independent
evaluation on SemCor 3.0 (senses) and the English EWT treebank (parts of speech) is a later increment.

## How the models answer

- A word is answered at the finest level that passes the thresholds of its lemma: the sense (the best fine sense wins by
  at least the fine threshold), else the group of the best sense (the group wins by at least the group threshold), else
  the part of speech. The thresholds of every lemma were chosen by 5-fold cross-validation on the training sentences of
  the lemma only, for a precision target of 95 percent; a level that never reached the target is never answered for the
  lemma. MiniLM has a fine threshold for 20 of the 20 lemmas and a group threshold for 11 of the 16 lemmas that have
  groups; BERT base cased for 20 of 20 and 9 of 16.
- A form that belongs to several lemmas (for example `gone` of `go` and of the adjective `gone`, `backed` of `back` and
  of the adjective `backed`, `pleased`, `going`, `rights`) needs a form chooser, which needs training sentences for the
  other lemma as well. The corpus has none yet, so the 56 held-out sentences (5.7 percent) whose target is such a form
  are answered by the `Basic` fallback at the part-of-speech level, without scored senses.

## Held-out sentences (984 rows)

Share of the sentences answered at each level, and the precision at each level (the sense answer is right when it is the
sense of the row, the group answer when it is the group of the sense of the row, the part-of-speech answer when it has
the part of speech of the row):

| Model | Sense | Sense precision | Group | Group precision | Part of speech | Part-of-speech precision |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basic | 4.07% | 82.50% | 0.00% | n/a | 95.93% | 77.44% |
| MiniLM | 50.20% | 96.15% | 8.13% | 97.50% | 41.67% | 88.54% |
| BERT base cased | 81.50% | 96.63% | 5.39% | 98.11% | 13.11% | 87.60% |

| Model | Correct at answered level | Fine top-1 (all rows) | Fine top-1 (ranked rows) | Part of speech |
| --- | ---: | ---: | ---: | ---: |
| Basic | 77.64% | 6.00% | 13.44% | 77.64% |
| MiniLM | 93.09% | 76.73% | 81.36% | 94.72% |
| BERT base cased | 95.53% | 86.79% | 92.03% | 97.87% |

Fine top-1 is the share of the rows whose most probable sense is the sense of the row (the probability of a sense is
the sum over the analyses of the probability of the analysis times the probability of the sense in it); the rows
without ranked senses are the 56 rows of the shared forms above.

## Comparison with the earlier experiment

The experiment of the planning phase (its `results.md`, the same 3,488 and 984 sentences, a reference
implementation of the same logistic regression with the same C and the same features, all senses of the lemma as
candidates, thresholds by the same
cross-validation) is the reference:

| Measure | Experiment | This increment |
| --- | ---: | ---: |
| BERT fine top-1 (the lemma's own classifier, all rows) | 93.9% | 93.70% (builder gate, 984 rows) |
| MiniLM fine top-1 (the lemma's own classifier, all rows) | 83.6% | 82.93% (builder gate, 984 rows) |
| BERT answered at the sense level | 86.8% at 96.5% precision | 81.5% at 96.63% |
| BERT answered at the group level | 8.8% at 97.7% | 5.4% at 98.11% |
| MiniLM answered at the sense level | 53.3% at 95.8% | 50.2% at 96.15% |
| MiniLM answered at the group level | 10.6% at 93.3% | 8.1% at 97.50% |

The precisions agree with the experiment and meet the 95 percent target. The shares are lower for two reasons that
the experiment did not have: the 5.7 percent of rows with a shared form go to the `Basic` fallback (81.5 / 0.943 =
86.4 percent of the other rows are answered at the sense level by BERT, close to the 86.8 percent of the experiment),
and the experiment answered a *low-confidence group* when neither threshold was met (4.4 percent for BERT and 36.2
percent for MiniLM, with 76.7 and 66.3 percent accuracy), whereas this version answers the part of speech there and
keeps the ranked senses (and with them the best sense and its group) in the result.

## Half-precision weights

The weights are stored as 16-bit floats. The builder checks the held-out rows with the full-precision weights and with
the weights read back from the written blob: BERT 93.70 and 93.70 percent, MiniLM 82.93 and 82.93 percent fine top-1
(difference 0.00 points, gate 0.2).

| Model | Blob before (class weights) | Blob now (half precision, fine senses of 20 lemmas) |
| --- | ---: | ---: |
| MiniLM | 1,159,517 bytes | 208,298 bytes |
| BERT base cased | 2,298,596 bytes | 404,951 bytes |

The earlier blobs scored the 1,521 pronunciation and part-of-speech classes of 744 lemmas; the new blobs hold the fine
senses of 20 lemmas (294 senses), so the sizes are not comparable per lemma. Both blobs train in the repository with
ONNX Runtime 1.31 (the earlier blobs were trained with 1.28), with the global mean recomputed once from the new anchors
and frozen from now on.

## Suite rows of the 20 words (44 rows, part of speech)

The reviewed suite (348 rows) contains 44 sentences of the 20 words (`back`, `first`, `help`, `home`, `little`, `long`,
`only`, `own`, `review`, `right`); its expectations are parts of speech. Correct selected part of speech:

| Model | Correct | Levels |
| --- | ---: | --- |
| Basic | 41 of 44 | 39 part of speech, 5 sense |
| MiniLM | 42 of 44 | 30 sense, 14 part of speech |
| BERT base cased | 42 of 44 | 38 sense, 2 group, 4 part of speech |

For comparison, the models of the earlier version (before these words were trained) were right on 41 (Basic) and 17
(MiniLM and BERT) of the same rows. The misses are `He patted the horse on its {back}` (BERT answers `back#animal-rear`,
a sense that the catalog files under the adjectives, so its part of speech is wrong for the suite), `The old theory is
{little} known` (MiniLM and BERT answer the sense `little#not-much-quantity`, an adjective sense, for an adverb use),
`The committee will {review}` (MiniLM, part of speech level, noun) and three `Basic` misses.

## Not measured yet

- Independent running text (SemCor senses, the English EWT part of speech): increment I5.
- The share of the confident Group answers versus the answers below the group threshold cannot be told apart in a
  result any more: a group below its threshold is answered at the part-of-speech level.
- Words that are not among the 20 (every other word): the models answer them like `Basic`.
