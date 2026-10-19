## WordData Subsystem Design

![WordData Structure](WordDataView.svg)

### Overview

The word data subsystem is the relational word data of the library: TSV tables under `data/` that hold the entries,
senses, synsets, glosses, examples, relations and pronunciations of English, imported once from Open English WordNet
2025 (OEWN, CC BY 4.0), and the sense inventory built on them: a readable label for every sense, the coarse groups of
the senses, the WordNet 3.0 frequency count of every sense, the senses of the project itself and the map from the
retired class ids. The subsystem also holds the build-time code that creates and checks the tables. It is data only; the
`compile-image` command of the builder compiles the tables into the word image that the core package embeds.

The subsystem contains seven units:

- **DataSchema**: loads `data/schema.yaml` (with its small YAML-subset reader) and rewrites its file split line.
- **DataLayout**: the file of a lemma (`FileFor`), the UTF-8 byte order, and the deterministic table writer.
- **OewnImporter**: the one-time import of the pinned OEWN file into the tables, and the import of the pinned
  WordNet 3.0 sense frequency counts.
- **DataValidator**: validates the committed tables against the schema, the sense labels, the groups and the legacy map.
- **FormGenerator**: generates the inflected-form table, keeping a generated row only when it is corroborated.
- **FormAuditor**: the seeded stratified audit of the generated rows (`audit-forms`), the regression gate.
- **SenseLabelGenerator**: gives every sense without a label a readable, deterministic label (`label-senses`).

The builder commands `import-oewn`, `import-wn30-counts`, `label-senses` and `validate-data` are the entry points.
Boundaries: the subsystem does not change the corpus or any package; the image is compiled from the tables by the
builder (see the WordSenseBuilder unit), not by the subsystem.

### Interfaces

- `data/schema.yaml`: the single source of truth of tables, columns, keys, vocabularies, relation types, features and
  the file split plan.
- `data/<table>/` and `data/<table>.tsv`: the tables. A table with a lemma column is divided among files named by the
  `FileFor` key (`data/entry/a.tsv`, `data/entry/sa.tsv`, `data/entry/_.tsv`); other tables are one file.
- `import-oewn --input FILE [--output DIR] [--sha256 HEX|none] [--threshold N] [--overwrite true]`,
  `import-wn30-counts --input FILE [--data DIR] [--sha256 HEX|none]`, `label-senses [--data DIR]` and
  `validate-data [--data DIR] [--baseline DIR]` (exit code 1 when a violation exists; the baseline is the data folder of
  the last release, against which the published labels are checked).
- The sense inventory tables: `sense_label` (the label of every sense), `sense_label_map` (renames and retirements of
  ids), `sense_group` and `group_sense` (the groups and their senses), `sense_frequency` (the WordNet 3.0 tag count of
  every sense key), `sense_respelling` (the respelling of a sense) and `legacy_class_map` (the retired class ids).
- `data/README.md` describes the tables for contributors and `data/PROVENANCE.md` records the sources, the pins, the
  WordNet 3.0 license notice for the counts and what was left out.

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

The sense inventory is a layer of tables over the imported senses. Every sense has one readable label
(`sense_label`), and its public id is `lemma#label`. The label is unique among the senses of its lemma, is frozen once
published and is never reused: a rename or a retirement is recorded in `sense_label_map`, so a caller that stored an
old id finds where it went. Curated labels and project labels are written by hand; `SenseLabelGenerator` writes a
label for every other sense and never changes one that exists.

Every sense belongs to exactly one coarse group (`sense_group`, `group_sense`). A group has one lemma, one part of
speech and a readable label, and does not contain two respellings, so a group answer is always pronounceable. The
partition rule holds for a curated lemma: every sense of it is in exactly one group, so a later change cannot leave a
sense without a group. A lemma that is not curated has one default group for each of its parts of speech, whose label
is the part of speech name followed by `-group` (`bank#noun-group`), so that the id of a default group never equals the
id of the only sense of that part of speech (`bank#noun`). The ids of
senses and of groups share one namespace, and the validator checks that they do not collide.

`sense_frequency` holds the WordNet 3.0 tag count of every sense key, from the pinned `index.sense` file joined on the
key, with the count 0 for a sense the file does not list and the source named (`wn30`). The counts rank the senses of
a lemma, most frequent first, and size the training data of each sense. A sense without a key has no row and ranks as
count 0.

Project senses are the senses that OEWN does not provide. They use the supplementary mechanism that the project's
own entries already use (the source `dema`: an entry, a synset and a sense, with an authored gloss) and have no sense
key. There is one for each determiner, pronoun, preposition, conjunction, interjection or auxiliary analysis
(`lemma#<part of speech name>`), plus the curated discourse senses (`well#discourse-marker`), and the entry for the
conjunction `or`. The candidate senses of a word come from its entries to their senses; there is no per-form sense
scope table.

`sense_respelling` records the respelling of the senses whose pronunciation differs (`lead` the metal and `lead` the
verb). `legacy_class_map` maps every class id of the retired class inventory to the group or sense that replaces it,
with
the level of that target (`group` or `sense`), so a stored old id and the relabeling of the corpus both have a complete,
validated target.

The importer and the validator share `DataSchema` and `DataLayout`, so they cannot disagree on shape, order or
placement.

### Error Handling

The importer throws `InvalidDataException` for a malformed, incomplete or inconsistent source (a count line that cannot
be read included) and for a hash that differs from the pin, and refuses to overwrite existing tables. The label
generator throws for an input table it cannot read and writes nothing. The validator never throws for bad data: it
reports each violation with the file, line, rule and value (at most 20 per rule are listed, with the total counted) and
the command exits with 1.

### Dependencies

The .NET base class library only. The subsystem is part of the builder project and is not shipped.

### Callers

The `import-oewn`, `import-wn30-counts`, `label-senses` and `validate-data` commands of the builder, the `compile-image`
command (which reads the validated tables), and the pull-request checks of the repository.
