# DownloadMirror Verification

## Verification Approach

Tests in `DownloadMirrorTests.cs` construct mirrors and read them from an injected environment
getter; no process environment is modified.

## Test Scenarios

- **DownloadMirror_Constructor_HttpsBase_IsAccepted**, **..._InvalidBase_Throws**,
  **..._RelativeBase_Throws**, **..._BadAuthentication_Throws**,
  **..._CredentialsOverPlainHttp_OnlyLoopbackAllowed**, **..._LoopbackNames_AllowSecretsOverHttp**
  (`localhost` and `[::1]`): validation rules.
- **DownloadMirror_FromEnvironment_NoUrl_ReturnsNull**, **..._AllVariables_BuildsMirror**,
  **..._InvalidConfiguration_Throws**, **..._BearerWithoutUrlOrPasswordWithoutUser_Throws**: the
  `WORDSENSE_MIRROR_*` variables.
- **DownloadMirror_ToString_RedactsSecrets**: no secret in the text form.

## Requirements Coverage

`WordSense-Mirror-Validation`, `WordSense-Mirror-Environment` and `WordSense-Mirror-Redaction`.
