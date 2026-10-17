# Model Comparison

This note compares the two ONNX encoder models of WordSense, `DemaConsulting.WordSense.MiniLM` and
`DemaConsulting.WordSense.BertBaseCased`, on the same data. The figures are the output of the builder command
`evaluate`. The statistical Basic model is compared in `basic-evaluation.md`; the combined table of all three models
is generated into `model-table.md` (`evaluate <model> --table docs/build_notes/model-table.md`) and shown in the root
README.

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
| holdout | 12,789 | 96.87% | 97.22% | 96.41% | 90.74% | 98.69% |
| real text | 689 | 92.60% | 95.36% | 94.45% | 84.76% | 96.40% |

### bert-base-cased-int8 (`bert-base-cased`)

| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| holdout | 12,789 | 99.05% | 99.22% | 99.25% | 96.04% | 99.64% |
| real text | 689 | 97.39% | 98.40% | 98.20% | 89.55% | 99.19% |

### Confidence calibration and thresholds

Measured once per encoder model with `evaluate` (IntraOpThreads 2). Cells are rows / best-sense accuracy per confidence
bucket. Coverage is the share of the target words with a confidence at or above the threshold and precision is the
best-sense accuracy of those words. The Basic model's figures (0.6 / 0.8 / 0.9) are in `basic-evaluation.md`.

| Model | Set | < 0.4 | 0.4 - 0.6 | 0.6 - 0.7 | 0.7 - 0.8 | 0.8 - 0.9 | >= 0.9 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| MiniLM | holdout | 857 / 75.50% | 742 / 91.37% | 514 / 96.30% | 522 / 96.36% | 614 / 97.23% | 9540 / 99.26% |
| MiniLM | real-text | 78 / 65.38% | 48 / 89.58% | 26 / 76.92% | 35 / 97.14% | 30 / 96.67% | 472 / 97.67% |
| BertBaseCased | holdout | 331 / 79.46% | 418 / 96.41% | 365 / 99.18% | 589 / 99.32% | 685 / 99.27% | 10401 / 99.74% |
| BertBaseCased | real-text | 48 / 77.08% | 53 / 92.45% | 47 / 100.00% | 57 / 100.00% | 44 / 100.00% | 440 / 99.32% |

| Model | Set | Threshold | Coverage | Precision |
| --- | --- | ---: | ---: | ---: |
| MiniLM | holdout | 0.6 | 87.50% | 98.87% |
| MiniLM | holdout | 0.8 | 79.40% | 99.13% |
| MiniLM | holdout | 0.9 | 74.60% | 99.26% |
| MiniLM | real-text | 0.6 | 81.71% | 96.63% |
| MiniLM | real-text | 0.8 | 72.86% | 97.61% |
| MiniLM | real-text | 0.9 | 68.51% | 97.67% |
| BertBaseCased | holdout | 0.6 | 94.14% | 99.68% |
| BertBaseCased | holdout | 0.8 | 86.68% | 99.71% |
| BertBaseCased | holdout | 0.9 | 81.33% | 99.74% |
| BertBaseCased | real-text | 0.6 | 85.34% | 99.49% |
| BertBaseCased | real-text | 0.8 | 70.25% | 99.38% |
| BertBaseCased | real-text | 0.9 | 63.86% | 99.32% |

## Cost

| Measure | MiniLM | BertBaseCased |
| ------- | -----: | ------------: |
| Model download (`model_int8.onnx` and the other files) | 22 MiB | 104 MiB |
| Weights size (KiB) | 1,129 | 2,241 |
| Classification latency (ms per sentence) | 1.84 | 8.55 |
| Model first use (ms) | 388 | 521 |
| Process memory added by the model (MiB) | 64 | 150 |
| Word table first use (ms) | 201 | 173 |
| Word table first use (MiB) | 59.7 | 59.7 |

The download sizes are the sums of the sizes of the pinned files of each model (MiniLM: 23,203,878 bytes = 22.13 MiB;
BertBaseCased: 109,360,908 bytes = 104.29 MiB), in MiB (1 MiB = 1,048,576 bytes). The earlier evaluator summed every file
in the model folder, which gave 23 MiB for MiniLM; it now sums the pinned files only. They replace the decimal-megabyte
figures in `model-provenance.md` (23.2 MB and 109.4 MB). The latency is machine dependent.

The memory row is what the model adds, measured after the word lexicon had been loaded (the word table row, 59.7
MiB since every dictionary word has its inflected forms; it was 42.8 MiB before, see `inflection-coverage.md`); it
does not include the lexicon. The timing and memory rows are from the quiet reference run (the latency, model and
memory rows were not re-measured after the inflection change because a busy machine distorted the re-run; only the
word table rows and the accuracy were refreshed); the second run that
measured the calibration (MiniLM 1.92 ms, 40 MiB, 300 ms; BertBaseCased 12.77 ms, 127 MiB, 529 ms) was noisier, and
the accuracy figures were identical.

## Against the Earlier Baseline

The MiniLM figures before the layered restructure were holdout 96.91 percent, real text 92.60 percent, part of speech
97.22 percent and pronunciation 96.25 percent. After the restructure the weights are retrained and keyed by lemma:
holdout is 96.87 percent (down 0.04 points), real text and part of speech are unchanged, and pronunciation is 96.41
percent (up 0.16 points). The differences are within the noise of retraining and are not a regression.

## Reading the Results

BertBaseCased is clearly the more accurate model: about 2.2 points better on the holdout and 4.8 points better on
real text, at about six times the latency and nearly five times the download. MiniLM remains the small, fast
choice. The figures of the two models are comparable because the holdout and the real-text sets are the same and the
weights are trained from the same anchors. Each set is a separate row, so a model that is better on the holdout but
worse on real text would be visible; here BertBaseCased is better on both. The recommendation in the user guide
("Choosing a Package") follows this table.
