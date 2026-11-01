# Model: all-MiniLM-L6-v2

The encoder files that the embedded weights of `src/DemaConsulting.WordSense.MiniLM` are trained against. The files are
not in the repository: they are the assets of the GitHub release `models/all-MiniLM-L6-v2/v1`.

## Versions

A version is a model export: the upstream checkpoint, the graph cut, the operator set and the output taps. A change of
the full-precision graph is a new version. A release is append only; see `tools/model-release/README.md`.

| Version | Quantization | Export | Documents |
| ------- | ------------ | ------ | --------- |
| `v1` | `int8-dynamic` | `sentence-transformers/all-MiniLM-L6-v2`, `last_hidden_state`, dynamic int8 quantization | [README](v1/README.md), [model-int8-dynamic](v1/model-int8-dynamic.md) |

Each release holds the documents `README.md` (the export: upstream, changes, shared files, license) and one
`model-<quantization>.md` per quantization file (how it was made and why). Their sources are in the folder of the
version; the other files of that folder are the downloaded model files and are git-ignored.

`SHA256SUMS` lists the SHA-256 of every model asset, and `LICENSE` is the Apache License 2.0 text that covers them. The
library trusts the digests pinned in the package, never the release.

## Where the files go

- `EnsureModelAsync` downloads them into `<store>/all-MiniLM-L6-v2/v1/`, from the mirror (when configured) and then from
  the release assets.
- `dotnet run --project tools/WordSense.Builder -- fetch-models` fetches them into `model/all-MiniLM-L6-v2/v1/`
  (git-ignored except the documents); the tests and the Builder read that folder.

## License

The model files are licensed under the Apache License 2.0; the full text is in [LICENSE](LICENSE) and is also a release
asset. The copyright is held by the authors of the base model `sentence-transformers/all-MiniLM-L6-v2` (the
sentence-transformers project and the authors of the models it derives from, see its model page); this repository
claims no copyright in them. The changes are listed in the README of the version. The authors of the base model do not
endorse these files.
