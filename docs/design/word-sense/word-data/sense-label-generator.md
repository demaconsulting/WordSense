### SenseLabelGenerator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`SenseLabelGenerator` gives every sense of a lemma of one token (`entry.token_count` 1) that has no label a readable,
deterministic label, so that every sense of every word has a public id `lemma#label` and callers can store it. It writes
the `sense_label` table; it never changes a label that already exists. The senses of a multi-word lemma are not
labeled, because the library classifies single words.

#### Data Model

Inputs, all read from `data/`:

- `entry`, `sense` and `synset`: the lemma and part of speech of each sense and the synset that groups its synonyms.
- `synset_relation`: the hypernym relation of a synset.
- `sense_label`: the labels that exist, curated, generated or project, with their origin.
- `sense_label_map`: the renames and retirements of earlier ids. The label part of every old id counts as used.
- `sense_frequency`: the tag count that orders the senses of a lemma, when the table is present.

Output: the complete `sense_label` table, the existing rows unchanged and one row for each sense of a one-token lemma
that had no label: origin `generated`, or `project` for a sense without a sense key (the generator never writes
`curated`). The generator never reads, creates, changes or removes a group: a group exists only when it is curated, and
its id (`lemma#group:slug`) lives in its own namespace, so a group does not take a label of the lemma.

Inventory order of the senses of a lemma: part of speech, then WordNet 3.0 tag count descending, then the UTF-8 bytes of
the sense id. The order depends on the data only.

The label of a sense without one is the first rule that gives a usable slug:

1. The only sense of a lemma and part of speech: the part of speech name (`noun`, `verb`, `adjective`, `adverb`,
   `pronoun`, `preposition`, `conjunction`, `interjection`, `determiner`, `auxiliary`), so the id is `lemma#noun`.
2. The first synonym: the lemma of the first other sense of the same synset, in the order of the UTF-8 bytes of the
   sense id, whose slug is not the slug of the lemma itself.
3. The hypernym head: the lemma of the first sense (in the order of the UTF-8 bytes of the sense id) of the first
   hypernym synset (in the order of the synset id) whose slug is not the slug of the lemma itself.
4. The part of speech name, when neither a synonym nor a hypernym gives a slug.

A slug is the lower-case, NFC-normalized text with the accents removed (the canonical decomposition without its
combining marks), every run of characters other than ASCII letters and digits turned
into one hyphen and leading and trailing hyphens removed, so a label contains only lower-case ASCII letters, digits and
hyphens. A candidate whose slug is empty is skipped.

Uniqueness: the labels of one lemma are unique across its parts of speech. A label already used by a sense of the lemma
or by an existing or retired id of the lemma is not taken (the label of a group is not: a group id carries the marker
`group:` and is in its own namespace).
When the slug of a sense is taken, the sense receives the
slug with the suffix `-2`, then `-3`, and so on, the first free number. Senses are labeled in inventory order, so the
sense that comes first keeps the plain slug.

#### Key Methods

- `Run(data, output)`: reads the inputs, computes the labels of the senses that have none and writes `sense_label`
  through `DataLayout` (sorted by the primary key, the existing rows byte for byte). Returns the counts: existing rows
  kept, rows generated, and the number of labels that needed a suffix or the part of speech fallback.
- `Generate(inputs)`: the pure computation, from the loaded tables to the new rows; it holds the rules above and
  has no file access.
- `Slug(text)`: the slug of a candidate, or an empty text when nothing usable remains.
- `Candidates(sense)`: the candidates of rule 2 to 4 in order.

Preconditions: the tables validate against the schema. Postconditions: every sense of a one-token lemma has exactly one
label; no row that
existed is changed or removed; the same inputs give the same bytes in the same order, whatever the order in which the
input rows were read.

#### Error Handling

A missing input table, an unreadable row, or two existing labels of one lemma that are equal throws
`InvalidDataException` with the file and the line, and nothing is written (the output is built in memory first).
A sense for which no rule gives a free label cannot occur, because the suffix always finds a free number. A label whose
sense no longer exists is kept: removing it is a data decision that goes through `sense_label_map`.

#### Dependencies

`DataSchema`, `DataLayout` and `DataTables` (to read and write the tables).

#### Callers

The `label-senses` command of the builder. `DataValidator` checks the result, and the `ImageCompiler` reads it.
