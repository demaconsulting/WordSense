# Model Provenance

This note records where each WordSense encoder model and its tokenizer vocabulary come from, how they are verified,
and the license terms that apply to them. There is one section per model. Every model has an identifier (the model
ID) that names its folder everywhere: `model/<model-id>/` in the repository, `<mirror>/<model-id>/` on a mirror,
`<store>/<model-id>/` in the per-user store and `<ref>/model/<model-id>/` on the GitHub LFS endpoint. The weights
trained for a model are embedded in the package of that model as the resource `weights.bin`; they are valid only for
the exact `model_int8.onnx` pinned in that package, which the SHA-256 check enforces.

## Download Sources

For both encoder models, each missing file is requested in this order and verified against its pinned SHA-256 before
it is accepted; a file that fails is discarded and the next source is tried:

1. A mirror, when one is passed to `EnsureModelAsync` or configured by the `WORDSENSE_MIRROR_URL` environment
   variable: `{mirror}/{model-id}/{file}`.
2. The GitHub Git LFS media endpoint of this repository (`media.githubusercontent.com`).

Hugging Face is not a download source. The files are written atomically into `<store>/<model-id>/` and reused
without network access. The SHA-256 values below are of the real content, not of the LFS pointer files.

## Basic

`DemaConsulting.WordSense.Basic` has no model files: its statistical table (`basic.bin`, an embedded resource of about
a megabyte or less) is trained by the builder command `train-basic` and compiled into the package. The table is
derived from:

- the tag counts in `corpus/ud`, which `ud-counts` produces from the Universal Dependencies English-EWT treebank
  (training and development parts only), pinned to the commit `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b`:
  `en_ewt-ud-train.conllu` with SHA-256 `D68E06122A702464C613076523D56740F047E5BBE89DD90EC32737E04D952143` and
  `en_ewt-ud-dev.conllu` with SHA-256 `39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8`. The
  treebank is licensed under CC BY-SA 4.0; see `corpus/ud/PROVENANCE.md`.
- the word table of the core package, and the training anchors and sense inventory of `corpus/`.

The SHA-256 of the committed `basic.bin` is printed by `train-basic` and recorded in `basic-evaluation.md`.

## MiniLM (all-MiniLM-L6-v2-int8)

Package: `DemaConsulting.WordSense.MiniLM`. Model ID: `all-MiniLM-L6-v2-int8`. Hidden size 384, uncased. Folder:
`model/all-MiniLM-L6-v2-int8/`. Weights: the embedded resource `weights.bin` of the package.

| File | Size (bytes) | SHA-256 |
| ---- | -----------: | ------- |
| `model_int8.onnx` | 22,972,370 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| `vocab.txt` | 231,508 | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |

The folder also holds `tokenizer.json` (711,661 bytes, SHA-256
`da0e79933b9ed51798a3ae27893d3c5fa4a201126cef75586296df9b4d2c62a0`), which is used only to generate the tokenizer
parity fixture of the tests and is not downloaded at run time.

Origin:

- Base model: `sentence-transformers/all-MiniLM-L6-v2` (BERT, six layers, 384 hidden units, uncased WordPiece
  vocabulary of 30,522 entries).
- `model_int8.onnx` was produced locally during the feasibility spike: the base model was exported to ONNX with a
  `last_hidden_state` output and quantized with ONNX Runtime dynamic int8 quantization. The exact command was not
  recorded.
- It is not byte-identical to any quantized file published in the Hugging Face repository (the upstream
  `model_qint8_avx512.onnx` is about 24.1 MB), which is why Hugging Face is not a source.
- `vocab.txt` is the standard BERT uncased vocabulary shipped with the base model.

License: the base model is licensed under the Apache License 2.0. The quantized derivative and the vocabulary are
redistributed under the same license, with attribution to the sentence-transformers project and the original MiniLM
authors.

## BertBaseCased (bert-base-cased-int8)

Package: `DemaConsulting.WordSense.BertBaseCased`. Model ID: `bert-base-cased-int8`. Hidden size 768, cased. Folder:
`model/bert-base-cased-int8/`. Weights: the embedded resource `weights.bin` of the package.

| File | Size (bytes) | SHA-256 |
| ---- | -----------: | ------- |
| `model_int8.onnx` | 108,477,896 | `dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d` |
| `vocab.txt` | 213,450 | `eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02` |
| `config.json` | 639 | `19aa23b85dfc4ce3e199a4ab06d51acd4b7bfc9ae9bae62b78823dd5dfb0c286` |
| `tokenizer.json` | 668,923 | `343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1` |

All four files are pinned by the package and downloaded.

Origin:

- Base model: `bert-base-cased` (BERT, 12 layers, hidden size 768, cased vocabulary).
- The upstream int8 export of bert-base-cased from Hugging Face (`onnx/model_int8.onnx`, 109,200,172 bytes, SHA-256
  `b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`) is a masked-language-model export that
  exposes only the language-model output scores.
- `model_int8.onnx` of this model is the derived encoder, not the upstream file: `tools/model-prep/make-bert-encoder.py`
  cuts the graph at the output of the last encoder layer and exposes it as `last_hidden_state`. No weights are changed;
  the language-model head is dropped. The file keeps the upstream name, so a mirror that serves the upstream file
  under this name fails the SHA-256 check and the download falls back to GitHub.
- `vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export.

License: the base model is licensed under the Apache License 2.0; the derivative and the vocabulary are redistributed
under the same license, with attribution to the authors of BERT.

## Adding a Model

A further encoder gets its own `model/<model-id>/` folder (files, `SHA256SUMS` and a README), its own section in this
note, a package that derives from `OnnxWordSenseModel`, and weights trained by the builder with `weights <modelId>`.

## Training Corpus Licences

The sentence corpus under `corpus/` is not shipped in the NuGet packages; only the derived weights are.
Every real sentence has a row in `corpus/provenance/*.tsv` (hash id, source, licence, URL, contributor or work,
origin `real`, `authored` or `llm-corpus`). Accepted sources:

- Public domain: Project Gutenberg works of US public-domain titles (header and licence block stripped).
- CC0: the Tatoeba CC0 subset.
- Open licences with attribution recorded: Tatoeba sentences under CC BY 2.0 FR (contributor recorded),
  Wikipedia example sentences (CC BY-SA 4.0, article URL recorded) and Universal Dependencies English EWT
  sentences (CC BY-SA 4.0). These are isolated single sentences used as labeled examples.
- Single web excerpts (`web-excerpt`) are allowed only as a last resort; none were used.

Project-written sentences carry `MIT (project-authored)`. Bulk scraping of a copyrighted site, continuous text of
more than one sentence, and paywalled or terms-prohibited sources are not used. Raw downloads are kept in a
git-ignored cache and are never committed.
