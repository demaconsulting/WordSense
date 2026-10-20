### DataValidator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataValidator` checks the committed tables so that hand edits keep the data consistent.

#### Data Model

`ValidationReport`: the violations listed (file, line, rule, message), the total number of violations, the row count
of every table and `IsValid`. Rule tags: schema, file-set, header, cell-count, format, cell, sort, empty-file,
placement, unique, foreign-key, relation, entry, synset, sense, example, label, stability, group. At most 20
violations are listed per rule; the total is still counted.

#### Key Methods

- `Validate(dir, baseline)`: loads the schema, checks the file set (a missing folder of a partitioned table counts as an
  empty table), then each file (format and cells), then the keys, foreign keys, sort order and placement, the relation
  conventions and the cross-table rules (satellite heads, an entry and a synset each have a sense and a gloss, sense
  positions 1..n, part-of-speech fit, token counts and head indexes, and examples naming senses of their own synset),
  and then the rules of the sense inventory. The baseline is the data folder of the last release (optional).
- Sense rules (`label`, `stability`, `sense`): a data folder with no label, group or id map yet (a fresh import) has no
  inventory, and the rules below are not applied to it. Every sense of a lemma of one token, and every sense without a
  sense key, has exactly one label; the labels of the senses of a
  lemma are unique and made of lower-case ASCII letters, digits and hyphens; no sense id equals the id of a group (a
  defensive check: the marker of a group id, below, is not a label character, so only a malformed label can collide); a
  label that was published is neither changed nor reused for another sense unless
  `sense_label_map` records the change; a project sense (label origin `project`) has no sense key and an authored gloss,
  and a sense without a sense key has the origin `project`. The published labels are
  those of the `sense_label` table of the baseline, which the check compares with the current table (without a baseline
  only the retired ids are checked); an id that `sense_label_map` retires must not appear as a current id. Only sense
  labels are compared: group ids are versioned per data release and are not frozen, so the baseline check never reads
  or compares a group, and `sense_label_map` is about senses only.
- Group rules (`group`, `unique`, `cell`): the id of a group is `lemma#group:label`: the id must carry the marker
  `group:` after the `#`, its lemma part must be the lemma of the group, and the label is the slug after the marker
  (lower-case ASCII letters, digits and single hyphens), unique in its lemma; a group has at least two senses but not all
  senses of its lemma and part of speech (the part of speech is the next level, so such a group is redundant); each group
  has one lemma, one part of speech and no two respellings; a sense is in at most one group; the origin of a group is
  `curated` and its kind is `equivalent` or `related` (both required and checked against the schema vocabularies by the
  cell rule). A sense needs no group, so there is no partition rule. The class and scope rules of the retired inventory
  are gone; an entry without a sense is accepted only for a closed-class word or when a carried analysis refers to it.
  The admission evidence of a group is curation metadata, checked by the curation import and not by the validator.

#### Error Handling

The validator reports instead of throwing: a schema that cannot load is one `schema` violation; an unreadable file is a
violation of that file; a baseline that cannot be read is a `stability` violation.

#### Dependencies

`DataSchema`, `DataLayout`.

#### Callers

The `validate-data` command and the test of the committed data.
