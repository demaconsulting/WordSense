# Model releases

The model files are not in the repository. Each model export is a GitHub release of this repository, and the library
downloads the files from its assets.

## Release layout

- Tag and release name: `models/<base-model>/v<n>`, for example `models/bert-base-cased/v1`. `v<n>` is the model export:
  the upstream checkpoint, the graph cut, the operator set and the output taps. A change of the full-precision graph is
  a new
  version. The tag is always an integer version, never `v1.0.0` (BuildMark would read such a tag as a library version).
- Assets:
  - the shared tokenizer files (`vocab.txt`, `tokenizer.json`, `config.json`, where the model has them),
  - one `model-<quantization>.onnx` per quantization (`int8-dynamic`, `int8-static`, `fp16`, `fp32`),
  - the documents: `README.md`, which describes the export (the upstream checkpoint with its link, pinned revision and
    license, what was changed in the graph and why, the shared files, the Apache License 2.0 attribution), and one
    `model-<quantization>.md` per quantization file (how it was made: script, tool versions, calibration set, input and
    output SHA-256; and why that quantization),
  - `LICENSE`, the Apache License 2.0 text,
  - a `<asset>.sha256` sidecar per asset: `<sha256>  <asset>` and a line feed.
- The sources of the documents are `model/<base-model>/v<n>/README.md` and `model/<base-model>/v<n>/model-<quantization>.md`
  in the repository, reviewed like the other documents. A new release must include `README.md`, and every
  `model-<quantization>.onnx` ships with its `model-<quantization>.md` (the script enforces both).
- The body of the release is a short summary that links to `README.md` and to each `model-<quantization>.md`, then
  lists the files with their size and SHA-256. The script writes it; it is not an asset and can be written again with
  `-UpdateBody`.
- Each package pins exactly one quantization and the SHA-256 and size of every file it downloads. The pins in the package
  are the trust anchor; the release and its sidecars are not trusted.
- No zip file and no single `SHA256SUMS` asset. The expected digests are documented in `model/<base-model>/SHA256SUMS`.
- The release is not the latest release (`make_latest=false`), so it never becomes the download of the library.

## Append only

Assets are added, never replaced or removed. The publish script enforces this:

- an asset that is already published with the same SHA-256 is skipped,
- an asset that is published with another SHA-256 stops the script before anything is uploaded,
- the script never replaces or removes an asset or a release, and no step has an overwrite flag,
- a document of the release (`README.md`, `model-<quantization>.md`) is an asset like the others: a published document
  cannot be changed, so write the document completely before the release is published. The body of a release is not an
  asset and can be rewritten (`-UpdateBody`).

The setting "immutable releases" of the repository stays off: it would block adding a quantization to a published
release.

### Tag rulesets (set up by the repository owner)

Two tag rulesets cover `models/**`. Both target tags with the pattern `models/**`:

- **Model tags: create**: restricts creation of a model tag. The repository admin role can bypass it, so the maintainers
  who publish models can create the tag of a new release.
- **Model tags: no move or delete**: restricts updates and deletions of a model tag. Nobody can bypass it, so a
  published model tag can neither be moved to another commit nor removed.

The rulesets are repository settings, not files of the pull request.

## Publish a model

Run on a maintainer machine with an authenticated `gh` (write access to the repository), from a PowerShell session in the
repository root. Call the script with `&` so that the file list is an array.

```powershell
& tools/model-release/publish-model.ps1 -BaseModel bert-base-cased -Version 2 `
    -Target (git rev-parse origin/main) -VerifyAgainst model/bert-base-cased/SHA256SUMS `
    -File 'model-int8-static.onnx=C:\work\export\model-int8-static.onnx', 'vocab.txt=C:\work\export\vocab.txt', `
          'config.json=C:\work\export\config.json', 'tokenizer.json=C:\work\export\tokenizer.json', `
          'LICENSE=model/bert-base-cased/LICENSE', 'README.md=model/bert-base-cased/v2/README.md', `
          'model-int8-static.md=model/bert-base-cased/v2/model-int8-static.md' -DryRun
```

Review the printed plan and the SHA-256 of every file, then run the same command without `-DryRun`. The script:

0. records the latest release of the repository before anything is written;
1. validates the tag, the asset names and the files, and checks the SHA-256 of every file (not of a document) against
   `-VerifyAgainst`;
2. stages the files with their `.sha256` sidecars in `-StageDir` (default: a temporary folder);
3. looks the release up (read only), decides per asset: upload, skip (identical) or stop (changed), and stops before any
   write when a new release has no `README.md` or a quantization file has no `model-<quantization>.md` (in the run or
   already published);
4. creates the release as a draft without making it the latest, with the body described above, uploads the new assets
   and checks size, digest and state of every asset;
5. publishes the draft (not as the latest release) and then watches the latest release for 30 seconds
   (`-LatestWaitSeconds`). GitHub moves that pointer lazily, so a publish can make the new release the latest one even
   with `--latest=false`, and a look right after the publish can still show the old value. When the pointer differs
   from the recorded one, the script restores it with `gh release edit <recorded> --latest` and watches again; it fails
   when it cannot restore it (after 3 attempts), or when the new release became the latest and there was none before.

Adding a quantization to a published release is the same command with only the new files and its document (here without
`-VerifyAgainst` when the digest list does not hold the file yet):

```powershell
& tools/model-release/publish-model.ps1 -BaseModel bert-base-cased -Version 1 `
    -File 'model-fp32.onnx=C:\work\export\model-fp32.onnx', 'model-fp32.md=model/bert-base-cased/v1/model-fp32.md' -UpdateBody
```

`-UpdateBody` writes the body of the release again from the assets it has after the run; without `-File` it changes
nothing but the body. The same switch adds the documents to a release that was published without them: `-File
'README.md=...', 'model-int8-dynamic.md=...' -UpdateBody`. The body is the only thing of a release that is not
append only. `-NotesFile` gives a body of your own.

After the release exists, run `dotnet run --project tools/WordSense.Builder -c Release -- fetch-models` once: the library
downloader checks the real URLs against the pins in the code. A new file also needs its digest in the pins of the package
and in `model/<base-model>/SHA256SUMS`.

## Add a model or a version

1. Prepare the files (`tools/model-prep`) and write the documents `model/<base-model>/v<n>/README.md` and
   `model/<base-model>/v<n>/model-<quantization>.md`.
2. Pin them in the encoder model of the package (`ModelSpec`: base model, version, files with SHA-256 and size, the
   `model-<quantization>.onnx` name) and list their digests in `model/<base-model>/SHA256SUMS`.
3. Publish the release as above.
4. Run `fetch-models`, then the tests with `WORDSENSE_REQUIRE_MODELS=true`.
