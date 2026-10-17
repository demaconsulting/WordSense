# Universal Dependencies counts

These files are the tag statistics that the statistical model of the `DemaConsulting.WordSense.Basic` package is trained
from. They are counts only; the treebank itself is not stored in this repository.

| File | Content |
| ---- | ------- |
| `tag-ngrams.tsv` | Header `order`, `tags`, `count`. Unigram, bigram and trigram counts of the part-of-speech tags; the tags are the letters `NRVJBPCIDATUOS` (`S` marks a sentence boundary), separated by spaces. |
| `word-tags.tsv` | Header `word`, `counts`. For each lower-case word made of letters and apostrophes, the number of occurrences per tag, for example `record` with `N:12,V:3`. |
| `LICENSE-CC-BY-SA-4.0.txt` | The text of the Creative Commons Attribution-ShareAlike 4.0 International license. |

Both TSV files are UTF-8 without a byte-order mark, with LF line endings, in a deterministic order.

## Provenance

- Source: the UD English-EWT treebank (English Web Treebank), <https://github.com/UniversalDependencies/UD_English-EWT>.
- Pinned commit: `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b`.
- Files counted: the training and development parts only; the test part is never used.
  - `en_ewt-ud-train.conllu`, SHA-256 `D68E06122A702464C613076523D56740F047E5BBE89DD90EC32737E04D952143`
  - `en_ewt-ud-dev.conllu`, SHA-256 `39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8`
- License: Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0), see
  `LICENSE-CC-BY-SA-4.0.txt`. The treebank is credited in the "Credits and licenses" section of the main README.

## Regeneration

```text
dotnet run --project tools/WordSense.Builder -c Release -- ud-counts
```

The command downloads the two pinned files into the git-ignored `tools/real-text/cache` directory when they are
missing, verifies their checksums, and rewrites the two TSV files here. `train-basic` calls it when the files are
missing or when `--refresh-ud true` is given. See `docs/build_notes/tuning.md`.

## Tag mapping

| Universal tag | Tag letter |
| ------------- | ---------- |
| NOUN, PROPN | N |
| PRON | R |
| VERB | V |
| ADJ | J |
| ADV | B |
| ADP | P |
| CCONJ, SCONJ | C |
| INTJ | I |
| DET | D |
| AUX | A |
| PART with the form "to" | T |
| PART otherwise, and every other tag | O |
| PUNCT | U |
| sentence boundary | S |
