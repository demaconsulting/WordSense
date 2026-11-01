# bert-base-cased v1

The encoder files of the release `models/bert-base-cased/v1`. The release is append only; see
`tools/model-release/README.md`.

| Asset | Purpose |
| ----- | ------- |
| `README.md` | This document: the export |
| `model-int8-dynamic.onnx` | ONNX encoder, int8 dynamically quantized. Output `last_hidden_state`, 768 dimensions |
| `model-int8-dynamic.md` | How `model-int8-dynamic.onnx` was made and what to know about it |
| `vocab.txt` | BERT cased WordPiece vocabulary (28,996 entries) |
| `config.json` | Model configuration published with the upstream export (hidden size 768) |
| `tokenizer.json` | Reference tokenizer definition (only for the tokenizer parity fixture) |
| `LICENSE` | The Apache License 2.0 that covers the model files |
| `<asset>.sha256` | The SHA-256 of each asset above, for humans and scripts |

## Upstream

- Base model: [`google-bert/bert-base-cased`](https://huggingface.co/google-bert/bert-base-cased) (BERT, 12 layers,
  hidden size 768, cased vocabulary), Apache License 2.0.
- ONNX export: [`Xenova/bert-base-cased`](https://huggingface.co/Xenova/bert-base-cased), file `onnx/model_int8.onnx`
  (109,200,172 bytes, SHA-256 `b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`), Apache License 2.0.
  It is a masked-language-model graph that exposes only `logits`, dynamically quantized to int8 by ONNX Runtime.

## Changes to the graph

- **Language-model head removed.** WordSense needs the hidden states, not the logits. The graph is cut at the output of
  the last encoder layer (`/bert/encoder/layer.11/output/LayerNorm/Add_1_output_0`), which is exposed as
  `last_hidden_state`, by `tools/model-prep/make-bert-encoder.py`.
- No weight is changed. The graph has no taps: the sense features are the last layer.
- The content differs from the upstream file: a mirror that serves the upstream file under the asset name fails
  SHA-256 verification, and the download falls back to the release assets.

## Shared files

`vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export. They are the same
bytes in every version of the model.

## License

The model files are licensed under the Apache License 2.0; the full text is the asset `LICENSE`. The copyright is held by
Google LLC and the other authors of the base model `bert-base-cased` and by the author of the ONNX export
`Xenova/bert-base-cased`; this repository claims no copyright in them. `model-int8-dynamic.onnx` is changed (graph cut)
and is not the original model. The authors of the base model do not endorse these files. The attribution of the project
is in the [Credits and Licenses](https://github.com/demaconsulting/WordSense#credits-and-licenses) section of the
repository README.
