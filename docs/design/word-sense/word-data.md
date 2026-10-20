## WordData Subsystem Design

![WordData Structure](WordDataView.svg)

### Overview

The word data subsystem is the relational word data of the library: TSV tables under `data/` that hold the entries,
senses, synsets, glosses, examples, relations and pronunciations of English, imported once from Open English WordNet
2025 (OEWN, CC BY 4.0), and the sense inventory built on them: a readable label for every sense, the optional curated
groups of closely related, confusable senses, the WordNet 3.0 frequency count of every sense and the senses of the
project itself. The subsystem also
holds the build-time code that creates and checks the tables. It is data only; the
`compile-image` command of the builder compiles the tables into the word image that the core package embeds.

The subsystem contains seven units:

- **DataSchema**: loads `data/schema.yaml` (with its small YAML-subset reader) and rewrites its file split line.
- **DataLayout**: the file of a lemma (`FileFor`), the UTF-8 byte order, and the deterministic table writer.
- **OewnImporter**: the one-time import of the pinned OEWN file into the tables, and the import of the pinned
  WordNet 3.0 sense frequency counts.
- **DataValidator**: validates the committed tables against the schema, the sense labels and the groups.
- **FormGenerator**: generates the inflected-form table, keeping a generated row only when it is corroborated.
- **FormAuditor**: the seeded stratified audit of the generated rows (`audit-forms`), the regression gate.
- **SenseLabelGenerator**: gives every sense without a label a readable, deterministic label (`label-senses`).

The builder commands `import-oewn`, `import-wn30-counts`, `label-senses` and `validate-data` are the entry points; the
bootstrap command `seed-inventory` adds the project senses of the closed-class entries and imports the curated labels
and groups of the curation folder (`corpus/curation`), once (it is idempotent and never changes a label). The class
tables of the earlier coarse inventory (`coarse_class`, `class_sense`, `class_entry`) and the per-form class scopes
(`form_sense_scope`) are retired and are no longer part of the subsystem; the committed working file
`corpus/migration/legacy-class-map.tsv` keeps their mapping for the relabeling of the corpus (see the data section
below).
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
  the last release, against which the published sense labels are checked; group ids are not compared).
- The sense inventory tables: `sense_label` (the label of every sense), `sense_label_map` (renames and retirements of
  sense ids), `sense_group` (the optional curated groups: id `lemma#group:slug`, lemma, part of speech, label, origin and
  kind) and `group_sense` (the senses of each group), `sense_frequency` (the WordNet 3.0 tag count of every sense key) and
  `sense_respelling` (the respelling of a sense).
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

The sense inventory is a layer of tables over the imported senses. The library classifies single words, so the inventory
covers the senses of the lemmas of one token (`entry.token_count` 1); the senses of a multi-word lemma have no label or
group. Every such sense has one readable label
(`sense_label`), and its public id is `lemma#label`. The label is unique among the senses of its lemma, is frozen once
published and is never reused: a rename or a retirement is recorded in `sense_label_map`, so a caller that stored an
old id finds where it went. Curated labels and project labels are written by hand; `SenseLabelGenerator` writes a
label for every other sense and never changes one that exists.

The levels of an answer nest: none, then the part of speech (a lemma and part of speech), then an optional group, then
the sense. A **group** (`sense_group`, `group_sense`) exists only when it is curated: a set of at least two, but not
all, senses of one lemma and one part of speech that are closely related in meaning and that a reader can confuse. A
group has one lemma, one part of speech, a readable label and a kind, and does not contain two respellings, so a group
answer is always pronounceable. There is no singleton group and no default group or group of a whole part of speech,
because a group that holds one sense or all of them says nothing that the sense or the part of speech does not say; a
sense needs no group and is in at most one. When the classifier is unsure between senses it steps up to the next level
that exists: the shared group of the two senses if both are in one, otherwise the part of speech. The membership of a
group is the table `group_sense`, and it is what the image exposes: a group answer always carries the ids of its member
senses, so a caller can step down to the senses.

The id of a group is `lemma#group:slug`, where the slug is the label of the group (lower-case ASCII letters, digits and
hyphens, unique in its lemma). The marker `group:` is not a label character, so a group id lives in its own namespace
and can never be the id of a sense; the validator rejects a group id without the marker, with another lemma, with a
slug that is not well formed, and (as a defensive check) any collision with a sense id. The two kinds of id differ in
stability. **Sense ids are frozen** once published (the rule above). **Group ids are versioned per data release and
are not frozen:** a group can be added, renamed, merged or dropped in any release, so a caller must treat a group id as
an opaque, per-release value and keep the sense ids instead. The baseline check of `validate-data --baseline`, the label
generator and `sense_label_map` are therefore about senses only and never compare or list a group. Every change of a
group is recorded in the release notes of the release that makes it (the repository has no separate changelog file);
the review of the groups of the current data is recorded in `corpus/curation/README.md`.

Each group has a **kind**: `equivalent` (near-duplicate senses that the source splits finely but that have effectively
one meaning) or `related` (a genuine family of meanings that are close but not one meaning); the kind is required and
validated against the `group_kind` vocabulary of the schema, so that a consumer can make a truthful claim about what
the group means. A group is **admitted** only with the curator's same-meaning judgment and evidence: observed confusion
(the blind judge or a classifier mixes the senses up) or near-identical glosses. What is checkable is enforced: the
validator checks the kind, the size of the group and its respellings, and the curation import requires a valid kind and
non-empty `evidence` text for every group it writes (the evidence is curation metadata in `corpus/curation/groups.tsv`,
not a data column); the judgment itself is a review decision.

`sense_frequency` holds the WordNet 3.0 tag count of every sense key, from the pinned `index.sense` file joined on the
key, with the count 0 for a sense the file does not list and the source named (`wn30`). The counts rank the senses of
a lemma, most frequent first, and size the training data of each sense. A sense without a key has no row and ranks as
count 0.

Project senses are the senses that OEWN does not provide. They use the supplementary mechanism that the project's
own entries already use (the source `dema`: an entry, a synset and a sense, with an authored gloss) and have no sense
key. There is one for each determiner, pronoun, preposition, conjunction, interjection or auxiliary analysis
(`lemma#<part of speech name>`), plus the curated discourse senses (`well#discourse-marker`), and the entry for the
conjunction `or` (`or#conjunction`; the spelling `or` has it as its first analysis). The candidate senses of a word come
from its entries to their senses.

`sense_respelling` records the respelling of the senses whose pronunciation differs (`lead` the metal and `lead` the
verb).

The class tables of the retired coarse inventory and the per-form class scopes are gone from the schema and the data.
A map from the retired class ids to the new groups and senses is not part of the data: it is a temporary working file
of the corpus relabeling (`corpus/migration/legacy-class-map.tsv`, committed as a record; the command that wrote it was
removed with the class tables) that is deleted when the relabeling is done, and it is neither validated by
`validate-data`, nor shipped, nor documented as a feature.

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
