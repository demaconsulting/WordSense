# Basic Model Evaluation

This note records how the rule model `DemaConsulting.WordSense.Basic` is measured and how the confidence of each of its
rules is calibrated. The cells marked `TBD` are placeholders that the build phase fills with the output of the builder
commands `basic-precision` and `evaluate basic`.

## Method

- `basic-precision` runs the rules on every training anchor whose target word has at least two senses in the word
  table. For each rule it counts how often the rule fired first (**fired**) and how often the part of speech it
  selected was the gold part of speech of the anchor (**correct**); the precision is correct divided by fired. It
  prints the rule, the counts, the precision and the confidence the rule has now. The holdout is never read.
- The confidence of a rule is a fixed value in `Rules.cs`. The values below are initial estimates; they are calibrated
  to the measured precision after the first run, so a result with a confidence of 0.7 is right about 70 percent of the
  time on the anchors.
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
| 1 | `after-modal` | a modal or auxiliary verb is directly before the word | 0.90 | TBD | TBD | TBD | TBD |
| 2 | `after-to` | the word `to` is directly before the word | 0.70 | TBD | TBD | TBD | TBD |
| 3 | `after-subject-pronoun` | a subject pronoun is directly before the word | 0.80 | TBD | TBD | TBD | TBD |
| 4 | `after-determiner` | a determiner or possessive is directly before the word | 0.85 | TBD | TBD | TBD | TBD |
| 5 | `after-degree-adverb` | a degree adverb is directly before the word | 0.70 | TBD | TBD | TBD | TBD |
| 6 | `after-preposition` | a preposition is directly before the word | 0.70 | TBD | TBD | TBD | TBD |
| 7 | `before-auxiliary` | an auxiliary verb is directly after the word | 0.70 | TBD | TBD | TBD | TBD |
| 8 | `before-noun` | a word that can only be a noun is directly after the word | 0.60 | TBD | TBD | TBD | TBD |

Anchors evaluated by `basic-precision`: TBD.

## Results

| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| holdout | TBD | TBD | TBD | TBD | TBD | TBD |
| real text | TBD | TBD | TBD | TBD | TBD | TBD |

| Measure | Basic |
| ------- | ----: |
| Classification latency (ms per sentence) | TBD |
| Word table first use (ms) | TBD |
| Word table first use (MiB) | TBD |

Basic chooses a part of speech only, so its pronunciation figure is limited to heteronyms whose meanings differ in
the part of speech.
