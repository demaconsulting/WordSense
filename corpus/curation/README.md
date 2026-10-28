# Curated sense inventory (input of `seed-inventory`)

`senses.tsv` and `groups.tsv` are the reviewed curation of 1,157 lemmas (307 curated groups). The import into `data/` keeps
184 groups (see "Groups").

- the first set, 20 lemmas (297 senses, 179 curated groups, 56 of them kept by the import): `back`, `even`, `first`,
  `forward`, `go`, `help`, `home`, `however`, `just`, `little`, `long`, `only`, `own`, `please`, `pretty`, `review`,
  `right`, `still`, `test` and `well`;
- the second set, 738 lemmas (6,283 senses), the lemmas of the legacy corpus: 6,580 senses in all;
- the 12 technical lemmas (176 senses and 6 project senses), see "Technical lemmas";
- the stage 1 set, 387 lemmas (3,836 senses, 58 of them project senses): the 371 most used polysemous lemmas that no
  anchor trains, and the 16 heteronym lemmas of sense level that are not among them (see "Stage 1 lemmas").

The ids of senses are public ids (`lemma#label`). The columns `tier`, `tag_count` and the others of `senses.tsv` are the
per-sense data of the curation (the tier sizes the training data of the sense). `group_id` and `group_reason` record the
curator's group of the sense (empty when the sense is in no group, see "Groups"). `flag` is the curator's remark (`D`
near-duplicate of another sense, `R` rare). `seed-inventory` reads `sense_id`, `oewn_sense_id`, `part_of_speech`,
`gloss` and `source` of `senses.tsv`, and `group_id`, `lemma`, `part_of_speech`, `member_sense_ids`, `kind` and
`evidence` of `groups.tsv`.

`dotnet run --project tools/WordSense.Builder -c Release -- seed-inventory` writes them into `data/` as the tables
`sense_label` (origin `curated`, or `project` for the senses that Open English WordNet does not have) and `sense_group`
and `group_sense` (origin `curated`). A curated label replaces a label that `label-senses` generated and is never
changed itself. The run is idempotent: a second run changes nothing.

## Labels of the second set

The curated labels of the 738 lemmas come from a pass of `gpt-5.6-luna` over the generated labels (4,075 of the 6,283
labels changed), followed by a review sweep. A second `gpt-5.6-luna` pass flagged 63 labels that misdescribe their
gloss, and 34 of them were changed by hand (the other flags were placeholder glosses such as `noun`, where the label
cannot be judged). The `flag` column holds the `D` and `R` remarks of the pass. `missing-usages.tsv` lists 33 lemmas
whose common usage (a phrasal verb, a discourse use) Open English WordNet lacks; each is only a candidate for a project
sense.

The second set has 84 groups (77 with every member, 7 with a subset), all `equivalent` with the evidence
`curated:gloss near-identical glosses`. A `gpt-5.6-luna` pass judged the full glosses of the 120 proposed groups. The
other 36 are dropped, including two that the pass accepted but whose glosses differ in more than wording. Their
`group_id` in `senses.tsv` is empty. They can return when the classifier's confusion data shows that their senses are
mixed up.

## Glosses of project senses

A project sense is a sense that Open English WordNet does not have (source `dema`, synset id `dema-syn-...`). The
glosses of the 937 project senses that the seeder created are one-line definitions that `gpt-6.1-sol` wrote from the
lemma, the part of speech, the role of the sense (for example a closed class), the other senses of the lemma and one or
two example sentences. `project-glosses.tsv` records `sense_id`, part of speech, old gloss and new gloss. The glosses
are stored in the committed table `data/gloss` (the rows of the `dema-syn-*` synsets only; the glosses of Open English
WordNet synsets stay verbatim) and, for the 135 project senses that this curation lists, in the `gloss` column of
`senses.tsv`, which `seed-inventory` writes into `data/`. No placeholder gloss remains.

## Technical lemmas

