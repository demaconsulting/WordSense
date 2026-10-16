# DownloadMirror Verification

## Verification Approach

Tests in `DownloadMirrorTests.cs` construct mirrors and read them from an injected environment
getter; no process environment is modified.

## Test Scenarios

- **DownloadMirror_Constructor_HttpsBase_IsAccepted**, **..._InvalidBase_Throws** (an unsupported scheme, a query,
  a fragment, and a URL with embedded user information `https://user:pw@mirror.example/m`),
  **..._UserInfoInBase_Throws** (user information with and without a password),
  **..._RelativeBase_Throws**, **..._BadAuthentication_Throws**,
  **..._CredentialsOverPlainHttp_OnlyLoopbackAllowed**, **..._LoopbackNames_AllowSecretsOverHttp**
  (`localhost` and `[::1]`): validation rules.
- **DownloadMirror_FromEnvironment_NoUrl_ReturnsNull**, **..._AllVariables_BuildsMirror**,
  **..._InvalidConfiguration_Throws**, **..._BearerWithoutUrlOrPasswordWithoutUser_Throws**,
  **..._WhitespaceOnlyCredentials_AreUnset** (whitespace-only user, password and bearer-token values count as unset,
  with and without a URL; a blank user with a real password is a password without a user): the
  `WORDSENSE_MIRROR_*` variables.
- **DownloadMirror_ToString_RedactsSecrets**: neither the password, the user name nor the token appears; a credential
  and a token are shown as `***` and an absent one as `null`.

## Requirements Coverage

`WordSense-Mirror-Validation`, `WordSense-Mirror-Environment` and `WordSense-Mirror-Redaction`.
