# DownloadMirror Unit Design

## Overview

`DownloadMirror` is an immutable record describing a user-hosted copy of the model files. The
configuration model follows the sibling Speech library: an absolute base URL plus optional Basic
credentials or a bearer token. The environment variables are `WORDSENSE_MIRROR_URL`,
`WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and `WORDSENSE_MIRROR_BEARER_TOKEN`.

## Data Model

`BaseUri`, `Credentials` (`NetworkCredential`), `BearerToken`.

## Methods

- Constructor: validates eagerly. The base must be an absolute http or https URL without query,
  fragment or user information; Basic credentials and a bearer token are mutually exclusive; a
  bearer token must not be blank; credentials require https unless the host is loopback.
- `FromEnvironment(getter)`: returns `null` when `WORDSENSE_MIRROR_URL` is unset; otherwise builds
  the mirror; credentials without a URL, or a user without a password (or the reverse), are errors.
- `ToString()`: shows the base URL only; secrets are redacted.

## Interactions

Consumed by `WordSenseModelDownloader`, which appends `all-MiniLM-L6-v2/{file}` to `BaseUri` and sends
the credentials only to this mirror.