Technical instructions (aircraft and machine maintenance, written in plain "STE-style" English: "Mount the bracket to the
frame.", "Torque the bolts to 25 N m.") use technical senses. The curation covers them in three ways:

- **Curated lemmas.** `mount`, `bearing`, `bleed`, `torque`, `run`, `position`, `support`, `install`, `remove`,
  `attach`, `inspect` and `clean` (176 senses of the data) are in `senses.tsv`, added with
  `tools/quota-fill/curate_new.py`: the Open English WordNet sense, its gloss and tag count (the tier follows the tag
  count: 5 or more C, 1 to 4 U, none R), and a public label that a `gpt-5.6-luna` pass reviewed (for example
  `mount#gem-setting` and `bearing#rotating-support`).
- **Project senses of a word the dictionary has** (source `project`, no key). `seed-inventory` adds a sense of the
  existing entry (the sense id `dema-<lemma>-<pos>-1`, the synset `dema-syn-<lemma>-<pos>-1`) when the curation lists a
  project sense whose entry has no keyless sense yet: `mount#mounting-fixture` (a bracket or support that holds
  equipment), `bleed#vent` (release trapped air or fluid from a system), `arm#prime-system` (to arm an alarm or a fire
  suppression system), `lead#electrical-lead` (a conductor with a terminal), `flush#fluid-flush` (a cleaning operation
  that passes liquid through a system) and `tap#tapping-point` (a connection point in a line or circuit).
- **Analyses without a sense.** The verb `torque` (`torque`, `torques`, `torqued`, `torquing`; in
  `corpus/lexicon/additions.tsv`) is a surface-only analysis. The adjective analysis of the participial adjectives that
  technical text uses (`damaged`, `drained`, `drilled`, `alloyed`; in `corpus/lexicon/own-adjectives.tsv`) sits beside
  the reviewed verb analysis (`FormGenerator`, requirement `WordSense-WordData-FormGenerator-OwnAdjectives`). 114 such
  adjective entries exist; the other 110 (`based`, `covered`, `changed`, `driven`, ...) are left out on purpose (see the
  method of the quota filling in `corpus/README.md`).

`tech-senses.tsv` lists the 62 senses that technical text uses (picked by `gpt-6.1-sol` from the senses of 19 technical
lemmas, with the note of their technical use) and the number of **extra anchors** for each (14 for a core meaning, 8 for
a minor one): real FAA text first, then written technical sentences. The quota filling reads this file (`--tech`).

**Known gaps** (found by `gpt-6.1-sol` in a review of the FAA sentences that the labeler could not place): the noun
`bleed` (bleed air), the verb `torque` as a sense, the noun `lock` as a mechanism, `arm` as a moment arm, a ground roll
and `clean` as aerodynamically clean.

## Stage 1 lemmas

The stage 1 set is the encoder expansion of the most used polysemous lemmas. A polysemous lemma is a lemma with two or
more labeled senses; the usage of a lemma is the sum of the WordNet 3.0 tag counts of its senses. The set is the 500 most
used polysemous lemmas that have only letters and are lower case (a numeral, and a capitalized lemma such as
`American`, which a lower-case harvest never finds, are replaced by the next lemmas in the ranking), minus the lemmas that
an anchor trains already, plus the 16 sense-level heteronym lemmas (`heteronyms.tsv`) that are not in it. Their rows were
added with `tools/quota-fill/curate_new.py apply --lemmas FILE`: the Open English WordNet sense, its gloss and its tag
count (the tier follows the tag count), the label that the data holds (a generated label, kept as it is), and the project
senses of the data (a sense without a sense key, tier U). After `seed-inventory` these labels are curated and never
change.

## Shape fit (`shape-fit.tsv`)

`shape-fit.tsv` (`sense_id`, `shapes`) lists, for each noun and verb sense of the lemmas in scope, the sentence shapes of
its own part of speech in which the sense is used naturally (comma-separated; empty when none). A judge model fills it
from the glosses (`tools/quota-fill/shapes.py fit-export` and `fit-apply`). It decides where the writers add anchors
(`tools/quota-fill/README.md`, "Shape coverage"); the builder does not read it.

## Part of speech of a curated sense

The `part_of_speech` of a curated sense must be the part of speech of its sense in `data/` (the part of speech of its
Open English WordNet entry; an adjective satellite is an adjective). `seed-inventory` refuses the curation otherwise.
`right#agreement-yes` is an adverb, as Open English WordNet files the sense (`oewn-00151409-r`) under the adverbs and as
the corpus rows have it. `back#animal-rear` is an adjective on purpose: it is the adjective satellite `oewn-00132764-s`
(`back%5:00:00:posterior:00`, "located at or near the back of an animal", as in the back legs of a horse); the noun use
of a horse's back is `back#body-trunk`.

## Groups

A **group exists only when it is needed**: a set of at least two, but not all, senses of one lemma and one part of
speech that are closely related in meaning and confusable. The curated name of a group in `groups.tsv` is
`lemma#slug`. `seed-inventory` writes it as the group id `lemma#group:slug` (for example `help#assistance` becomes
`help#group:assistance`). The marker `group:` keeps the id of a group apart from the ids of senses, so there is no
collision and no suffix. Group ids are versioned per data release and are not frozen (see `data/README.md`).

**Admission rule.** A group needs the curator's same-meaning judgment **and** evidence: observed confusion (the blind
judge or a classifier mixes the senses up) or near-identical glosses. `groups.tsv` records both:

- `kind` is `equivalent` (near-duplicate senses) or `related` (a genuine family of meanings). Where there is no judge
  data, the kind is `equivalent` when every non-head member (the member with the highest WordNet tag count is the head)
  is flagged `D` or the glosses are near-identical, otherwise `related`. For a group named after one of its members,
  the kind follows the review of that group (a family of meanings is `related`, a head with near-duplicates is
  `equivalent`).
