# WordSense word data

The tables in this folder are the word data of WordSense: entries, senses, synsets, glosses, examples, relations and
pronunciations of English. They were imported once from **Open English WordNet 2025** (<https://en-word.net>), which
is licensed under **CC BY 4.0** (<https://creativecommons.org/licenses/by/4.0/>), and keep that license; project-authored
additions (source `dema`) are MIT. See [PROVENANCE.md](PROVENANCE.md) for the source file, its hash and what was left out.

The data is **authoritative once committed**: edit the tables by hand (or with a script) and run the validator. The
importer refuses to overwrite existing tables, so a re-import can never destroy an edit.

## Rules for every table

- Plain TSV: one header line, one row per line, tab between cells, no quoting. A tab or line break cannot appear in a
  cell.
- UTF-8 without byte-order mark, LF line ends, final line feed, Unicode NFC text, no leading or trailing space in a
  cell.
- Rows sorted by the primary key (the order of the key columns in `schema.yaml`), strings by their UTF-8 bytes, integer
  columns numerically. An empty cell (NULL) sorts first and is allowed only for a nullable column (`?` in the schema).
- A table with a lemma is split into files named by the first letter of the lemma, or by two letters for the letters
  listed in `file_split` of the schema (`entry/a.tsv`, `entry/sa.tsv`). A lemma that does not start with an ASCII letter
  is in `_.tsv`. Rows that belong to a synset or a sense are in the file of the lemma of their entry.

## Tables

| Table | Key | Content |
| --- | --- | --- |
| `source` | `source_id` | origin of rows: `oewn`, and `dema` for project-authored rows |
| `entry` | `entry_id` | lemma, part of speech (`n v a r`), homograph number (0 if unique), token count, head token |
| `synset` | `synset_id` | part of speech (`s` is a satellite), head synset of a satellite, lexicographer file |
| `gloss` | `synset_id, seq` | definition text |
| `example` | `synset_id, seq` | example phrase |
| `sense` | `sense_id` | an entry in a synset, with position and sense key |
| `synset_relation` | `from_synset, rel, to_synset` | relation between synsets, one stored direction |
| `sense_relation` | `from_sense, rel, rel_subtype, to_sense` | relation between senses, one stored direction |
| `pronunciation` | `entry_id, seq` | IPA given by the source, a hint |
| `id_map` | `old_id, release` | identifier map of a later release (empty) |
| `coarse_class` | `class_id` | class of the coarse sense inventory of the shipped models |
| `class_sense` | `class_id, sense_id` | the senses of a class; a sense is in at most one class |
| `class_entry` | `class_id, entry_id, features` | the entry (and grammatical features) a class stands for |
| `form` | `spelling, rank` | one analysis of a written form: entry, features, source, reviewed flag |
| `form_sense_scope` | `spelling, rank, class_id` | the classes an analysis stands for (no row: one answer) |

The part of speech letters are `n v a r` and, for the closed classes, `u` pronoun, `p` preposition, `c` conjunction,
`i` interjection, `d` determiner, `x` auxiliary. An entry of source `dema` may have no sense (a bare lemma and part of
speech that forms refer to); an entry of source `oewn` always has senses. `form.spelling` is the exact written form;
the table is partitioned by the spelling like entries by the lemma.

The coarse tables map the labeled senses of `corpus/senses` to OEWN senses. When a lemma and part of speech has a class
of kind `sense_group`, `no_oewn` or `unmapped`, every sense of it is in exactly one class, so no sense is left without
a meaning.

## Relations

A relation is stored in one direction only (for example `hypernym`; the `hyponym` rows are derived). A symmetric
relation (`antonym`, `similar`, ...) is stored once with the smaller identifier first. Synset and sense relations have
separate vocabularies in `schema.yaml`. A sense relation of type `other` needs a subtype.

## Commands

```text
dotnet run --project tools/WordSense.Builder -c Release -- validate-data [--data DIR]
dotnet run --project tools/WordSense.Builder -c Release -- import-oewn --input english-wordnet-2025.xml.gz [--output DIR]
```

`validate-data` prints each violation with file, line, rule and value, and exits with 1 when there is one. The OEWN
source file is never committed; `import-oewn` checks its SHA-256 against the pin.
