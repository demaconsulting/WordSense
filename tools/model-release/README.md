# Model releases

The model files are not in the repository. Each model export is a GitHub release of this repository, and the library
downloads the files from its assets.

## Release layout

- Tag and release name: `models/<base-model>/v<n>`, for example `models/bert-base-cased/v1`. `v<n>` is the model export:
  the upstream checkpoint, the graph cut, the opset and the output taps. A change of the full-precision graph is a new
  version. The tag is always an integer version, never `v1.0.0` (BuildMark would read such a tag as a library version).
- Assets:
  - the shared tokenizer files (`vocab.txt`, `tokenizer.json`, `config.json`, where the model has them),
  - one `model-<quantization>.onnx` per quantization (`int8-dynamic`, `int8-static`, `fp16`, `fp32`),
  - `LICENSE`, the Apache License 2.0 text,
  - a `<asset>.sha256` sidecar per asset: `<sha256>  <asset>` and a line feed.
- Each package pins exactly one quantization and the SHA-256 and size of every file it downloads. The pins in the package
  are the trust anchor; the release and its sidecars are not trusted.
- No zip file and no single `SHA256SUMS` asset. The expected digests are documented in `model/<base-model>/SHA256SUMS`.
- The release is not the latest release (`make_latest=false`), so it never becomes the download of the library.

## Append only

Assets are added, never replaced or removed. The publish script enforces this:

- an asset that is already published with the same SHA-256 is skipped,
- an asset that is published with another SHA-256 stops the script before anything is uploaded,
- the script never replaces or removes an asset or a release, and no step has an overwrite flag.

The setting "immutable releases" of the repository stays off: it would block adding a quantization to a published
release.

### Tag ruleset (set up by the repository owner)

A tag ruleset on `models/**` stops anyone from moving or removing a model tag:

- Target: tags, pattern `models/**`.
- Rules: restrict updates, restrict deletions, restrict creation to the maintainers who publish models.

The ruleset is a repository setting, not a file of the pull request.

## Publish a model

Run on a maintainer machine with an authenticated `gh` (write access to the repository), from a PowerShell session in the
repository root. Call the script with `&` so that the file list is an array.

```powershell
& tools/model-release/publish-model.ps1 -BaseModel bert-base-cased -Version 2 `
    -Target (git rev-parse origin/main) -VerifyAgainst model/bert-base-cased/SHA256SUMS `
    -File 'model-int8-static.onnx=C:\work\export\model-int8-static.onnx', 'vocab.txt=C:\work\export\vocab.txt', `
          'config.json=C:\work\export\config.json', 'tokenizer.json=C:\work\export\tokenizer.json', `
          'LICENSE=model/bert-base-cased/LICENSE' -DryRun
```

Review the printed plan and the SHA-256 of every file, then run the same command without `-DryRun`. The script:

1. validates the tag, the asset names and the files, and checks every SHA-256 against `-VerifyAgainst`;
2. stages the files with their `.sha256` sidecars in `-StageDir` (default: a temporary folder);
3. looks the release up (read only) and decides per asset: upload, skip (identical) or stop (changed);
4. creates the release as a draft without making it the latest, uploads the new assets and checks size, digest and state
   of every asset;
5. publishes the draft (not as the latest release) and checks that the latest release is unchanged.

Adding a quantization to a published release is the same command with only the new files (here without `-VerifyAgainst`
when the digest list does not hold the file yet):

```powershell
& tools/model-release/publish-model.ps1 -BaseModel bert-base-cased -Version 1 `
    -File 'model-fp32.onnx=C:\work\export\model-fp32.onnx'
```

After the release exists, run `dotnet run --project tools/WordSense.Builder -c Release -- fetch-models` once: the library
downloader checks the real URLs against the pins in the code. A new file also needs its digest in the pins of the package
and in `model/<base-model>/SHA256SUMS`.

## Add a model or a version

1. Prepare the files (`tools/model-prep`).
2. Pin them in the encoder model of the package (`ModelSpec`: base model, version, files with SHA-256 and size, the
   `model-<quantization>.onnx` name) and list their digests in `model/<base-model>/SHA256SUMS`.
3. Publish the release as above.
4. Run `fetch-models`, then the tests with `WORDSENSE_REQUIRE_MODELS=true`.
