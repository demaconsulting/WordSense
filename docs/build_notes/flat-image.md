# Flat Word Image: Measurements

The core package reads its word data in place from one embedded flat image (`wordsense.img`) instead of
materializing a dictionary of `WordDetails` lists at first use. The image is compiled by `compile-image` from the
authoritative tables in `data/`.

## Accuracy

Accuracy and the per-sentence outcomes are unchanged. The evaluator was run on the previous commit and on this one;
the outcome files are byte-identical for all three models.

| Model         | Holdout | Real text |
| ------------- | ------- | --------- |
| Basic         | 91.17   | 85.49     |
| MiniLM        | 96.91   | 92.60     |
| BertBaseCased | 99.05   | 97.39     |

## Cost of the Lexicon

Fifteen fresh processes per variant, on a shared development machine (Release, .NET 10).

| Measure                              | Old lexicon | Flat image  |
| ------------------------------------ | ----------- | ----------- |
| Cold start (first lookup), median    | 185.1 ms    | 54.5 ms     |
| Managed heap after first lookup      | 59.7 MiB    | 15.4 MiB    |
| Working-set growth                   | 86.4 MiB    | 28.3 MiB    |
| Lookup of a known word (materialize) | 117 ns      | 812 ns      |
| Lookup of an unknown word            | 42 ns       | 484 ns      |
| Embedded data size                   | 1,937,657 B | 1,866,721 B |

The lookup of the image builds the `WordDetails` list on demand, so a single lookup is slower than a dictionary read
but stays below 1 µs. A small direct-mapped cache of recent lookups keeps the model throughput at the previous level.
Model latency on the shared machine is noisy; the differences measured were within that noise.

## Image Content

- 338,070 spellings and 364,444 analyses (4,627 sense-scope rows, 167,583 newly bootstrapped entries)
- 19 wrong-lemma forms corrected (`wick*` use the verb `wick`, `re-sign*` use the verb `re-sign`)
