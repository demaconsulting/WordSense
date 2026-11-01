### ModelDownloader Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`ModelDownloader` makes the model files of a model available in the per-user store, verified against their pinned
SHA-256 digests, from a configured mirror or from the release assets of the repository on GitHub.

#### Data Model

- `ModelSpec` describes a model: the base model (`bert-base-cased`), the version (`1`, the model export: checkpoint,
  graph cut, operator set and output taps), its files, the ONNX file name `model-<quantization>.onnx`, and the resources
  of
  its embedded weights and head. `ModelFile(Name, Sha256, Length)` pins each file. The derived members are
  `ModelId` (the base model), `Quantization` (taken from the ONNX file name, `int8-dynamic`), `ReleaseTag`
  (`models/<base-model>/v<version>`), `StoreFolder` (`<base-model>/v<version>`) and `WeightsId` (the identity recorded
  inside the embedded weights and head files: `BlobModelId` when set, else `<base-model>/v<version>/<quantization>`).
  Both packages set `BlobModelId` to the identity their embedded files were built with.
- Store layout: `<store>/<base-model>/v<version>/<file>`; the store defaults to the per-user
  `DemaConsulting/WordSense/models` folder or `DownloadOptions.StoreRoot`. No other folder of the store is read.
- Mirror layout: `<mirror>/<base-model>/v<version>/<file>` with the segments escaped.
- Release source (`PublicSources`):
  `https://github.com/demaconsulting/WordSense/releases/download/models/<base-model>/v<version>/<file>`. The slashes of
  the tag stay literal and each segment is escaped. GitHub redirects the request to a signed CDN address.
- The mirror is taken from `DownloadOptions` when given, otherwise from the environment variables
  `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN`.
- The sidecars of the release (`<file>.sha256`) are never requested: the pins are the trust anchor.

#### Key Methods

- `Verify(spec, directory)`: checks that every file exists and matches its digest, without network access.
- `EnsureAsync(spec, downloadOptions, ...)`: for each file, skips it when the stored file is valid; otherwise tries the
  sources in order (mirror, then the release asset; `BuildSources`). A download goes to `<file>.<guid>.part` under the
  per-download timeout, is hashed (lower-case hexadecimal SHA-256), compared with the pinned digest and atomically moved
  into place; the temporary file is always deleted. A digest mismatch, an HTTP failure, a timeout or an I/O failure moves
  on to the next source. A transient failure (HTTP 408, 429 or 5xx, or a connection that broke during the transfer) is
  retried once on the same source after the wait of the `Retry-After` header, else 2 seconds, never more than 10
  seconds; a missing file, any other 4xx status, a digest mismatch, a timeout and a refused connection are not retried.
  The `storeRoot` argument selects the store when no `DownloadOptions` are passed (the builder command `fetch-models`
  uses it).
- Redirects are followed manually (at most 10, http and https only). Credentials (bearer or basic, sent
  preemptively) are attached only to hops with the same scheme, host and port as the mirror, so neither the release host
  nor the CDN host it redirects to receives them.

#### Error Handling

`Verify` throws `FileNotFoundException` or `InvalidDataException`. When all sources fail for a file, `EnsureAsync`
throws `HttpRequestException` that names the model and version and lists every source tried, in order, as
`<source> (<request URL without query>): <reason>`. The text never holds a query string (a signed redirect carries one),
a user-info or a credential. Cancellation by the caller is rethrown. An invalid mirror configuration throws
`ArgumentException` (see DownloadMirror). Timeouts that are not positive or infinite, or exceed about 49 days, are
rejected, and so is a store root that is empty, whitespace-only or a path the platform rejects (an `ArgumentException`
with the parameter name `StoreRoot`, checked before any request; a null store root means the default store). These are
the raw, internal exceptions of the unit: `OnnxWordSenseModel` wraps them at the public boundary
(`FileNotFoundException` and `HttpRequestException` as `ModelUnavailableException`, `InvalidDataException` as
`ModelIntegrityException`, `ArgumentException` and `ArgumentOutOfRangeException` as `DownloadConfigurationException`;
see _OnnxWordSenseModel Unit Design_), so a caller of the public API never sees them directly.

#### Dependencies

`DownloadMirror`, the core `DownloadOptions`, the file system and `HttpClient`.

#### Callers

`OnnxWordSenseModel` and the builder command `fetch-models`.
