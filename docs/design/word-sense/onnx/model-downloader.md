### ModelDownloader Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`ModelDownloader` makes the model files of a model available in the per-user store, verified against their pinned
SHA-256 digests, from a configured mirror or from the GitHub LFS copy of the repository.

#### Data Model

- `ModelSpec` and `ModelFile(Name, Sha256)` describe a model and the digest of each of its files.
- Store layout: `<store>/<model-id>/<file>`; the store defaults to the per-user `DemaConsulting/WordSense/models`
  folder or `MirrorOptions.StoreRoot`.
- Mirror layout: `<mirror>/<model-id>/<file>` with the segments escaped.
- GitHub LFS source: `https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/<model-id>/<file>`.
- The mirror is taken from `MirrorOptions` when given, otherwise from the environment variables
  `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN`.

#### Key Methods

- `Verify(directory, spec)`: checks that every file exists and matches its digest, without network access.
- `EnsureAsync(spec, mirrorOptions, ...)`: for each file, skips it when the stored file is valid; otherwise tries the
  sources in order (mirror, then GitHub LFS). A download goes to `<file>.<guid>.part` under the per-download timeout,
  is hashed (lower-case hexadecimal SHA-256), compared with the pinned digest and atomically moved into place; the
  temporary file is always deleted. A digest mismatch, an HTTP failure, a timeout or an I/O failure moves on to the
  next source.
- Redirects are followed manually (at most 10, http and https only). Credentials (bearer or basic, sent
  preemptively) are attached only to hops with the same scheme, host and port as the mirror.

#### Error Handling

`Verify` throws `FileNotFoundException` or `InvalidDataException`. When all sources fail for a file, `EnsureAsync`
throws `HttpRequestException` listing each source's failure (never a credential). Cancellation by the caller is
rethrown. An invalid mirror configuration throws `ArgumentException` (see DownloadMirror). Timeouts that are not
positive or infinite, or exceed about 49 days, are rejected.

#### Dependencies

`DownloadMirror`, the core `MirrorOptions`, the file system and `HttpClient`.

#### Callers

`OnnxWordSenseModel`.
