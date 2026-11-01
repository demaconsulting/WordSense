# all-MiniLM-L6-v2 v1

The encoder files of the release `models/all-MiniLM-L6-v2/v1`. The release is append only; see
`tools/model-release/README.md`.

| Asset | Purpose |
| ----- | ------- |
| `README.md` | This document: the export |
| `model-int8-dynamic.onnx` | ONNX encoder, int8 dynamically quantized. Output `last_hidden_state`, 384 dimensions |
| `model-int8-dynamic.md` | How `model-int8-dynamic.onnx` was made and what to know about it |
| `vocab.txt` | BERT uncased WordPiece vocabulary (30,522 entries) |
| `tokenizer.json` | Reference tokenizer definition (only for the tokenizer parity fixture; not downloaded) |
| `LICENSE` | The Apache License 2.0 that covers the model files |
| `<asset>.sha256` | The SHA-256 of each asset above, for humans and scripts |

## Upstream

- Base model: [`sentence-transformers/all-MiniLM-L6-v2`](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
  (BERT, 6 layers, hidden size 384, uncased vocabulary), Apache License 2.0.
- There is no pinned upstream ONNX file. The ONNX file was exported from the base model locally (see
  `model-int8-dynamic.md`), so it is not byte-identical to any `onnx/model_qint8_*.onnx` file of the Hugging Face
  repository (the upstream `model_qint8_avx512.onnx` is about 24.1 MB, this file 22,972,370 bytes). The Hugging Face
  source is never used for it.

## Changes to the graph

- **Exported.** The base model was exported to ONNX with the output `last_hidden_state`, the hidden states of the last
  layer; WordSense pools them itself. The exact export command was not recorded, and no other edit of the graph is
  known.
- No taps: the sense features are the last layer.
- **Quantized.** The weights are not the upstream values: see `model-int8-dynamic.md`.

## Shared files

`vocab.txt` and `tokenizer.json` are the files shipped with the base model.

## License

The model files are licensed under the Apache License 2.0; the full text is the asset `LICENSE`. The copyright is held by
the authors of the base model `sentence-transformers/all-MiniLM-L6-v2` (the sentence-transformers project and the
authors of the models it derives from, see its model page above); this repository claims no copyright in them.
`model-int8-dynamic.onnx` is changed (exported, quantized) and is not the original model. The authors of the base model
do not endorse these files. The attribution of the project is in the
[Credits and Licenses](https://github.com/demaconsulting/WordSense#credits-and-licenses) section of the repository
README.
