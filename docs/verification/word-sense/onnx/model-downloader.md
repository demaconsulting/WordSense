### ModelDownloader Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/ModelDownloaderTests.cs` run a loopback `HttpListener` (`LocalServer`)
that serves small files, a failure, a corrupted file, a slow response, a redirect or a challenge, and a stub transport
for GitHub's redirect to a signed CDN host. The release location (`PublicSources`) is a required argument of every
download test and points at the loopback server, so no test uses the network. Files are
requested and stored per model and version, under `<base-model>/v<version>/<file>` on the mirror and in the store. Request
logs show the order of attempts and the headers received. The retry wait is zero in the tests. Test methods are named
`ModelDownloader_*`.

#### Test Environment

Standard xUnit v3 runner. A loopback listener on an ephemeral port and a temporary store directory per test. The mirror
environment is read through an injected getter, so the process environment is never modified.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Mirror serves**: the mirror is tried first at `<mirror>/<base-model>/v<version>/<file>` and satisfies the request; no
  other source is contacted.
- **Source order and fall-through**: mirror first, then the release asset; a failing mirror falls through to the release
  asset, and without a mirror the release asset is the only source. The release URL keeps the slashes of the tag
  and escapes each segment.
- **Retry**: a 503 followed by a success needs two requests; a source that keeps failing is asked twice, then the next
  source is tried; a 404 is not retried; the wait is the `Retry-After` value up to 10 seconds, else 2 seconds.
- **SHA-256 verification**: a copy with a wrong digest is never kept and the next source is tried; with no valid source
  the failure message lists every attempt, one mismatch per source.
- **Atomic store**: a file is written to a uniquely named temporary file and moved into place only after verification, so
  no partial or corrupt file is ever visible under its final name.
- **Already present**: a valid file in the store causes no request; a corrupt file is downloaded again, and a corrupt file
  is left in place when no source works.
- **Single in-flight download with retry**: concurrent requests for the same model share one download, and a request after
  a failure starts a new attempt (verified through the `OnnxWordSenseModel` scenarios and the request logs).
- **Environment mirror**: a mirror configured by `WORDSENSE_MIRROR_URL` (with the user, password or bearer-token
  variables) is used first, a mirror given in `DownloadOptions` takes precedence, and an invalid environment throws
  `ArgumentException` before any request, even when the cache is valid (the downloader keeps the raw exception;
  `OnnxWordSenseModel` maps it to `DownloadConfigurationException`, see its unit document).
- **Credentials only to the mirror**: a bearer token or Basic credentials are sent to the mirror and never to the release
  host. A release that redirects to a signed CDN host (tested with a stub transport) sends no credential to either
  host.
- **Same-origin credentials**: a mirror redirect to another origin is followed without credentials, a redirect within the
  origin keeps them, a redirect from `https` to `http` on the same host and port (a scheme downgrade, tested with a
  stub transport) is followed without credentials, a redirect loop falls through, and a redirect to a non-http(s)
  location is refused and falls through.
- **Timeouts and cancellation**: a slow source times out and falls through, also when it stalls after the headers
  (the limit covers the body); an invalid timeout (zero, negative,
  oversized) throws before any request; an infinite timeout succeeds; a cancelled caller throws
  `OperationCanceledException`.
- **Invalid store root** (`ModelDownloader_EnsureAsync_InvalidStoreRoot_ThrowsBeforeRequests`): an empty, a
  whitespace-only and an invalid-character store root throw an `ArgumentException` named `StoreRoot` before any request.
- **Partial downloads**: a body that ends before its declared length, or stalls, falls through to the next source and
  leaves no temporary file; a body larger than the pinned size is refused with or without a declared length. The
  downloader does not resume: each attempt writes a new temporary file, and a stale `.part` file of an earlier run is
  neither used nor removed.
- **Explicit directory**: `Verify` of a model directory throws for a missing file (`FileNotFoundException`) and for an
  altered file (`InvalidDataException`); the downloader keeps these raw exceptions, and `OnnxWordSenseModel` reports
  them as `ModelUnavailableException` and `ModelIntegrityException` with the originals as inner exceptions.
- **Default store root**: the default store is beneath the user's local application data, in
  `DemaConsulting/WordSense/models`; a model's folder is `<base-model>/v<version>`, and files that lie directly in
  `<store>/<base-model>/` are not read (`ModelDownloader_EnsureAsync_OldStoreLayout_IsIgnored`).
- **Failed sources are reported**: every source failing throws `HttpRequestException` whose message names each source
  with its request path and no query, token or signed value (`OnnxWordSenseModel` reports it as
  `ModelUnavailableException` with this exception as inner exception).
