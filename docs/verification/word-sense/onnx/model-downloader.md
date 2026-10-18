### ModelDownloader Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/ModelDownloaderTests.cs` run a loopback `HttpListener` (`LocalServer`)
that serves small files, a failure, a corrupted file, a slow response, a redirect or a challenge. The mirror and GitHub
LFS base addresses are pointed at it with pinned digests substituted for the test files. No test uses the network.
Files are
requested and stored per model, under `<model-id>/<file>` on the mirror and in the store. Request logs show the order of
attempts and the headers received. Test methods are named `ModelDownloader_*`.

#### Test Environment

Standard xUnit v3 runner. A loopback listener on an ephemeral port and a temporary store directory per test. The mirror
environment is read through an injected getter, so the process environment is never modified.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Mirror serves**: the mirror is tried first at `<mirror>/<model-id>/<file>` and satisfies the request; no other source
  is contacted.
- **Source order and fall-through**: mirror first, then the GitHub LFS copy; a failing mirror falls through to GitHub LFS,
  and without a mirror GitHub LFS is the only source.
- **SHA-256 verification**: a copy with a wrong digest is never kept and the next source is tried; with no valid source
  the failure message lists every attempt.
- **Atomic store**: a file is written to a uniquely named temporary file and moved into place only after verification, so
  no partial or corrupt file is ever visible under its final name.
- **Already present**: a valid file in the store causes no request; a corrupt file is downloaded again, and a corrupt file
  is left in place when no source works.
- **Single in-flight download with retry**: concurrent requests for the same model share one download, and a request after
  a failure starts a new attempt (verified through the `OnnxWordSenseModel` scenarios and the request logs).
- **Environment mirror**: a mirror configured by `WORDSENSE_MIRROR_URL` (with the user, password or bearer-token
  variables) is used first, a mirror given in `MirrorOptions` takes precedence, and an invalid environment throws
  `ArgumentException` before any request, even when the cache is valid.
- **Credentials only to the mirror**: a bearer token or Basic credentials are sent to the mirror and never to GitHub
  LFS.
- **Same-origin credentials**: a mirror redirect to another origin is followed without credentials, a redirect within the
  origin keeps them, a redirect from `https` to `http` on the same host and port (a scheme downgrade, tested with a
  stub transport) is followed without credentials, a redirect loop falls through, and a redirect to a non-http(s)
  location is refused and falls through.
- **Timeouts and cancellation**: a slow source times out and falls through, also when it stalls after the headers
  (the limit covers the body); an invalid timeout (zero, negative,
  oversized) throws before any request; an infinite timeout succeeds; a cancelled caller throws
  `OperationCanceledException`.
- **Partial downloads**: a body that ends before its declared length, or stalls, falls through to the next source and
  leaves no temporary file; a body larger than the pinned size is refused with or without a declared length. The
  downloader does not resume: each attempt writes a new temporary file, and a stale `.part` file of an earlier run is
  neither used nor removed.
- **Explicit directory**: `Verify` of a model directory throws for a missing file (`FileNotFoundException`) and for an
  altered file (`InvalidDataException`).
- **Default store root**: the default store is beneath the user's local application data, in
  `DemaConsulting/WordSense/models`.
- **Failed sources are reported**: every source failing throws `HttpRequestException` whose message names each source.
