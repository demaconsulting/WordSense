# WordSense word data

The tables in this folder are the word data of WordSense: entries, senses, synsets, glosses, examples, relations and
pronunciations of English. They were imported once from **Open English WordNet 2025** (<https://en-word.net>), which
is licensed under **CC BY 4.0** (<https://creativecommons.org/licenses/by/4.0/>), and keep that license; project-authored
additions (source `dema`) are MIT. The sense frequency counts (`sense_frequency`) come from **WordNet 3.0**
(Princeton University) and are used under its license (WordNet 3.0 Copyright 2006 by Princeton University; the full
notice is in [PROVENANCE.md](PROVENANCE.md)); they set the quota of training sentences of each sense, fix the labeling
order of the senses of a lemma (which sense gets the plain label and which gets `-2`) and, as a tempered prior,
correct the scores of the encoder models to the usage of the senses; the word image also carries them (a 16-bit count
for each sense) so that the Basic model can rank the senses of the lemmas no model scores by usage; they do not order
the senses in the word image, which follow the order of Open English WordNet. **SemCor** is an evaluation source only:
it is never committed. See
[PROVENANCE.md](PROVENANCE.md) for the source file, its hash and what was left out.

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
| `id_map` | `old_id, release` | identifier map of the previous release (OEWN 2024) |
| `form` | `spelling, rank` | one analysis of a written form: entry, features, source, reviewed flag |
| `sense_label` | `sense_id` | the readable label of a sense (public id `lemma#label`) and its origin |
| `sense_label_map` | `old_id, release` | renames and retirements of public sense ids (an old id is never reused) |
| `sense_group` | `group_id` | a curated set of 2+, but not all, confusable senses; id `lemma#group:label`; a kind |
| `group_sense` | `group_id, sense_id` | the group of a sense; a sense is in at most one group, and needs none |
| `sense_frequency` | `sense_key` | the WordNet 3.0 tag count of a sense key (0 when the sense has none) |

The part of speech letters are `n v a r` and, for the closed classes, `u` pronoun, `p` preposition, `c` conjunction,
`i` interjection, `d` determiner, `x` auxiliary. An entry of source `dema` may have no sense (a bare lemma and part of
speech that forms refer to); an entry of source `oewn` always has senses; an entry of a closed class has a project sense.
`form.spelling` is the exact written form; the table is partitioned by the spelling like entries by the lemma.

The class tables of the earlier coarse inventory (`coarse_class`, `class_sense`, `class_entry`) and the table of
per-form class scopes (`form_sense_scope`) are retired and no longer part of the data. The temporary record of their
mapping for the relabeling of our own corpus was deleted when the relabeling was done (see
[MAPPING.md](MAPPING.md)).

## The sense inventory

The library classifies single words, so the inventory covers the senses of the lemmas of one token (`entry.token_count`
is 1) and every project sense.

- **Labels.** Every sense has one readable label, unique among the senses of its lemma, made of lower-case ASCII
  letters, digits and hyphens; the public id of the sense is `lemma#label` (for example `bank#coin-bank`). A label is
  chosen by hand (origin `curated`), written by `label-senses` (origin `generated`: the part of speech name for the
  only sense of a lemma and part of speech, else the first synonym, else the head of the hypernym, with a numeric
  suffix where two senses would share one), or belongs to a project sense (origin `project`). A published label is
  never changed or reused: a rename or a retirement is a row of `sense_label_map`, and `validate-data --baseline DIR`
  compares the sense labels (and only them) with the data folder of the last release. A sense label contains no `:`, so
  it never contains the marker `group:` of a group id.
- **Project senses.** A sense that Open English WordNet does not provide: one for every closed-class entry
  (`lemma#<part of speech name>`, for example `the#determiner` and `or#conjunction`) and the curated discourse senses
  (`well#discourse-marker`). They are rows of source `dema`: an entry, a synset, a gloss and a sense without a sense key.
- **Levels and groups.** The levels nest: none, then the part of speech (a lemma and part of speech), then an optional
  group, then the sense. A **group** exists only when it is curated: a set of at least two, but not all, senses of one
  lemma and one part of speech that are closely related in meaning and that a reader can confuse. A group has no
  singleton and no default or whole-part-of-speech group; a sense needs no group, and a sense is in at most one. When
  the classifier is unsure between senses, it steps up to the next level that exists:
  the shared group of the two senses if both are in one, otherwise the part of speech. Almost every lemma has no group;
  the data has the groups of the curation only (see `corpus/curation`). The members of a group are the rows of
  `group_sense`; a group answer of the image always exposes its member sense ids, so that a caller can step down to
  the senses.
- **Group ids.** The id of a group is `lemma#group:slug` (for example `help#group:assistance`), where the slug is made of
  lower-case ASCII letters, digits and hyphens and is the label of the group (`sense_group.label`, unique in its
  lemma). The marker `group:` is not a label character, so a group id can never be the id of a sense (`help#assistance`
  is a sense, `help#group:assistance` is a group); the validator rejects a group id without the marker, with another
  lemma, with a slug that is not well formed and any collision with a sense id. Sense ids are frozen once published;
  **group ids are not**. They are versioned with the data release: a group can be added, renamed, merged or dropped in
  any release. A caller must treat a group id as an opaque, per-release value, must not store it across releases as if
  it were stable, and should store the sense ids (which it gets with every group answer) instead. Every change of a group
  is recorded in the release notes of the release that makes it (this repository has no separate changelog file); the
  review that made the groups of the current data is recorded in `corpus/curation/README.md`. `sense_label_map` is
  about sense ids only and never lists a group.
- **Group kind and admission.** Every group has a `kind`: `equivalent` (near-duplicate senses, which the source splits
  finely but which have effectively one meaning) or `related` (a genuine family of meanings that are close but not one
  meaning). A group is admitted only with the curator's same-meaning judgment **and** evidence: observed confusion (the
  blind judge or a classifier mixes the senses up) or near-identical glosses. The checkable parts are enforced: the kind
  is present and in the `group_kind` vocabulary, a group has at least two and not all senses of its part of speech, and
  the curation file `corpus/curation/groups.tsv` must carry non-empty `evidence` text for every group that is written
  (the evidence is curation metadata and not a column of the data tables). The curator's judgment itself is not
  machine-checkable.
- **Frequency.** `sense_frequency` has the WordNet 3.0 tag count of every sense that has a sense key, joined on the key
  by `import-wn30-counts`; 0 when the sense index has no line for it. A project sense has no key and counts as 0. The
  counts feed the usage prior and the tiers, and the image compiler writes them into the word image (section 38, outside
  the inventory hash); the order of the senses of a lemma in the image is the order of Open English
  WordNet (`sense.position`, then `entry.homograph_no`, project senses last).
- **Numerals.** The tables keep the 134 spellings of Open English WordNet that have no letter (`0`, `1000`, `24/7`,
  `.22`: 176 `form` rows, 176 entries and 178 senses) as the source has them; the image compiler leaves them out, because
  the library never looks up a token without a letter.

## Relations

A relation is stored in one direction only (for example `hypernym`; the `hyponym` rows are derived). A symmetric
relation (`antonym`, `similar`, ...) is stored once with the smaller identifier first. Synset and sense relations have
separate vocabularies in `schema.yaml`. A sense relation of type `other` needs a subtype.

## Commands

```text
dotnet run --project tools/WordSense.Builder -c Release -- validate-data [--data DIR] [--baseline DIR]
dotnet run --project tools/WordSense.Builder -c Release -- import-oewn --input english-wordnet-2025.xml.gz [--output DIR]
dotnet run --project tools/WordSense.Builder -c Release -- import-wn30-counts --input index.sense [--data DIR]
dotnet run --project tools/WordSense.Builder -c Release -- label-senses [--data DIR]
dotnet run --project tools/WordSense.Builder -c Release -- seed-inventory [--data DIR] [--curation DIR]
```

`validate-data` prints each violation with file, line, rule and value, and exits with 1 when there is one; with
`--baseline` (the data folder of the last release) it also checks that no published sense label was changed or reused
(group ids are not compared).
The OEWN source file is never committed; `import-oewn` checks its SHA-256 against the pin, and `import-wn30-counts` the
SHA-256 of `index.sense` (see [PROVENANCE.md](PROVENANCE.md)). `label-senses` and `seed-inventory` write the sense
inventory tables; run them after the counts are imported (the counts fix the labeling order of the senses of a lemma). `seed-inventory`
adds the project senses of the closed-class entries and imports the curated labels and groups of `corpus/curation`,
leaving out a group of one sense and a group of every sense of its lemma and part of speech, and refusing a group
without a valid kind or without evidence, and a curated sense whose part of speech is not the one of its sense in the
data (an adjective satellite is an adjective). A curated label replaces a generated one (which was never published) and
never a curated or project one. `label-senses` never creates a group.
