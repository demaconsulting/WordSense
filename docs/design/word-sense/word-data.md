## WordData Subsystem Design

![WordData Structure](WordDataView.svg)

### Overview

The word data subsystem is the relational word data of the library: TSV tables under `data/` that hold the entries,
senses, synsets, glosses, examples, relations and pronunciations of English, imported once from Open English WordNet
2025 (OEWN, CC BY 4.0), and the build-time code that creates and checks them. It is data only. No shipped package reads
it yet; a later change builds the embedded lexicon from it.

The subsystem contains six units:

- **DataSchema**: loads `data/schema.yaml` (with its small YAML-subset reader) and rewrites its file split line.
- **DataLayout**: the file of a lemma (`FileFor`), the UTF-8 byte order, and the deterministic table writer.
- **OewnImporter**: the one-time import of the pinned OEWN file into the tables.
- **DataValidator**: validates the committed tables against the schema.
- **FormGenerator**: generates the inflected-form table, keeping a generated row only when it is corroborated.
- **FormAuditor**: the seeded stratified audit of the generated rows (`audit-forms`), the regression gate.

The builder commands `import-oewn` and `validate-data` are the entry points. Boundaries: the subsystem does not change
the corpus, the lexicon blob or any package; the existing builder commands are untouched.

### Interfaces

- `data/schema.yaml`: the single source of truth of tables, columns, keys, vocabularies, relation types, features and
  the file split plan.
- `data/<table>/` and `data/<table>.tsv`: the tables. A table with a lemma column is divided among files named by the
  `FileFor` key (`data/entry/a.tsv`, `data/entry/sa.tsv`, `data/entry/_.tsv`); other tables are one file.
- `import-oewn --input FILE [--output DIR] [--sha256 HEX|none] [--threshold N] [--overwrite true]` and
  `validate-data [--data DIR]` (exit code 1 when a violation exists).
- `data/README.md` describes the tables for contributors and `data/PROVENANCE.md` records the source, the pin and what
  was left out.

### Design

The format is deterministic by construction: UTF-8 without byte-order mark, LF line ends, a header line, no quoting,
NFC text, tab and line break forbidden in cells, and rows sorted by the UTF-8 bytes of the primary key (integer columns
numerically, so the order matches a reader that compares bytes). A table with a lemma column is placed by `FileFor`:
the lower-cased ASCII first letter of the NFC lemma, or the first two letters for the letters of the split plan, or `_`
when the lemma does not start with an ASCII letter. The split plan is chosen by the importer (a letter is split when its
rows exceed the threshold) and recorded in the schema, so the validator checks placement against the data rather than
inferring it. Rows that belong to a synset or sense follow the lemma of their entry, so one lemma's rows are in the
same file of each table.

Relations are stored in one direction only; the inverse is derived. Symmetric relations are stored once with the
smaller identifier first. The schema declares the inverse pairs and which direction is stored, and the synset and
sense levels have separate vocabularies, because `also`, `exemplifies` and `similar` exist at both levels.

Coarse classes (the stage that maps the project's own sense labels to OEWN senses) use the partition rule: when a lemma
and part of speech has any class of kind `sense_group`, `no_oewn` or `unmapped`, every sense of it is in exactly one
class, so a later change cannot leave a sense without a meaning.

The importer and the validator share `DataSchema` and `DataLayout`, so they cannot disagree on shape, order or
placement.

### Error Handling

The importer throws `InvalidDataException` for a malformed, incomplete or inconsistent source and for a hash that
differs from the pin, and refuses to overwrite existing tables. The validator never throws for bad data: it reports
each violation with the file, line, rule and value (at most 20 per rule are listed, with the total counted) and the
command exits with 1.

### Dependencies

The .NET base class library only. The subsystem is part of the builder project and is not shipped.

### Callers

The `import-oewn` and `validate-data` commands of the builder, and the pull-request checks of the repository.
