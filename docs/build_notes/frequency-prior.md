# Frequency prior

The classifiers of the encoder models are trained on sentences that are balanced by sense quota (about 20 anchors for a
frequent sense and 5 to 10 for a rare one, and every sense of a lemma has some), so the probabilities they give carry the
proportions of the training data, not the proportions of real use: the rare senses are over-predicted. This note records
the correction that the builder now applies when it trains the weights, how its settings were chosen, and what it did to
every measurement.

## The correction

For a lemma with the senses `s` that have anchors, the builder turns the one-versus-rest probabilities `p(s|x)` into

`p'(s|x) ~ p(s|x) / q(s) * pi(s)^alpha`, normalized over the senses of the lemma,

where

- `q(s)` is the share of the sense among the training anchors of its lemma, so dividing by it removes the proportions of
  the training data (the label shift of a balanced training set);
- `pi(s)` is the usage share of the sense: its WordNet 3.0 tag count (`data/sense_frequency`) plus the smoothing constant
  `k = 0.5`, divided by the sum of that over the senses of the lemma; a sense without a count (a project sense has no
  sense key, and a key the table does not list counts 0) gets the constant alone;
- `alpha = 0.5` tempers the counts, because they are small (most senses have fewer than ten tags) and come from the
  Brown corpus, a different genre.

The scores of a lemma are log-odds whose softmax is the probability, so the correction is an additive offset
`-ln q(s) + alpha ln pi(s)` on each score. The trainer adds it to the stored bias of the sense. Nothing else changes: the
weights have the same size (4,158 KiB and 8,147 KiB), the runtime has no new code and no new cost, and the margins, the
probabilities and the thresholds are all read from the corrected scores. The first thresholds are chosen by the
cross-validation of each lemma on the corrected out-of-fold scores, and the real-text calibration (target 0.97, as
before) then scores with the stored biases, so the thresholds belong to the corrected model.

The settings are builder options (`weights ... --prior-alpha 0.5 --prior-smoothing 0.5`; `--no-prior` trains the raw
scores) and are printed at the start of every run. The weights blob (`WSX3`) has no slot for metadata, so the settings are
not stored in it: they are the defaults of the builder and are documented here and in the design of the builder
(`docs/design/word-sense/word-sense-builder.md`, "Usage prior"). The offsets are part of the key of the fit cache, so a
cached fit is never reused for another prior.

## How the settings were chosen

The correction was tried first as an experiment on copies of the weights (the same code path as the shipped models, the
cached pooled vectors, and the production thresholds: cross-validation on the corrected scores, then the real-text
calibration at 0.97). The experiment reproduced the committed weights byte for byte without the correction. The
variants were:

- `flat`: only `q` divided out (`alpha = 0`);
- `P1`: the WordNet 3.0 counts, with `k` 0.5 or 1 and `alpha` 0.5 or 1;
- `P2`: an independent prior, the share of the high-confidence labels of the randomly harvested candidate sentences of the
  quota waves (33,400 labels, 1,761 senses, about 410 lemmas), with `k = 1`.

<!-- markdownlint-disable MD013 -->

| Variant | MiniLM real test: sense share @ precision, fine top-1 | MiniLM SemCor fine top-1 | BERT real test: sense share @ precision, fine top-1 | BERT SemCor fine top-1 |
| --- | ---: | ---: | ---: | ---: |
| none (before) | 3.14% @ 86.36%, 56.37% | 47.46% | 15.18% @ 93.73%, 66.94% | 56.92% |
| `flat` (`alpha` 0) | 2.90% @ 86.89%, 55.38% | 44.60% | 17.08% @ 94.43%, 67.08% | 55.20% |
| **P1, k 0.5, alpha 0.5 (shipped)** | **3.09% @ 87.69%, 56.42%** | **53.76%** | **15.70% @ 94.24%, 65.79%** | **60.91%** |
| P1, k 0.5, alpha 1 | 2.66% @ 87.50%, 54.47% | 58.27% | 12.70% @ 94.01%, 63.51% | 63.72% |
| P1, k 1, alpha 0.5 | 3.00% @ 87.30%, 56.80% | 53.10% | 13.70% @ 94.79%, 65.84% | 60.48% |
| P1, k 1, alpha 1 | 2.81% @ 88.14%, 55.09% | 57.89% | 12.08% @ 94.49%, 64.13% | 63.33% |
| P2, k 1, alpha 0.5 | 2.90% @ 85.25%, 55.38% | 49.99% | 15.70% @ 93.64%, 65.94% | 58.34% |
| P2, k 1, alpha 1 | 2.71% @ 84.21%, 54.00% | 52.47% | 14.37% @ 93.05%, 63.80% | 59.46% |

<!-- markdownlint-enable MD013 -->

Dividing out `q` alone does not help on running text (SemCor falls); the usage term is what helps. A larger exponent raises
the fine top-1 on SemCor further, but it costs the part of speech and the share of sense-level answers on the real test
sentences (for BERT the sense share falls from 15.2% to 12.7% and the real-text part of speech from 96.2% to 94.7%), so
`alpha = 0.5` with `k = 0.5` was chosen as the setting that gains on SemCor and costs least on the balanced real test.
The harvest prior (P2) is independent of WordNet but covers only about 410 lemmas (the others get a flat prior) and gains
less on SemCor, so the WordNet counts are the prior of the builder.

