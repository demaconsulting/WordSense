# Model Comparison

This note compares the two ONNX encoder models of WordSense, `DemaConsulting.WordSense.MiniLM` and
`DemaConsulting.WordSense.BertBaseCased`, on the same data. The cells marked `TBD` are placeholders that the build
phase fills with the output of the builder command `evaluate`. The rule model is compared in `basic-evaluation.md`.

## Method

The builder runs `evaluate <modelId>` for `minilm` and `bert-base-cased`:

```text
dotnet run --project tools/WordSense.Builder -c Release -- evaluate minilm --output minilm-evaluation.md
dotnet run --project tools/WordSense.Builder -c Release -- evaluate bert-base-cased --output bert-evaluation.md
```

- **Holdout.** The sentences in `corpus/holdout/` were never used to train the weights. One row is one sentence with
  one braced target word.
- **Real text.** The real-sentence sets (`corpus/realtext/` and the real holdout sets) hold sentences from openly
  licensed or public-domain sources and are used for evaluation only.
- **Best sense.** The share of rows whose chosen sense (part of speech and, for a heteronym, meaning) was right. The
  encoder models always name their best candidate, so this measures the choice itself.
- **Part of speech.** The share of rows whose part of speech was right.
- **Pronunciation.** The share of rows of heteronym lemmas whose meaning (the pronunciation) was right.
- **Answered.** The share of rows with a confidence of at least 0.5, the point where the lemma's own abstain rule
  would switch; **answered accuracy** is the best-sense accuracy of those rows.
- **Weights size.** The size of the embedded weights file of the model.
- **Latency.** The mean time to classify one holdout sentence on the machine that ran the command, not a benchmark
  of other hardware.
- **Word table first use.** The time and the managed memory of the first use of `WordLexicon.Words`; the same
  for every model because the table is in the core package.

## Results

### all-MiniLM-L6-v2-int8 (`minilm`)

| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| holdout | TBD | TBD | TBD | TBD | TBD | TBD |
| real text | TBD | TBD | TBD | TBD | TBD | TBD |

### bert-base-cased-int8 (`bert-base-cased`)

| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| holdout | TBD | TBD | TBD | TBD | TBD | TBD |
| real text | TBD | TBD | TBD | TBD | TBD | TBD |

## Cost

| Measure | MiniLM | BertBaseCased |
| ------- | -----: | ------------: |
| Model download (`model_int8.onnx` and the other files) | 23.2 MB | 109.4 MB |
| Weights size (KiB) | TBD | TBD |
| Classification latency (ms per sentence) | TBD | TBD |
| Word table first use (ms) | TBD | TBD |
| Word table first use (MiB) | TBD | TBD |

The download sizes are the sums of the file sizes in `model-provenance.md`.

## Reading the Results

The figures of the two models are comparable because the holdout and the real-text sets are the same and the weights
are trained from the same anchors. Each set is a separate row, so a model that is better on the holdout but worse on
real text is visible. The recommendation in the user guide ("Choosing a Package") follows this table once the
placeholders are filled.
