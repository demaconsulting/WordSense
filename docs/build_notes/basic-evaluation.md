# Basic Model Evaluation

This note records how the rule model `DemaConsulting.WordSense.Basic` is measured and how the confidence of each of its
rules is calibrated. The figures are the output of the builder
commands `basic-precision` and `evaluate basic`.

## Method

- `basic-precision` runs the rules on every training anchor whose target word has at least two senses in the word
  table. For each rule it counts how often the rule fired first (**fired**) and how often the part of speech it
  selected was the gold part of speech of the anchor (**correct**); the precision is correct divided by fired. It
  prints the rule, the counts, the precision and the confidence the rule has now. The holdout is never read.
- The confidence of a rule is a fixed value in `Rules.cs`. The initial values were estimates; the calibrated values are
  the measured precision on the training anchors, so a result with a confidence of 0.95 is right about 95 percent of
  the time on the anchors.
- `evaluate basic` measures the model like the encoder models (see `model-comparison.md`): best sense, part of
  speech, pronunciation, answered and answered accuracy on the holdout and on the real-text sentences, plus latency
  and the first use of the word table. A word without a decision is counted as not answered.

```text
dotnet run --project tools/WordSense.Builder -c Release -- basic-precision --output basic-precision.md
dotnet run --project tools/WordSense.Builder -c Release -- evaluate basic --output basic-evaluation.md
```

## Rules

The rules run in the order of the table; the first rule that fires wins. A rule fires only when the part of speech it
selects is one the target word can have. When no rule fires, or the selected part of speech has more than one sense
for the word, the model abstains.

| Order | Rule | Fires when | Initial confidence | Fired | Correct | Precision | Calibrated confidence |
| ----: | ---- | ---------- | -----------------: | ----: | ------: | --------: | --------------------: |
| 1 | `after-modal` | a modal or auxiliary verb is directly before the word | 0.90 | 1,728 | 1,721 | 0.996 | 0.99 |
| 2 | `after-to` | the word `to` is directly before the word | 0.70 | 1,597 | 1,561 | 0.977 | 0.98 |
| 3 | `after-subject-pronoun` | a subject pronoun is directly before the word | 0.80 | 1,556 | 1,542 | 0.991 | 0.99 |
| 4 | `after-determiner` | a determiner or possessive is directly before it | 0.85 | 5,913 | 5,626 | 0.951 | 0.95 |
| 5 | `after-degree-adverb` | a degree adverb is directly before the word | 0.70 | 99 | 88 | 0.889 | 0.89 |
| 6 | `after-preposition` | a preposition is directly before the word | 0.70 | 833 | 682 | 0.819 | 0.82 |
| 7 | `before-auxiliary` | an auxiliary verb is directly after the word | 0.70 | 525 | 511 | 0.973 | 0.97 |
| 8 | `before-noun` | a word that can only be a noun is directly after the word | 0.60 | 133 | 102 | 0.767 | 0.77 |

Anchors evaluated by `basic-precision`: 24,461. The calibrated confidence is the precision rounded to two decimals and
capped at 0.99, because a rule is never certain.

## Results

| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| holdout | 12,789 | 47.24% | 47.25% | 47.65% | 49.29% | 95.84% |
| real text | 689 | 49.64% | 49.64% | 49.63% | 51.96% | 95.53% |

| Measure | Basic |
| ------- | ----: |
| Classification latency (ms per sentence) | 0.01 |
| Word table first use (ms) | 152 |
| Word table first use (MiB) | 42.8 |

Basic answers about half of the ambiguous words, and about 96 percent of those answers are right, so its confidence
is a trustworthy filter: use the answered subset, and treat abstentions as unknown. The unanswered half is where the
encoder models earn their cost. The best-sense figure counts an abstention as wrong, which is why it is far below the
answered accuracy.

Basic chooses a part of speech only, so its pronunciation figure is limited to heteronyms whose meanings differ in
the part of speech.