## Effect on the shipped models

The retrained models were evaluated through the public model path with every set of the builder; the figures equal those
of the experiment variant. The suite and the STE challenge set are shown too.

<!-- markdownlint-disable MD013 -->

| Measure | MiniLM before | MiniLM after | BERT before | BERT after |
| --- | ---: | ---: | ---: | ---: |
| Real test: sense share @ precision | 3.14% @ 86.36% | 3.09% @ 87.69% | 15.18% @ 93.73% | 15.70% @ 94.24% |
| Real test: fine top-1 (balanced set) | 56.37% | 56.42% | 66.94% | 65.79% |
| Real test: part of speech | 93.53% | 93.58% | 96.24% | 96.05% |
| Open corpus test: part of speech (all / ambiguous) | 93.72% / 91.28% | 93.73% / 91.30% | 93.98% / 91.70% | 94.03% / 91.79% |
| UD-EWT test (mapped): part of speech (all / ambiguous) | 91.60% / 88.54% | 91.68% / 88.66% | 91.85% / 88.93% | 91.95% / 89.09% |
| STE challenge set: part of speech | 98.20% | 98.20% | 98.20% | 98.20% |
| SemCor: sense share @ precision | 1.65% @ 76.69% | 2.42% @ 85.55% | 12.55% @ 83.02% | 17.01% @ 86.67% |
| SemCor: fine top-1 | 47.46% | 53.76% | 56.92% | 60.91% |
| Suite (348 rows): answered / fine | 93.68% / 91.67% | 93.39% / 91.09% | 95.69% / 94.25% | 95.69% / 94.54% |
| Usage-weighted real test: fine top-1 (diagnostic) | 57.34% | 59.92% | 68.81% | 70.62% |
| Usage-weighted real test: sense share @ precision | 2.69% @ 83.29% | 2.93% @ 85.17% | 15.07% @ 93.49% | 16.89% @ 94.61% |

<!-- markdownlint-enable MD013 -->

Reading the table:

- **SemCor** is the independent measure of usage: human-annotated running text that no step reads. Both models improve
  clearly in the fine top-1 (+6.3 points MiniLM, +4.0 points BERT), and the share of sense-level answers and their
  precision rise too, at the same calibration target. SemCor is **circular for this prior** to some degree: the
  WordNet 3.0
  counts include the counts of SemCor itself, so the gain on SemCor is partly the prior having seen the answers. That
  is why the correction is tempered (`alpha = 0.5`) and why the second line of evidence below matters.
- **The usage-weighted real test** re-weights the 2,102 real test sentences by an independent sense prior, the share of
  each sense among the randomly harvested candidate sentences (`tools/quota-fill/usage_weighted.py`; the harvest labels
  are not WordNet counts and not SemCor). Every lemma keeps its total weight and only the mix of its senses changes. The
  fine top-1 rises by 2.6 (MiniLM) and 1.8 points (BERT), so the gain is not an artifact of the WordNet counts.
- **The real test as it is** is balanced by sense quota like the training data, so it rewards the old, uncorrected
  proportions: the fine top-1 moves by +0.05 (MiniLM) and -1.15 points (BERT), and the part of speech by +0.05 and -0.19
  points, which is the price of the correction on a set that is not distributed by usage.
- **Running-text part of speech** (open corpus, UD) does not fall; it rises by 0.01 to 0.16 points.
- **The suite**: the BERT model gains one row (fine). The MiniLM model loses three rows and gains two (net two rows
  fine, one
  answered); the rows, each checked against the ranked senses, are listed in `corpus/suite/README.md`. They are
  near-even part-of-speech choices of the encoder for lemmas whose noun senses have the larger counts (`second`,
  `detail`, `conflict`), plus `bass`, where the sense `bass#pitch` now ranks before the fish senses that split their mass.
  Each is an effect of the correction, not a defect, and the expectations were rewritten under the policy of the suite.
- **Cost**: none at run time; the thresholds were re-derived, so the share answered at the sense level moves by a few
  tenths of a point.

## Caveats and what the prior does not do

- A sense with very few anchors is lifted the most by dividing out `q` (`conflict#incompatibility` has one anchor and gains
  +2.4 in score). The quota deficits of the corpus (2,871 of the 6,969 senses of the trained lemmas are short and 1,386
  have no anchor at present) make `q` partly a measure of data scarcity and not of balance; filling the quotas
  (README, "Planned Improvements") makes the correction more faithful.
- The prior is per lemma and sense, not per context: it does not know the genre of the text. The WordNet counts come from
  the Brown corpus.
- Senses without an anchor have no column in the classifier and can never be answered, with or without the prior.

## Reproduction

`weights minilm` and `weights bert-base-cased` retrain the weights with the prior (the pooled vectors are reused from the
embedding cache, so a run takes about two minutes), and `weights ... --no-prior` reproduces the earlier weights. The
usage-weighted diagnostic needs the harvest cache of `tools/real-text/cache/quota/work`:

```text
WordSense.Builder evaluate minilm --outcomes minilm.tsv
python tools/quota-fill/usage_weighted.py minilm=minilm.tsv
```
