# Basic Model Evaluation

This note records how the statistical model `DemaConsulting.WordSense.Basic` is trained and measured. The figures are
the output of the builder commands `train-basic` and `evaluate basic`; how to retrain and re-measure it is in
`tuning.md`.

## Method

The model is small enough to ship inside the package (the table `basic.bin`, see the design of `BasicTables`) and has
two statistical parts that are fused for every ambiguous word:

1. **Part-of-speech tagger.** A second-order hidden Markov model over thirteen tags (noun, pronoun, verb, adjective,
   adverb, preposition, conjunction, interjection, determiner, auxiliary, infinitive "to", punctuation, other). The
   transition counts and the counts of the closed-class words and of the word suffixes come from the Universal
   Dependencies English-EWT treebank (training part). The dictionary of the core package limits the tags an open-class
   word may take. The forward-backward algorithm gives a posterior over the tags for every word of a sentence.
2. **Context cues.** For each lemma with at least two senses, a naive Bayes model over the words in a window of five on
   each side and the words directly before and after the target. It is trained on the training anchors only, and it
   separates senses that share a part of speech (for example `bass`, `tear` and `row`).

The fusion adds the weighted logarithm of the tag posterior and the scaled cue score of each candidate sense and turns
the scores into probabilities with a softmax. The sense with the highest probability is the answer and that
probability is the confidence; the model answers every ambiguous word and never abstains.

## Training and hyperparameters

`train-basic` retrains everything from the committed counts in `corpus/ud`, the word table of the core package, and
the anchors and sense inventory in `corpus`. Every tunable value is in one place, `BasicHyperparameters.cs` of the
builder; the values the runtime needs travel in the header of the table. The values of the shipped table are the
defaults of that file.

| Parameter | Value | Meaning |
| --------- | ----: | ------- |
| gamma | 0.75 | Exponent of the emission ratio of a tabled word |
| beta | 5 | Pseudo count of the tag prior in the emission of a tabled word |
| lambda 1, 2, 3 | 0.1, 0.3, 0.6 | Unigram, bigram and trigram weights of the transition |
| HMM weight | 1.5 | Weight of the log tag posterior in the fusion |
| temperature | 2 | Divisor of the cue scores in the fusion |
| closed-class minimum count / share / add share | 5 / 0.9 / 0.2 | When a treebank word counts as closed-class |
| tabled ambiguous words | 2,000 | The most frequent dictionary-ambiguous words whose counts are tabled |
| suffix minimum total | 5 | Fewest treebank words a suffix needs to be tabled |
| cue alpha | 0.1 | Additive smoothing of the cue likelihoods |
| cue minimum feature count | 2 | Fewest anchors a context feature needs to enter the vocabulary of a lemma |

The hyperparameters were chosen on data that is not used for the reported results: the treebank development part, and
a fixed-seed sample of 5,000 training anchors split into a training and a development subset (one sentence in five,
by a hash of its text, is development). The holdout and the real-text sentences are never used for tuning.

## Results

`evaluate basic` prints the same comparison table on every run, so that two runs can be compared line by line. The
reference rows are fixed values of earlier measurements; the first row is the current table. The table
`basic.bin` is 85,237 bytes (83 KiB), SHA-256 `EE8FCBEA97188A0D6C687F601C1BF237E28DC2A2945B45A4C9CD2B0B6B0435FC`.

<!-- markdownlint-disable MD013 -->

| Model | Best sense, holdout | Best sense, real text | Part of speech (holdout / real) | Pronunciation (holdout / real) |
| ----- | ------------------: | --------------------: | ------------------------------: | -----------------------------: |
| Basic (this build) | 91.16% | 85.20% | 92.25% / 90.13% | 90.18% / 87.41% |
| Basic spike (research prototype) | 91.4% | 81.9% | 92.6% / 87.5% | 93.9% / 84.9% |
| MiniLM | 96.91% | 92.60% | 97.25% / 95.36% | 96.46% / 94.45% |
| BertBaseCased | 99.05% | 97.39% | 99.23% / 98.40% | 99.25% / 98.20% |

<!-- markdownlint-enable MD013 -->
The holdout is optimistic (written together with the training anchors); the real-text column is the realistic
expectation.

### Confidence

| Set | Threshold | Coverage | Precision |
| --- | --------: | -------: | --------: |
| holdout | 0.6 | 95.14% | 91.19% |
| holdout | 0.8 | 85.98% | 94.53% |
| holdout | 0.9 | 78.07% | 96.83% |
| real text | 0.6 | 88.39% | 91.30% |
| real text | 0.8 | 77.50% | 94.19% |
| real text | 0.9 | 69.23% | 97.27% |

Calibration is monotone: holdout accuracy per bucket is 47.3% (0.4-0.6), 57.7% (0.6-0.7), 61.8% (0.7-0.8), 71.9%
(0.8-0.9) and 96.8% (at least 0.9). The probabilities are under-confident in the middle buckets, over-confident
below 0.6, and well calibrated at 0.9 and above.

### Cost

| Measure | Value |
| ------- | ----: |
| Table size | 83 KiB |
| Table cold load | 22 ms |
| Classification time | 2.3 us per word, 34 us per sentence |
| Word table first use (core package) | 170 ms, 59.7 MiB |
| Process memory added by the model | 21 MiB |

### Differences to the spike

The holdout is 2.8 points below the spike (88.65% against 91.4%) and the part-of-speech holdout 2.9 points below;
the real-text best sense is 2.9 points above it (84.76% against 81.9%). The shipped model is trained only on the
committed treebank counts and the training anchors, with hyperparameters tuned on the development subsets. The cause
of the holdout gap was not established (it is a hypothesis, not a finding, that the spike was fitted closer to the
holdout's own style). The gap was not closed by tuning: raising gamma to 1.0 and the HMM weight to 2.0 improved the
development subsets by under a point each but lowered the holdout (88.46%) and real text (84.33%), so the defaults
were kept. Table quantization and clitic tokenization were considered as causes but not isolated with experiments. The
holdout was deliberately not used to chase the gap, to keep it unused for tuning.

### A bug found during evaluation

The first evaluation gave only 66.4% (holdout). The cue model accumulated the sense priors, which are stored as signed
bytes, in a byte-typed sum and overflowed; the sum is now an `int`. The table was not affected, only the scoring; the
evaluation above is after the fix.

## Reading the confidence

The confidence is the probability that the fusion assigns to its answer. It is a ranking signal and is roughly
calibrated on the anchors, not a guarantee: the calibration buckets in the output show how often an answer of a given
confidence is right on the holdout. A caller that wants fewer wrong answers can filter at a threshold of its choice,
and treat the rest as unknown; the threshold rows show the cost in answered words.

## Limits

- The anchors and the holdout were written together, so their accuracy is higher than that on real text; the real-text
  column is the honest number.
- Basic chooses among the senses of the lexicon. A word that is not in the lexicon has no answer, and a tokenizer that
  splits a clitic such as "don't" differently from the lexicon loses the word.
- A heteronym with two meanings of the same part of speech is decided only for lemmas that have a cue table, that is,
  lemmas with enough anchors.
