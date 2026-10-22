### DownloadMirror Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/DownloadMirrorTests.cs` construct mirrors and read them from an
injected environment getter; no process environment is modified. Nothing is mocked and no network access happens.

#### Test Environment

Standard xUnit v3 runner. No files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **DownloadMirror_Constructor_HttpsBase_IsAccepted**: a valid `https` base is accepted.
- **DownloadMirror_Constructor_InvalidBase_Throws**: an unsupported scheme, a query, a fragment and a URL with embedded
  user information are rejected; **DownloadMirror_Constructor_UserInfoInBase_Throws** covers user information with and
  without a password, and **DownloadMirror_Constructor_RelativeBase_Throws** a relative base.
- **DownloadMirror_Constructor_BadAuthentication_Throws**: both credentials and a bearer token, or a whitespace-only
  token, are rejected.
- **DownloadMirror_Constructor_CredentialsOverPlainHttp_OnlyLoopbackAllowed** and
  **DownloadMirror_Constructor_LoopbackNames_AllowSecretsOverHttp**: credentials over plain `http` are refused unless
  the host is a loopback name (`localhost`, `127.0.0.1`, `[::1]`).
- **DownloadMirror_FromEnvironment_NoUrl_ReturnsNull**: without `WORDSENSE_MIRROR_URL` there is no mirror.
- **DownloadMirror_FromEnvironment_AllVariables_BuildsMirror**: `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`,
  `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN` build a mirror (with a consistent subset of them).
- **DownloadMirror_FromEnvironment_InvalidConfiguration_Throws**: credentials without a URL, a user without a password,
  a URL that is not absolute, an `ftp` URL, a null getter, and user, password and bearer token all set are rejected; the
  messages contain none of the secret values, and a mistyped URL that carries a pasted credential is never echoed.
- **DownloadMirror_FromEnvironment_BearerWithoutUrlOrPasswordWithoutUser_Throws**: a bearer token without a URL, and a
  password without a user, are rejected.
- **DownloadMirror_FromEnvironment_WhitespaceOnlyCredentials_AreUnset**: whitespace-only user, password and token values
  count as unset.
- **DownloadMirror_ToString_RedactsSecrets**: neither the password, the user name nor the token appears; a credential
  and a token are shown as `***` and an absent one as `null`.
- **Mirror from options**: a mirror built from `DownloadOptions` (base address, bearer token or credentials) is
  validated by the same rules, so an invalid option set throws `ArgumentException` (`OnnxWordSenseModel` reports it as
  `DownloadConfigurationException` with this exception as inner exception).
