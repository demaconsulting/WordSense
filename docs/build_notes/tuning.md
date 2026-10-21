# Tuning the Basic Model

The statistical tables of the Basic model (in the core package `DemaConsulting.WordSense`) can be retrained and
re-measured with two
commands. This page describes the workflow and which data it is safe to tune against.

## Commands

Run both from the repository root.

```text
dotnet run --project tools/WordSense.Builder -c Release -- train-basic
dotnet run --project tools/WordSense.Builder -c Release -- evaluate basic
```

- `train-basic` retrains the whole model and writes `src/DemaConsulting.WordSense/Resources/basic.bin`. It reads
  the tag counts in `corpus/open/counts` (made by `open-counts` from the training split of the representative open
  corpus), the word table of the core package, and the anchors and chooser anchors in `corpus`. When the counts are
  missing it stops and names `open-counts`. It prints the sizes of the new table and its SHA-256.
- `evaluate basic` measures the table that is embedded in the built package, so rebuild after `train-basic`. It prints
  the same comparison table every run: best sense, part of speech and pronunciation on the holdout and the real-text
  sentences, the reference rows of earlier models, the calibration buckets, the thresholds, the table size and the
  latency.
- The core word image must be current (`compile-image` command) before training, because the dictionary is read from the
  core package.

Retraining is deterministic: the same inputs give a byte-identical table.

## Where the numbers live

Every tunable value is a field of `BasicHyperparameters` in `tools/WordSense.Builder/BasicHyperparameters.cs`: the
training choices (which words are closed-class, how many ambiguous words are tabled, the suffix and cue vocabularies),
the smoothing, the fusion weights, the sample sizes of the tuner and the tuning grid. The trainer, the tuner and the
header of the table all read this one file. The values the runtime needs (gamma, beta, the three lambdas, the HMM
weight and the inverse temperature) are written into the header of `basic.bin`, so a change of those values needs
retraining but no change in the runtime code. A change of the table format itself needs the reader in
`BasicTables.cs` and its tests to change too.

## What is safe to tune against

Tune against:

- the development split of the representative part of the open corpus (`corpus/open/representative/data/dev.tsv`);
- the anchor sample of the tuner: 5,000 training anchors drawn with a fixed seed, split into an anchor training subset
  and an anchor development subset (one sentence in five, by a hash of its text).

Never tune against the holdout sentences, the real-text sentences, the test splits of the open corpus or its targeted
challenge set; they are the measurements, and tuning on them makes the reported accuracy meaningless. The treebank is
not used for tuning or for training at all (it is an evaluation source only).

`tune-basic` writes nothing. It trains each variant of the grid in `BasicHyperparameters.Grid` (one parameter at a
time, the others at their default, and two combined variants) and prints one table with the same columns every run:

| Column | Meaning |
| ------ | ------- |
| Variant | The parameter and its value |
| Open dev ambiguous POS | Accuracy of the tagger on development tokens that have more than one allowed tag |
| Anchor dev analysis | Accuracy of the full model on the anchor development subset |
| Table size | Size of the serialized table |

```text
dotnet run --project tools/WordSense.Builder -c Release -- tune-basic
```

## Workflow

1. Change a value in `BasicHyperparameters.Default` (or add values to `Grid`) and run `tune-basic` to compare variants.
2. Put the chosen value into `Default`, run `train-basic`, rebuild, and run `evaluate basic`.
3. Compare the printed table with the one of the previous run. If the real-text number moved by more than the
   differences you expect from noise (689 rows: one row is about 0.15 points), be careful with the conclusion.
4. Commit `basic.bin`, the changed hyperparameter file and the updated numbers in `basic-evaluation.md`.

To change the training data (new anchors, sense inventory or open-corpus sentences), edit it and run `open-counts`
(when the open corpus changed) and `train-basic`. The measurement of the part-of-speech accuracy is `evaluate-tagger`.
