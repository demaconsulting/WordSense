### WordLexicon Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordLexicon` provides the embedded lexicon of known English words, with the possible analyses of every known
spelling, and the sense catalog: every fine sense of every lemma with its group (when it is in one) and its OEWN sense
key. Every model starts from it, and a word with one sense never needs context.

#### Data Model

- The lexicon is the embedded flat word image `wordsense.img`, read in place by the internal `WordImage`. Nothing is
  expanded into a dictionary: a lookup is a binary search over a UTF-8-byte-sorted spelling index followed by a decode
  of the analysis rows of that spelling.
- Image layout (WSI1 version 3): a header of 112 bytes (magic `WSI1`, version 3, section count, a reserved word, the
  SHA-256 of the data tables the image was built from at offset 16, the SHA-256 of all bytes from offset 112 at 48, and
  the 32-byte inventory hash at 80), a directory of 20-byte entries and the sections. The analysis sections are the
  spelling index and bytes, the features, the entries with their lemma and part of speech and the analysis rows (one
  row per `form` row: an entry index and a feature index; never one per sense), the lemma-to-forms
  CSR and the fold index. The catalog sections are: the sense ids sorted by UTF-8 bytes (a sense index is its
  position); the OEWN key suffix of each sense (the part of the key after `lemma%`, empty for a project sense, so the
  full key is the lower-case lemma, `%` and the suffix); the sense-to-group index (group index plus one, 0 for a sense in
  no group); the sense-to-respelling index (respelling index plus one, 0 for none) with the distinct respelling strings
  (retained in the format and checked by the catalog reader, but empty in the data and not exposed by the catalog
  records, so neither a sense nor a group has a respelling in the API);
  the group ids (UTF-8 sorted) and the part of speech of each group; the group members (CSR of sense indexes in
  inventory order; the lemma of a group comes from its members, and a group has no respelling of its own);
  the lemma and part-of-speech blocks (the lemma bytes and the part of speech, sorted by lemma UTF-8 bytes and
  then part of speech, each with a CSR of its sense indexes in inventory order); and a key index (the sense indexes
  sorted by full key bytes). The inventory hash covers, as the raw decompressed bytes of the sections each fed as id,
  length and bytes, the spelling, analysis, feature, entry lemma and part of speech, sense id, sense-group, group id,
  group part-of-speech and lemma-block sections; the encoder weights and `basic.bin` carry it and refuse an image with
  another hash. Each section is Brotli-compressed after a byte-plane shuffle, or stored when that is not smaller. The
  directory is validated completely when the image is opened, before any section is decompressed: every section lies
  after the directory and ends inside the image, no two sections overlap, a section id occurs once, the flags are
  either the stored flag (`0x100`) alone or a stride from 1 to 255, an empty section stores no bytes, a stored section is
  as long as its content, and a compressed section is shorter than its content and a whole number of records of its
  stride. A compressed section must be exactly one Brotli stream: the decoder reports the input it consumed, and bytes
  after the end of the stream are rejected. The
  analysis sections are decoded when the image is opened; the catalog sections are decoded on the first call of a
  catalog method.
- `WordDetails(Lemma, PartOfSpeech, Features)`: one analysis. `Features` is the inflection as `Key=Value` pairs joined
  by `+` (for example `Person=3+Tense=Pres`), or empty. A form that belongs to several lemmas or tenses (`left`,
  `bases`, `rows`, `read`) has one analysis for each.
- `SenseInfo(SenseId, GroupId, Lemma, PartOfSpeech, SenseKey)`: a sense of the catalog. `SenseId` is
  `lemma#label`; `GroupId` is null when the sense is in no group; `SenseKey` is the OEWN sense key (for example
  `bow%1:06:02::`) and is null for a project sense.
- `SenseGroup(GroupId, Lemma, PartOfSpeech, SenseIds)`: a group of senses of one lemma and part of speech.
  A group exists only where it was curated: it has at least two senses and never all the senses of its lemma and part
  of speech, and a sense is in at most one group. Its id is `lemma#group:label`, in a namespace of its own: the
  `#group:` marker is required by the builder and the image reader, so a group id can never equal a sense id
  (`lemma#label`). Sense ids are frozen; group ids are versioned per data release.
- Inventory order: the senses of a lemma and part of speech are in the order of Open English WordNet: the position of the
  sense in its entry, then the homograph number of the entry, with project senses after the dictionary senses and then
  by the bytes of the sense id; the WordNet 3.0 tag counts do not order them. The order is part of the image, so every
  model sees the same candidates.
- Lookup merge (`WordImage.TryLookup`; U+2019 is mapped to `'` first). When the spelling is stored exactly, the answer
  is exactly the analyses stored for
  that spelling, in their stored order, one for each lemma, part of speech and features; nothing from the lower-case
  spelling is merged in (the form tables already list the lower-case lemmas of `Polish` under the spelling `Polish`).
  When it is not stored, the answer starts with the analyses of the lower-case spelling, if that is stored, and goes on
  with the analyses of the other stored spellings that have the same lower-case form (the fold index, in its order),
  each analysis listed once at its first occurrence, so the lookup of `POLISH` is that of `polish` followed by the
  analyses of `Polish` that are not yet listed (the two `Title` analyses). The lookup never reorders by the case of the
  request. A capitalized spelling that has no entry of its own behaves like
  its lower-case base, and an inflected form behaves like the analyses generated for it at build time. The lookup lists
  candidates and never chooses among them by case: the spelling `Polish` lists the lemmas `Polish` (adjective and noun,
  also with the feature `Capitalization=Title`, which marks the analysis of the capitalized entry itself) and `polish`
  (noun and verb); the lower-case spelling `polish` lists the same lemmas without the `Title` marker, and `august` lists
  `august` and `August`; the name `Jack`, the month `May` and the entry `State` are listed only for the exact spellings
  `Jack`, `May` and `State`. A caller or model that wants the lemma of the written case chooses among the candidates
  (the Basic model does for the lemmas that the lower-case spelling carries, and does not ask for the exact capitalized
  spelling; see its design).