- `evidence` is a short text that the importer requires to be non-empty for every group it writes: `judge: C/N in-group
  confusion, A in-group alternates` (the blind judge on N sentences of the group confused its senses C times and named
  another member of the group as the alternative A times when it was unsure), `curated:flag ...` (the `D` flags of the
  curation), or `curated:gloss ...` (the glosses are near-identical, or the meanings are related as the curator judged
  from the glosses).

The importer applies these rules to the curated groups, in this order:

- A curated group that mixes two parts of speech is split by part of speech (three of the groups).
- A group, or a part of a split group, with **one sense** is left out (122 of the parts): a sense needs no group.
- A group with **every sense of its lemma and part of speech** is left out (4 parts): the part of speech is the next
  level, so such a group says nothing more.
- A group that remains needs a `kind` of the `group_kind` vocabulary of `data/schema.yaml`, non-empty `evidence` and a
  slug of lower-case ASCII letters, digits and hyphens; otherwise the import stops and writes nothing.

The result is 184 groups of 397 senses: 154 `equivalent` and 30 `related`. The first set has 56 groups of 138 senses
(26 `equivalent` and 30 `related`), the second set 84 `equivalent` groups of 169 senses and the stage 1 set 44
`equivalent` groups of 90 senses. The importer replaces the
groups of the lemmas of the curation as a whole and removes any group row whose origin is not `curated`. After the
import the tables are edited in `data/`, and these files only record what was imported.

**Groups of the stage 1 set.** `gpt-6.1-sol` proposed groups from the full glosses for every lemma and part of speech of
the set with four or more senses (363 units, 3,232 senses; `tools/quota-fill/prompts/group-proposer.md`): 190 groups.
`claude-sonnet-5.5` reviewed them blind (`group-reviewer.md`: it saw the glosses of the unit and the proposed groups,
nothing else, and 20 control groups of two senses with no word in common, which it rejected). It accepted 183 and
rejected 7. Only the 44 groups that the reviewer judged `equivalent` are kept: the 139 `related` ones were accepted
too readily to change answers. The evidence is `curated:gloss near-identical glosses`; no judge data exists for the
stage 1 lemmas yet, so these groups rest on the glosses and are candidates to drop or to extend when a confusion pass of
trained encoders is available.

**Decisions on the first set.** `test#group:trial` is `related` (18/65 in-group confusion). `only#group:merely` is
`related`, and the `D` flag of `only#exclusively` is cleared, because it is a major adverb reading (226 WordNet tags)
and not a near-duplicate. `first#group:first-in-order` is `equivalent` (both members are `D`). `still#group:distillation`
is `related`.

**Known gaps of the evidence.** Six `related` groups named after a member have no observed in-group confusion
(`home#group:domestic`, `home#group:homeland`, `review#group:look-again`, `still#group:distillation`,
`test#group:examine-knowledge` and `test#group:try-out`), and two of them (`home#group:domestic` and
`test#group:try-out`) have no in-group alternate either. They stay on the curator's judgment of related meanings. The
groups without judge data (the other 40) rest on the curation flags and glosses only. They are candidates to drop if a
larger judge run still shows no confusion.

## Heteronyms (`heteronyms.tsv`)

