### OewnImporter Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`OewnImporter` creates the tables once from the pinned Open English WordNet 2025 LMF file. After the import the tables
are edited by hand; the importer never overwrites them unless told to. It also imports the pinned WordNet 3.0 sense
frequency counts into the `sense_frequency` table.

#### Data Model

The importer streams the XML (DTD ignored, no resolver; plain or gzip input) and writes the tables `source`, `entry`,
`synset`, `gloss`, `example`, `sense`, `synset_relation`, `sense_relation`, `pronunciation` and `id_map`.

Derived columns: `entry.homograph_no` (0 when the lemma and part of speech is unique, else 1..n by the numeric tail of
the OEWN entry id); entry part of speech `s` becomes `a`; `synset.head_synset_id` from the `similar` relation of a
satellite synset; `sense.sense_key` from the sense id tail; `gloss.seq`; `entry.token_count` and `head_idx` (a
heuristic: the first token of a verb, the token before the first preposition of a noun, else the last token).
Hyponym-type relations are stored as their inverse (the one direction of the schema) and deduplicated; symmetric
relations are stored once with the smaller identifier first. Text is cleaned (tab and line break to a space, NFC) and
the number of cleaned cells is reported.

Left out and documented in `data/PROVENANCE.md`: subcategorization, adjective position, syntactic behaviour, ILI
definitions and links, `dc:source` and `Form`.

Sense frequency counts: the pinned WordNet 3.0 `index.sense` file has one line per sense key (`sense_key synset_offset
sense_number tag_count`). The importer joins the lines to the `sense` table on the sense key and writes one
`sense_frequency` row for every sense that has a key: the tag count of its line, or 0 when the file has no line for the
key; the source column is `wn30`. A line whose key is not a sense of the data is counted and reported, not written. The
counts are used under the WordNet license notice, which `data/PROVENANCE.md`, the data README and the README repeat.

#### Key Methods

- `Import(input, output, schemaPath, expectedSha256, splitThreshold, overwrite)`: checks the SHA-256 against the pin,
  refuses existing tables without `overwrite`, parses, derives (every relation must join a synset or sense of the file),
  chooses the split plan and writes the tables in sorted order to a staging folder next to the output, together with a
  copy of the schema that carries the new `file_split` line and, when the output has none, the empty `sense_label_map`.
  The replacement is one protected step: each existing target is moved to a backup folder, then the staged item is moved
  in. If any move fails, every move is undone (placed items removed, backups moved back) and the original exception is
  rethrown, so the tables, the schema and the id map are all either the new set or the old one. The staging and backup
  folders are deleted afterwards without hiding the original exception; the backup is kept only when the restore itself
  failed, and the error then names it. An internal `beforeMove` parameter lets a test make a chosen move fail.
  Returns an `ImportResult` with the counts.
- `ReadForms(input, expectedSha256)`: checks the SHA-256, reads the file and returns the cleaned `Form` spellings by
  entry identifier in file order; an entry without a `Form` is absent. The form generator uses it for the spellings
  that the source lists.
- `ImportPrevious(previous, data, release, expectedSha256)`: checks the SHA-256 of an earlier release, then writes
  `id_map` for it. A sense identifier that the data still has is unchanged and gets no row; a changed one maps by its
  sense key when exactly one current sense has that key. An entry identifier that the data still has gets no row;
  otherwise it maps to the entry of the majority of its mapped senses, or to the only entry with its lemma and part of
  speech. An identifier without a unique successor gets no row and is counted as removed (no candidate) or ambiguous (a
  tie). Two current senses with one sense key are an error. Returns an `IdMapResult` with the counts.
- `ImportCounts(input, data, expectedSha256)`: checks the SHA-256 of the counts file against the pin before reading it,
  reads the `sense` table of `data`, joins the counts on the sense key and writes `sense_frequency` in sorted order.
  Returns the number of senses with a count, with count 0 and the number of unmatched lines.
- `Import` also writes the empty `sense_label_map` table when it does not exist (an existing one holds published
  decisions and is never touched).

#### Error Handling

A wrong hash, malformed XML, no lexicon, an unknown relation type, a relation to a synset or sense that the file does
not hold, a satellite without head, a missing referenced synset, a previous release in which two current senses derive
the same key, or existing tables without `overwrite` throws before any existing file is changed: the import writes to a
staging folder and replaces the files in one step that is undone when a move fails, so with `overwrite` an input with a
defect, or a move that fails (a locked file), leaves the committed tables, the schema and the id map byte-identical (the
staging folder is removed in every case). `ImportCounts` throws in the
same way for a wrong hash, and for a line that does not have four fields or whose count is not a non-negative integer,
so no table is written from a damaged file.

#### Dependencies

`DataSchema`, `DataLayout`, `System.Xml`.

#### Callers

The `import-oewn` and `import-wn30-counts` commands.
