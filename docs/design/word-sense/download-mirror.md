# DownloadMirror Unit Design

## Overview

`DownloadMirror` is an immutable record describing a user-hosted copy of the model files: an absolute base URL
plus optional Basic credentials or a bearer token. The environment variables are `WORDSENSE_MIRROR_URL`,
`WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN`.

## Data Model

`BaseUri`, `Credentials` (`NetworkCredential`), `BearerToken`.

## Methods

- Constructor: validates eagerly. The base must be an absolute http or https URL without query,
  fragment or user information; Basic credentials and a bearer token are mutually exclusive; a
  bearer token must not be blank; credentials require https unless the host is loopback.
- `FromEnvironment(getter)`: returns `null` when `WORDSENSE_MIRROR_URL` is unset; otherwise builds
  the mirror; credentials without a URL, or a user without a password (or the reverse), are errors.
  A blank or whitespace-only value counts as unset for every variable.
- `ToString()`: shows the base URL; the credentials and bearer token are replaced by `***` markers (or `null` when
  absent).

## Error Handling

- A null base URL raises `ArgumentNullException`; a null environment getter raises `ArgumentNullException`.
- The constructor raises `ArgumentException` for a relative base, a scheme other than http or https, a query,
  fragment or user information, both Basic credentials and a bearer token, a blank bearer token, and any credential
  with a plain-http non-loopback host.
- `FromEnvironment` raises `ArgumentException` for credentials without a URL, a user without a password (or the
  reverse), a URL that is not absolute, and a scheme other than http or https. The messages never echo the rejected
  URL, because a mistyped URL can carry a pasted credential. The constructor errors above also apply.
- Nothing is thrown by `ToString()`.

## Interactions

Consumed by `WordSenseModelDownloader`, which appends `all-MiniLM-L6-v2/{file}` to `BaseUri` and sends
the credentials only to requests of the same scheme, host and port as the mirror (including redirect hops).
