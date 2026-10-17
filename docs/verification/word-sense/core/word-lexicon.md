### WordLexicon Verification

#### Verification Approach

The unit is verified with the real embedded lexicon and with tiny hand-made blobs for the reader. Tests in
`test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs` use the public `WordLexicon.Words` property; tests in
`WordsBlobTests.cs` feed the internal `WordsBlob` reader valid and corrupt byte arrays produced by a small helper in the
test project (the writer lives in the builder, so the shipped assembly has no writer). Nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Known lemma has its senses**: `Words["read"]` holds two entries that share the lemma `read`; a single-sense word
  holds one.
- **Keys are lower case and include inflections**: every key equals its lower-cased form, and inflected forms such as
  `records` and `recorded` are keys whose entries carry the lemma `record`. No morphology runs at run time.
- **Unknown or mixed-case key is absent**: a made-up word and an upper-case spelling of a known word are not found,
  because lookup is ordinal on lower-case keys.
- **Lazy loading**: the lexicon is not loaded until the first access of `Words`, and later accesses return the same
  instance.
- **Thread safety**: many tasks that access `Words` at the same moment all receive the same instance, and the embedded
  resource is read once.
- **Shared entries**: equal strings and equal senses are shared, so two keys with the same sense return the same
  `WordDetails` instance.
- **Valid blob round trip**: a small blob read by the `WordsBlob` reader gives the expected keys, senses and order.
- **Corrupt blob is rejected**: an unknown format marker, truncated data, an invalid sense reference and trailing data
  each throw `InvalidDataException`.
- **Broken payloads are rejected**: a table of broken payloads (truncated magic, over-long or over-large varints,
  counts larger than the data, a string running past the end, an unknown part of speech, out-of-range meaning, key and
  sense references, an entry without senses, a duplicate key, trailing bytes) each throw `InvalidDataException` before
  a large allocation is made; the smallest valid blob is accepted.