- Numerals policy: a token without a letter (a number written with digits such as `3` or `1999`, a number with
  separators such as `24/7` or `3.5`, punctuation) is never a word. `TryLookup` answers it absent before the cache and
  the image are consulted, and `FormsOf` returns no form for a lemma without a letter, exactly as the models treat
  such a token (`BasicClassifier.Normalize` returns an empty key for it, so it is unknown with a fixed tagger tag). The
  image still holds about 130 such spellings (the numerals of Open English WordNet, `3` as a noun and an adjective
  among them) that the lookup never shows; a future image regeneration can drop them from the data. `Count`
  is the count of the image and includes them. The check is on the letter, so `3rd`, `3D` and the number words
  (`three`, `twenty`) are found.
- Possessive fallback: when a spelling ending in `'s` or `s'` (straight or typographic apostrophe) is not stored and the
  stem is known, the lookup returns the analyses of the stem with `Poss=Yes` added to the features (alphabetical order
  kept). Contractions (`don't`, `it's`) are a closed list of stored words and never take this path. The reference
  tokenizer keeps an apostrophe between letters inside the word, so `dog's` reaches the lookup as one token, but a
  trailing apostrophe is a token of its own (`dogs'` becomes `dogs` and `'`); a caller that wants the plural possessive
  resolved passes `dogs'` as one word.

#### Key Methods

- `WordLexicon.Lookup(string)` and `TryLookup(string, out ImmutableArray<WordDetails>)`: the analyses of a word. The
  array is immutable and shared, so no caller can change what another caller sees.
- `WordLexicon.Contains(string)`, `FormsOf(string lemma)` and `Count`.
- `WordLexicon.SensesOf(string lemma, PartOfSpeech pos)`: the senses of a lemma and part of speech in inventory order
  (the order of Open English WordNet, project senses last); empty for a lemma or part of speech without senses; null
  lemma throws
  `ArgumentNullException`.
- `WordLexicon.TryGetSense(string senseId, out SenseInfo)`: binary search over the sorted sense ids.
- `WordLexicon.TryGetSenseByKey(string senseKey, out SenseInfo)`: binary search over the key index; the key is the
  full OEWN sense key. A project sense has no key and is never found this way.
- `WordLexicon.TryGetGroup(string groupId, out SenseGroup)`: the group with the ids of its senses in inventory order,
  which is how a group answer of a model leads to its member senses. A sense is in at
  most one group; a group has one lemma and one part of speech and at least two, but not all, of the senses of that
  lemma and part of speech, its id has the `#group:` marker and never equals a sense id, and its members agree with the
  sense-to-group index; the image reader checks this when it decodes the catalog.
- `WordLexicon.Version`: the first 16 lower-case hexadecimal digits of the inventory hash of the image, computed from
  the opened image (the first access throws `ModelIntegrityException` for a missing or corrupt image). It identifies the
  sense inventory: it changes whenever an analysis, a sense or a group changes, and is the same on every call. It is not
  a version number; consumers compare it for equality only, for example to detect that stored group ids were saved
  against another catalog.
- The image is opened lazily, thread-safely and exactly once (`Lazy` with execution-and-publication). A small
  direct-mapped cache of recent answers backs `TryLookup`, which is what the models call.
- Spelling order decision: the sort order of the spelling index (UTF-8 byte order) is not re-checked at load. Checking
  338K spellings would add work to every process start, and the content SHA-256 already verified by `WordImage.Open`
  covers the index bytes, so a committed image that opens cannot be mis-ordered unless the builder wrote it so. The
  full order check at every index therefore runs as a test (`ImageTests`) against the committed image.

#### Error Handling

If the embedded resource is missing or the image is corrupt (wrong magic or version, content hash mismatch, invalid
section or reference, a section that starts inside the header or the directory or runs past the image, overlapping or
duplicate sections, unsupported flags, inconsistent raw and stored lengths, bytes after the end of a compressed stream,
a sense that names a group or respelling that does not exist, a duplicate sense id, a group id
without the `#group:` marker or equal to a sense id, a group with
fewer than two members or with all the senses of its lemma and part of speech, a group that mixes lemmas or parts of
speech, a group that lists a sense twice, group members that disagree with the sense-to-group index, or a key index that
is not in strict key order), the first
access throws `ModelIntegrityException` (the original `InvalidDataException` of `WordImage.Open` is its inner
exception; `WordLexicon.OpenImage(Stream?)` does the mapping, and a missing embedded resource is reported the same
way). `WordImage.Open` itself still throws `InvalidDataException`, which the builder and the tests use directly.
Because the lazy value caches the failure, every later access throws as well. The
catalog sections are decoded and checked on their first use through `WordLexicon.ReadCatalog<T>(Func<WordImage, T>)`,
which `SensesOf`, `TryGetSense`, `TryGetSenseByKey` and `TryGetGroup` use and which maps a damaged catalog to a
`ModelIntegrityException` in the same way (the catalog checks include the order of the key index, that every sense that
names a group is listed exactly once by that group, and the structure and range of the retained respelling sections).

#### Dependencies

The .NET base class library and the embedded resource `wordsense.img`. No other WordSense unit.

#### Callers

`BasicClassifier`, `OnnxClassifier` and `WeightedLemmaTable` (all lookups and the sense catalog), the builder
(`ImageCompiler` writes the image; `Evaluator` and `AppendixGenerator` read it) and application code that wants the
analyses of a word or the senses and groups of a lemma.
