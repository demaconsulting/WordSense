# Fit speed of the fine-sense weights (increment I4, follow-up)

The fit of the per-lemma classifiers was the slow step of `WordSense.Builder weights <model>` once the embeddings were
cached: the one-versus-rest logistic regression of every lemma was solved by Newton's method in the feature space (384
dimensions for MiniLM, 768 for BERT base cased), and the thresholds needed five more fits per lemma for the
cross-validation. For the 20 lemmas of the first batch the BERT fit took 11.5 minutes; at the planned 740 or more
lemmas that is hours. This note records the change and its measurements.

## What changed

- **Kernel solver.** The weight vector of an L2-regularized linear model lies in the span of its training samples, and a
  lemma has 50 to 500 anchors against 384 or 768 features. `KernelBasis` takes the Cholesky factor of the Gram matrix of
  the anchors of the training part (one Gram matrix per lemma, shared by the five folds and the final fit); its rows are
  coordinates of the samples in an orthonormal basis of their span, with the same inner products, so Newton's method
  runs on at most as many coordinates as there are samples and takes the same iterates as in the feature space. A
  duplicate sentence adds no coordinate. The solution is expanded back into a weight vector over the features. When a
  training part has at least as many samples as the encoder has features the feature-space solver is used, as before.
- **Parallelism.** The senses of one fit and the folds of one cross-validation are fitted concurrently, each into its
  own slot (the lemmas already were), so the longest lemma no longer decides the run time. All sums run in a fixed
  scalar order and only element-wise updates are vectorized, so the weights are byte-identical whatever the thread
  count and the processor.
- **Reference solver.** `weights <model> --solver primal` keeps the feature-space Newton method. Its result is the
  committed blobs of the previous increment byte for byte (the refactoring of the trainer changed no number), which is
  the evidence that the old behavior is preserved.
- **Fit cache.** The solver is part of the key and the format version is 3, so no fit of the feature-space solver is
  reused for the kernel solver or the other way round.

## Fit times (20 lemmas, 3,488 anchors, 16 threads, 28 logical processors, ONNX embeddings cached)

<!-- markdownlint-disable MD013 -->

| Model | Feature-space solver (before) | Kernel solver, senses and folds in sequence | Kernel solver, parallel (now) | Speed-up |
| --- | ---: | ---: | ---: | ---: |
| MiniLM (384 dimensions) | 76.7 s | 79.9 s | 8.6 s | 8.9 times |
| BERT base cased (768 dimensions) | 691.5 s (11 min 31 s) | 87.9 s | 9.4 s | 73.6 times |

<!-- markdownlint-enable MD013 -->

The MiniLM fit gains nothing from the kernel form alone: its largest lemmas (`right` 418 anchors, `go` 405) have about
as many anchors as the encoder has dimensions, so the time was spent in the two longest lemmas. The parallel senses and
folds remove that tail. The whole run, including the held-out gate, took 10 s for MiniLM and 13 s for BERT (before: 78 s
and 11 min 35 s).

## Equivalence (gate: within 0.2 points on the held-out sentences of batch 1)

The new blobs are 208,063 bytes (MiniLM, before 208,298) and 405,020 bytes (BERT, before 404,951). The held-out
evaluation (984 rows) with the new weights and with the previous weights, through the same classifier:

<!-- markdownlint-disable MD013 -->

| Model | Sense share / precision | Group share / precision | Part-of-speech share / precision | Fine top-1 (all rows) | Part of speech |
| --- | ---: | ---: | ---: | ---: | ---: |
| MiniLM, previous weights | 50.20% / 96.15% | 8.13% / 97.50% | 41.67% / 88.54% | 76.73% | 94.72% |
| MiniLM, new weights | 50.20% / 96.15% | 8.13% / 97.50% | 41.67% / 88.54% | 76.73% | 94.72% |
| BERT base cased, previous weights | 81.50% / 96.63% | 5.39% / 98.11% | 13.11% / 87.60% | 86.79% | 97.87% |
| BERT base cased, new weights | 81.50% / 96.63% | 5.39% / 98.11% | 13.11% / 87.60% | 86.79% | 97.87% |

<!-- markdownlint-enable MD013 -->

The difference is 0.00 points in every column. The answer (level, sense or group) of every one of the 984 rows is the
same; the confidence of 13 MiniLM rows and 4 BERT rows differs in the sixth decimal. The thresholds found are the same
in number (fine threshold for 20 of 20 lemmas, group threshold for 11 of 16 lemmas for MiniLM and 9 of 16 for BERT).
The half-precision gate reads 82.93% and 93.70% fine top-1 for full and half precision (difference 0.00). The unit
tests check the kernel basis against the Gram matrix, the weights, biases and thresholds against the feature-space
solver (also with duplicate sentences), and the bit-for-bit repetition (`KernelFitTests`).

## Reproducing

```text
dotnet run --project tools/WordSense.Builder -c Release -- weights bert-base-cased --cache <dir>
dotnet run --project tools/WordSense.Builder -c Release -- weights bert-base-cased --cache <dir> --solver primal
```

Use a cache folder that holds only `embeddings.bin` for a timing run (the fit cache `fits.bin` would otherwise answer
the lemmas), and the same thread count for both.
