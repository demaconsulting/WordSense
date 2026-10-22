### DownloadMirror Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`DownloadMirror` is the validated configuration of an optional download mirror (for example an internal artifact
server). It guarantees that an accepted mirror can be used without leaking credentials.

#### Data Model

An internal sealed record of the base URI, an optional bearer token and optional basic credentials. The timeout is
held by `DownloadOptions`. Its printed form masks the secrets.

#### Key Methods

- Constructor validation: the URI shall be absolute, http or https, without query, fragment or user information;
  bearer token and basic credentials shall not both be given; a bearer token shall not be blank; credentials on a
  plain http URI are accepted only for `localhost` or a loopback address.
- `FromOptions(DownloadOptions)`: builds the mirror from the public options.
- `FromEnvironment(reader)`: builds the mirror from `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`,
  `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN`; blank values count as unset; returns none when no
  URL is set.

#### Error Handling

Any violation throws `ArgumentException` (or `ArgumentOutOfRangeException`); a credential variable without the URL, or
only one of user and password, is also an error. Messages about a bad URL never include its value. This is the raw,
internal vocabulary of the unit: `OnnxWordSenseModel` reports it to the caller as a `DownloadConfigurationException`
whose inner exception is the original one.

#### Dependencies

The core `DownloadOptions`.

#### Callers

`ModelDownloader`.