`heteronyms.tsv` is the curated input of the heteronym table (`WordSense.Builder heteronyms` and the section "Keys and
Heteronyms" of the appendix). It names spellings whose analyses or senses stand for different words, so that a
downstream library, which attaches its own data to the keys of the answers, knows where the key, not the spelling,
decides. It carries **no pronunciation**: a class is a meaning, named in English (`fish` and `music` for `bass`), and
the rows are patterns of the analyses and senses that belong to the class.

| Column | Meaning |
| --- | --- |
| `level` | `sense`, `tense` or `lemma` (see below) |
| `group` | the group of the classes (the lemma, or the spelling for different lemmas) |
| `class` | the meaning class, named by its meaning; a group has two or more |
| `pattern` | `lemma/Part[/Features][#label,label]` (see below) |
| `note` | optional (rows have four or five cells): the note of the group (see below) |

The levels: `sense` lists the lemmas whose senses of one analysis sound different, `tense` the verbs whose past and
present sound different, and `lemma` the spellings of different lemmas and the pairs that the data cannot show. A
pattern names the lemma and part of speech of an analysis, the feature items it must contain and the labels of the senses
of the class (all senses of the analysis when there is no `#`). A `note` on any row of a group is the note of the group
(a group has at most one), shown in the appendix and written to the TSV export.

Rows are sorted by `level`, `group`, `class` and `pattern`, tab separated, one header row. The builder checks the table
against the sense catalog (every label is a sense of the lemma and part of speech), rejects a key that two classes claim
and a group that matches no spelling, and a test checks that every lemma whose entries the pronunciations split into
sound classes is in the `sense` rows, except the lemmas that the review below rejected.

**Counts.** The table has 293 rows (186 `lemma`, 94 `sense` and 13 `tense`) in 117 groups. The builder turns them, with
the heteronyms that it derives from the pronunciations, into 370 heteronym records (a record is a spelling of a group)
for 367 distinct spellings: `wound`, `leading` and `lowering` are in two groups each. The appendix and the TSV export
state the same numbers.

**Review decisions.** A blind lexicographer reviewed every group against its senses (sound, stress and the meaning
that separates them). The table follows these decisions:

- *Split by sense.* `aged`, `blessed` and `learned` do not put the whole adjective into one class: `aged#elderly`
  against `aged#of-age`, `learned#erudite` against `learned#conditioned`, `blessed#blest` against the holy and
  emphatic senses. `close` maps its noun by the senses that exist (the ending and completion senses go with the verb).
  `incense` splits its verb by sense (perfuming with the noun, angering alone). `lead` puts the printing strip with the
  metal and restricts the guide verb to its senses. `recreate` puts `revitalize` with `play`. `row` restricts the verb
  to the rowing sense. `tower` puts the sense of rising above with the structure.
- *Listed.* The noun `read` (with the non-past `read`), the auxiliary `do` (with the party and lexical verb class), the
  participle `leading` (the verb `lead` with the guidance class) and the participles of `lower` in the `lowering` group.
  `axes` is the plural and the third person of `axe` (the group has only `ax`, whose senses are those of `axe`).
  `graduate` as an adjective (`a graduate student`, the sense `graduate#postgraduate`) and `separate` as a noun and
  `separates` as its plural (the garment `separate-garment` and the `offprint`) are read like each other (one class
  `adjective-noun`, as in `alternate`), and the verb the other way. The analyses come from
  `corpus/lexicon/additions.tsv` (evidence `oewn`) and the flag `open` of two rows of
  `corpus/lexicon/form-frozen.tsv`; see the design of the form generator.
- *Not listed* because they are not heteronyms in standard English or cannot be told apart by the keys: `birr`,
  `recap`, `bourgeois`, `denier` (a difference in British English only), `whoop` (the /h/ and /w/ variants do not
  follow the senses), and `emu`, `fleer`, `laver`, `punt`, `router` and `slough` (too unreliable by accent). The test
  of the sense rows keeps a list of the ten that were `sense` groups (`birr`, `denier`, `emu`, `fleer`, `laver`,
  `punt`, `recap`, `router`, `slough` and `whoop`): a lemma of the list is exempt from the check that the
  pronunciations of the data are covered, and must not be in the `sense` rows.
- *Listed with a note.* The stress-shift pairs `compound`, `conflict`, `contrast`, `escort`, `export`, `impact`,
  `import`, `survey`, `torment`, `transfer`, `transport`, `update`, `compact`, `decrease`, `defect`, `discard`,
  `increase`, `recall`, `discount`, `intern`, `protest`, `rebound`, `alternate`, `lower` and `lowering`. The usual
  contrast is the reason a consumer needs the key, and the `note` says that some dialects merge the two classes (the
  verb takes the stress of the noun, in American English especially).
- *Not possible with this data.* A pattern may name only an analysis of the word table and a sense of the catalog.
  `desert` as an adjective (`desert island`) has no adjective entry in Open English WordNet (the noun modifies the
  noun). `reread` as a noun has no entry or sense; the dictionary has only the verb. The `wind` verbs that mean make
  breathless or burp, the metalworking verb `lead` and the enclosure and street nouns of `close` have no sense in the
  catalog. Each would need a new entry or sense, with corpus anchors for the trained lemmas and a retraining of the
  encoder weights.

**Provenance.** The `sense` rows were derived from the data: the entries of `data/entry` that share a lemma, with the
Open English WordNet pronunciations of `data/pronunciation` (compared after normalizing the notation and the common
dialect differences; a stress difference counts), split the senses of `data/sense` into sound classes, each named by
its meaning (for example `fish`, `music`, `bend` and `weapon`). The `tense` rows list the verbs whose past and present
are spelled alike and sound different (`read` and its prefixed verbs). The `lemma` rows were written by
hand: the spellings of different lemmas that the pronunciations of a lemma cannot compare (`wound` as the injury and as
the past of `wind`, `dove`, `lives`, `does`, `bases`) and the noun and verb pairs with a stress or voicing difference
whose entries have no pronunciation in the data (`use`, `object`, `present`, `project`, ...), taken from an independent
inventory of the same English heteronyms. They are not verified against the pronunciations of the data, which are
absent for them. The table is a committed input: edit it by hand and run the builder to check it.
