# Tuning the Basic Model

The statistical tables of the `DemaConsulting.WordSense.Basic` package can be retrained and re-measured with two
commands. This page describes the workflow and which data it is safe to tune against.

## Commands

Run both from the repository root.

```text
dotnet run --project tools/WordSense.Builder -c Release -- train-basic
dotnet run --project tools/WordSense.Builder -c Release -- evaluate basic
```

- `train-basic` retrains the whole model and writes `src/DemaConsulting.WordSense.Basic/Resources/basic.bin`. It reads
  the treebank counts in `corpus/ud`, the word table of the core package, the anchors and the sense inventory in
  `corpus`. When the counts are missing, or `--refresh-ud true` is given, it first runs `ud-counts`, which downloads
  the pinned UD English-EWT training and development files (verifying their SHA-256 checksums) into the git-ignored
  `tools/real-text/cache`. It prints the sizes of the new table and its SHA-256.
- `evaluate basic` measures the table that is embedded in the built package, so rebuild after `train-basic`. It prints
  the same comparison table every run: best sense, part of speech and pronunciation on the holdout and the real-text
  sentences, the reference rows of earlier models, the calibration buckets, the thresholds, the table size and the
  latency.
- The core word table must be current (`words` command) before training, because the dictionary is read from the
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

- the Universal Dependencies English-EWT development part (the first 2,002 sentences are scored);
- the anchor sample of the tuner: 5,000 training anchors drawn with a fixed seed, split into an anchor training subset
  and an anchor development subset (one sentence in five, by a hash of its text).

Never tune against the holdout sentences or the real-text sentences; they are the measurements, and tuning on them
makes the reported accuracy meaningless. The UD test part is not used at all and is not even downloaded.

`tune-basic` writes nothing. It trains each variant of the grid in `BasicHyperparameters.Grid` (one parameter at a
time, the others at their default) and prints one table with the same columns every run:

| Column | Meaning |
| ------ | ------- |
| Variant | The parameter and its value |
| UD dev ambiguous POS | Accuracy of the tagger on UD development tokens that have more than one allowed tag |
| Anchor dev best sense | Accuracy of the full model on the anchor development subset |
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

To change the training data (new anchors, sense inventory or UD counts), edit it and run `train-basic`; add
`--refresh-ud true` to regenerate the treebank counts.
