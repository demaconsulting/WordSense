# bert-base-cased v2

The encoder files of the release `models/bert-base-cased/v2`. The release is append only; see
`tools/model-release/README.md`.

| Asset | Purpose |
| ----- | ------- |
| `README.md` | This document: the export |
| `model-int8-static.onnx` | ONNX encoder, int8 statically quantized. Outputs `last_hidden_state` (layer 12) and the taps `hidden_9`, `hidden_10` and `hidden_11`, 768 dimensions each |
| `model-int8-static.md` | How `model-int8-static.onnx` was made and why this quantization |
| `vocab.txt` | BERT cased WordPiece vocabulary (28,996 entries); the same file as in v1 |
| `config.json` | Model configuration published with the upstream export (hidden size 768); the same file as in v1 |
| `tokenizer.json` | Reference tokenizer definition (only for the tokenizer parity fixture); the same file as in v1 |
| `LICENSE` | The Apache License 2.0 that covers the model files |
| `<asset>.sha256` | The SHA-256 of each asset above, for humans and scripts |

## Upstream

- Base model: [`google-bert/bert-base-cased`](https://huggingface.co/google-bert/bert-base-cased) (BERT, 12 layers,
  hidden size 768, cased vocabulary), Apache License 2.0.
- ONNX export: [`Xenova/bert-base-cased`](https://huggingface.co/Xenova/bert-base-cased), file `onnx/model.onnx` (fp32),
  Apache License 2.0. The export is a masked-language-model graph: it exposes only `logits`.
- Pinned revision: `TODO-PIN-UPSTREAM-REVISION` (a placeholder until the workflow `model-prep` resolves it and the
  script pins it, see `model-int8-static.md`).

## Changes to the graph

v2 is cut from the fp32 export. v1 is cut from the int8 export of the same repository, so the two versions differ in
the full-precision graph.

- **Language-model head removed.** WordSense needs the hidden states, not the logits. The graph is cut at the output of
  the final LayerNorm of the last encoder layer (`/bert/encoder/layer.11/output/LayerNorm/`), which is exposed as
  `last_hidden_state`.
- **Taps added.** The output of the final LayerNorm of layers 9, 10 and 11 is exposed as `hidden_9`, `hidden_10` and
  `hidden_11` (an `Identity` node each). The sense features of a word are the mean of the hidden states of layers 9 to
  12; the part-of-speech and tense heads read layer 12 (see `docs/design/word-sense/onnx/onnx-encoder.md`). The taps
  change no other node of the graph, and `last_hidden_state` is bit-identical with and without them.
- **Operator set 13.** Per-channel quantization needs it; the outputs of the full-precision graph do not change.
- **Quantized.** The weights are not the upstream values: see `model-int8-static.md`.

## Shared files

`vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export. They are the same
bytes in every version of the model.

## License

The model files are licensed under the Apache License 2.0; the full text is the asset `LICENSE`. The copyright is held by
Google LLC and the other authors of the base model `bert-base-cased` and by the author of the ONNX export
`Xenova/bert-base-cased`; this repository claims no copyright in them. The files are changed (graph cut, taps,
quantization) and are not the original model. The authors of the base model do not endorse them. The attribution of the
project is in the [Credits and Licenses](https://github.com/demaconsulting/WordSense#credits-and-licenses) section of the
repository README.
