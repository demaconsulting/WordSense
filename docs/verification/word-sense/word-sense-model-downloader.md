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
- **..._AlreadyPresent_MakesNoRequests**, **..._CorruptExistingFile_IsRedownloaded**,
  **..._FromDirectory_ChecksEveryFile**, **..._DefaultStoreRoot_IsPerUser**.
- **..._CallerCancels_ThrowsOperationCanceled**, **..._Progress_IsReported**.

(All names are prefixed with `WordSenseModelDownloader_EnsureModel_` or `WordSenseModelDownloader_`.)

## Requirements Coverage

`WordSense-Downloader-Order`, `-Fallthrough`, `-Integrity`, `-AllFail`, `-Present`, `-Cancel`,
`-Progress`, `-MirrorConfig` and `-Credentials`.
