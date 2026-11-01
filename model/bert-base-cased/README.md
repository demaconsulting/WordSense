# Model: bert-base-cased

The encoder files that the embedded weights of `src/DemaConsulting.WordSense.BertBaseCased` are trained against. The
files are not in the repository: they are the assets of the GitHub releases `models/bert-base-cased/v<n>`.

## Versions

A version is a model export: the upstream checkpoint, the graph cut, the operator set and the output taps. A change of
the full-precision graph is a new version. A release is append only; see `tools/model-release/README.md`.

| Version | Quantization | Export | Documents |
| ------- | ------------ | ------ | --------- |
| `v1` | `int8-dynamic` | `Xenova/bert-base-cased` int8, cut at the last encoder layer (`tools/model-prep/make-bert-encoder.py`) | [README](v1/README.md), [model-int8-dynamic](v1/model-int8-dynamic.md) |
| `v2` | `int8-static` | `Xenova/bert-base-cased` fp32, cut at the last encoder layer, statically quantized, taps of layers 9 to 11 (`tools/model-prep/make-bert-static-encoder.py`) | [README](v2/README.md), [model-int8-static](v2/model-int8-static.md) |

Each release holds the documents `README.md` (the export: upstream, changes, shared files, license) and one
`model-<quantization>.md` per quantization file (how it was made and why). Their sources are in the folder of the
version; the other files of that folder are the downloaded model files and are git-ignored.

`SHA256SUMS` lists the SHA-256 of every model asset of the versions, and `LICENSE` is the Apache License 2.0 text that
covers them. The library trusts the digests pinned in the package, never the release.

## Where the files go

- `EnsureModelAsync` downloads them into `<store>/bert-base-cased/v<n>/`, from the mirror (when configured) and then
  from the release assets.
- `dotnet run --project tools/WordSense.Builder -- fetch-models` fetches them into `model/bert-base-cased/v<n>/`
  (git-ignored except the documents); the tests and the Builder read that folder.

## License

The model files are licensed under the Apache License 2.0; the full text is in [LICENSE](LICENSE) and is also a release
asset. The copyright is held by Google LLC and the other authors of the base model `bert-base-cased` (see its model
page [`google-bert/bert-base-cased`](https://huggingface.co/google-bert/bert-base-cased)) and by the author of the ONNX
export `Xenova/bert-base-cased` on which the encoders are based; this repository claims no copyright in them. The
changes of each version are listed in its README. The authors of the base model do not endorse these files.
