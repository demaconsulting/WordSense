### WordLexicon Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordLexicon` provides the embedded lexicon of known English words, with the possible analyses of every known
spelling, and the sense catalog: every fine sense of every lemma with its group, its OEWN sense key and its optional
respelling. Every model starts from it, and a word with one sense never needs context.

#### Data Model

- The lexicon is the embedded flat word image `wordsense.img`, read in place by the internal `WordImage`. Nothing is
  expanded into a dictionary: a lookup is a binary search over a UTF-8-byte-sorted spelling index followed by a decode
  of the analysis rows of that spelling.
- Image layout: a header (magic `WSI1`, version, SHA-256 of the data it was built from, SHA-256 of its content, and the
  inventory hash of the sense, group and analysis sections), a section directory and the sections: spelling index and
  bytes, analysis rows (lemma, part of speech and features; one row per analysis, never one per sense), entries,
  lemma-to-forms CSR and the fold index, and the catalog sections: sense ids (UTF-8 sorted), OEWN key suffixes (the part
  of the sense key after `lemma%`), the sense-to-group index, the sense-to-respelling index, group ids and parts of
  speech, the range of senses of each lemma and part of speech in inventory order, and a key-to-sense sorted index.
  Each section is Brotli-compressed after a byte-plane shuffle, or stored when that is not smaller. The catalog
  sections are decoded on first use of a catalog method.
- `WordDetails(Lemma, PartOfSpeech, Features)`: one analysis. `Features` is the inflection as `Key=Value` pairs joined
  by `+` (for example `Person=3+Tense=Pres`), or empty. A form that belongs to several lemmas or tenses (`left`,
  `bases`, `rows`, `read`) has one analysis for each.
- `SenseInfo(SenseId, GroupId, Lemma, PartOfSpeech, SenseKey, Respelling)`: a sense of the catalog. `SenseId` is
  `lemma#label`; `SenseKey` is the OEWN sense key (for example `bow%1:06:02::`) and is null for a project sense.
- `SenseGroup(GroupId, Lemma, PartOfSpeech, SenseIds, Respelling)`: a group of senses of one lemma and part of speech.
- Inventory order: the senses of a lemma and part of speech are ordered by WordNet 3.0 tag count, highest first, with
  project senses after the counted ones; the order is part of the image, so every model sees the same candidates.
- Lookup tries the exact spelling first, then the lower-cased spelling (U+2019 maps to `'`) through the fold index,
  so case-sensitive entries win over the folded form. A capitalized spelling that has no entry of its own behaves like
  its lower-case base, and an inflected form behaves like the analyses generated for it at build time.
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
- `WordLexicon.SensesOf(string lemma, PartOfSpeech pos)`: the senses of a lemma and part of speech in inventory order,
  the most frequent first; empty for a lemma or part of speech without senses; null lemma throws
  `ArgumentNullException`.
- `WordLexicon.TryGetSense(string senseId, out SenseInfo)`: binary search over the sorted sense ids.
- `WordLexicon.TryGetSenseByKey(string senseKey, out SenseInfo)`: binary search over the key index; the key is the
  full OEWN sense key. A project sense has no key and is never found this way.
- `WordLexicon.TryGetGroup(string groupId, out SenseGroup)`: the group with the ids of its senses. Every sense of the
  catalog is in exactly one group of its own lemma and part of speech; the image reader checks this when it decodes the
  catalog.
- The image is opened lazily, thread-safely and exactly once (`Lazy` with execution-and-publication). A small
  direct-mapped cache of recent answers backs `TryLookup`, which is what the models call.
- Spelling order decision: the sort order of the spelling index (UTF-8 byte order) is not re-checked at load. Checking
  338K spellings would add work to every process start, and the content SHA-256 already verified by `WordImage.Open`
  covers the index bytes, so a committed image that opens cannot be mis-ordered unless the builder wrote it so. The
  full order check at every index therefore runs as a test (`ImageTests`) against the committed image.

#### Error Handling

If the embedded resource is missing or the image is corrupt (wrong magic or version, content hash mismatch, invalid
section or reference, a sense that names a group or respelling that does not exist, or a duplicate sense id), the first
access throws `InvalidDataException`. Because the lazy value caches the failure, every later access throws as well. The
catalog sections report their own corruption on their first use in the same way.

#### Dependencies

The .NET base class library and the embedded resource `wordsense.img`. No other WordSense unit.

#### Callers

`BasicClassifier`, `OnnxClassifier` and `WeightedLemmaTable` (all lookups and the sense catalog), the builder
(`ImageCompiler` writes the image; `Evaluator` and `AppendixGenerator` read it) and application code that wants the
analyses of a word or the senses and groups of a lemma.
