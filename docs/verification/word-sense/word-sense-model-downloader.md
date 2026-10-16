# WordSenseModelDownloader Verification

## Verification Approach

Tests in `WordSenseModelDownloaderTests.cs` run a loopback `HttpListener` (`LocalServer`) that
serves small files, a failure, a corrupted file, a slow response or a challenge, and point the mirror,
Hugging Face and GitHub LFS base URIs at it with pinned digests substituted for the test files. No
test uses the network. Request logs show the order of attempts and the headers received.

## Test Scenarios

- **..._MirrorServes_UsesOnlyMirror**: the mirror is tried first and satisfies the request.
- **..._MirrorFails_FallsThroughInOrder**: mirror, then Hugging Face, then GitHub LFS.
- **..._NoMirror_HuggingFaceThenLfs**: without a mirror the Hugging Face source is first, and is
  skipped for the files that do not allow it.
- **..._HashMismatch_FallsThroughAndKeepsNothingBad**: a wrong digest falls through and leaves no
  partial file.
- **..._Timeout_FallsThrough**, **..._AllSourcesFail_ThrowsWithEveryAttempt**.
- **..._MirrorFromEnvironment_IsUsedFirst**, **..._ProgrammaticMirror_BeatsEnvironment**,
  **..._InvalidEnvironment_ThrowsBeforeRequests**.
- **..._BearerToken_SentOnlyToMirror**, **..._BasicCredentials_SentToMirror**.
- **..._MirrorRedirectsToOtherOrigin_SendsNoCredentialsThere**: a mirror that 302s to a second local
  server (with the private client and with a caller client that does not auto-redirect) leaves that
  server with no Authorization header; **..._MirrorRedirectsWithinOrigin_KeepsCredentials** keeps
  them on a same-origin redirect; **..._RedirectLoop_FallsThrough** abandons a redirect loop.
- **..._InvalidRequestTimeout_ThrowsBeforeRequests** (zero, negative, oversized),
  **..._InfiniteRequestTimeout_Succeeds**, **..._ProgressThrows_PropagatesWithoutFallThrough**.
- **..._AlreadyPresent_MakesNoRequests**, **..._CorruptExistingFile_IsRedownloaded**,
  **..._CorruptCacheAndAllSourcesFail_LeavesFileInPlace** (the corrupt file is not deleted when no source works).
- **..._InvalidEnvironmentWithValidCache_Throws**: a bad mirror environment is an `ArgumentException` even when the
  cache is already valid.
- **..._ProgressThrowsIOException_EndsInDownloadException**: an `IOException` from the progress callback is a
  source failure, so every source fails and `WordSenseModelDownloadException` is thrown.
- **WordSenseModelDownloader_FromDirectory_ChecksEveryFile**: a directory with a missing or altered
  file is rejected (`WordSense-Downloader-Location`); it covers `WordSenseModelLocation.FromDirectory`.
- **WordSenseModelDownloader_DefaultStoreRoot_IsPerUser**: the default store is beneath the user's
  local application data (`WordSense-Downloader-StoreRoot`).
- **..._CallerCancels_ThrowsOperationCanceled**, **..._Progress_IsReported**.

(All names are prefixed with `WordSenseModelDownloader_EnsureModel_` or `WordSenseModelDownloader_`.)

## Requirements Coverage

`WordSense-Downloader-Order`, `-Fallthrough`, `-Integrity`, `-AllFail`, `-Present`, `-Cancel`,
`-Progress`, `-MirrorConfig`, `-Credentials`, `-Options`, `-Location` and `-StoreRoot`.
