# Curated sense inventory (input of `seed-inventory`)

`senses.tsv` and `groups.tsv` are the reviewed curation of 758 lemmas: the first batch (20 lemmas, 297 senses, 179
groups: `back`, `even`, `first`, `forward`, `go`, `help`, `home`, `however`, `just`, `little`, `long`, `only`, `own`,
`please`, `pretty`, `review`, `right`, `still`, `test` and `well`) and the second batch (738 lemmas, 6,283 senses, the
lemmas of the legacy corpus, see "Second batch" below), 6,580 senses in all, and the 12 technical lemmas of wave 3 (176
senses and 6 project senses, see "Technical lemmas (wave 3)" below). The ids of senses are public ids
(`lemma#label`). The columns
`tier`, `tag_count` and the others of `senses.tsv` are the per-sense data of the curation (the tier sizes the training
data of the sense); `group_id` and `group_reason` of a sense record the curator's group of the sense (empty when the
sense is in no group any more, see "Group review" below) and `flag` is the curator's remark (`D` near-duplicate of
another sense, `R` rare). `seed-inventory` reads `sense_id`, `oewn_sense_id`, `part_of_speech`, `gloss` and `source`
of `senses.tsv`, and `group_id`, `lemma`, `part_of_speech`, `member_sense_ids`, `kind` and `evidence` of `groups.tsv`.

`dotnet run --project tools/WordSense.Builder -c Release -- seed-inventory` writes them into `data/` as the tables
`sense_label` (origin `curated`, or `project` for the senses that Open English WordNet does not have) and `sense_group`
and `group_sense` (origin `curated`). A label that `label-senses` generated is replaced by the curated one (a generated
label was never published); a curated or project label is never changed. The run is idempotent: a second run changes
nothing.

## Second batch (738 lemmas)

The curated labels of the 738 lemmas of the legacy corpus come from a pass of `gpt-5.6-luna` over the generated labels
(4,075 of the 6,283 labels changed), followed by a review sweep: a second `gpt-5.6-luna` pass flagged 63 labels that
misdescribe their gloss, and 34 of them were changed by hand (the other flags were placeholder glosses such as `noun`,
where the label cannot be judged). The `flag` column holds the `D` and `R` remarks of the pass. `missing-usages.tsv`
lists 33 lemmas whose common usage (a phrasal verb, a discourse use) Open English WordNet lacks; each is only a
candidate for a project sense.

The groups of the second batch follow the admission rule below with the evidence available now: of the 120 groups the
pass proposed, 84 are kept (77 with every member, 7 with a subset), all `equivalent` with the evidence
`curated:gloss near-identical glosses` (a `gpt-5.6-luna` pass judged the full glosses of every group, and two groups it
accepted were dropped by hand because their glosses differ in more than wording). The other 36 are dropped; they can
return when the classifier's confusion data shows that their senses are mixed up. Their `group_id` in `senses.tsv` is
empty.

## Glosses of project senses

A project sense (a sense that Open English WordNet does not have; source `dema`, synset id `dema-syn-...`) first had a
placeholder gloss: the seeder template ("The preposition "about"; a project sense, because Open English WordNet has no
preposition sense of this word."; 817 senses), a bare part-of-speech or label word (`noun`, `verb`, `adj-noun`; 118) or
"... no definition recorded" (2). A gloss is a placeholder when the sense is
a project sense and its gloss is empty, a bare word or hyphenated pair of words, the seeder template or ends with
"no definition recorded". The 937 placeholder glosses were replaced by real one-line definitions that
`gpt-6.1-sol` wrote from the lemma, the part of speech, the role of the sense (for example a closed class), the other
senses of the lemma and one or two example sentences. `project-glosses.tsv` is the record (`sense_id`, part of speech,
old gloss, new gloss). The glosses are stored where the seeder and the compiler read them: the committed table
`data/gloss` (the rows of the `dema-syn-*` synsets only; no gloss of an Open English WordNet synset was touched, and
OEWN glosses stay verbatim) and, for the 135 project senses that this curation lists, the `gloss` column of
`senses.tsv`, which `seed-inventory` writes into `data/`. No placeholder gloss remains.

## Technical lemmas (wave 3)

Technical instructions (aircraft and machine maintenance, written in plain "STE-style" English: "Mount the bracket to the
frame.", "Torque the bolts to 25 N m.") use words whose technical senses the encoders had not seen. A probe of 30 such
sentences found three kinds of gap, and wave 3 closed them:

- **Lemmas that were not curated.** `mount`, `bearing`, `bleed`, `torque`, `run`, `position`, `support`, `install`,
  `remove`, `attach`, `inspect` and `clean` (176 senses of the data) were added to `senses.tsv` with `tools/quota-fill/
  curate_new.py`: the Open English WordNet sense, its gloss and tag count (the tier follows the tag count: 5 or more C,
  1 to 4 U, none R), and a public label that a `gpt-5.6-luna` pass reviewed (a label that the generator wrote stays when
  it was good; for example `mount#gem-setting` instead of `mount#setting`, `bearing#rotating-support`).
- **Project senses of a word the dictionary has** (source `project`, no key). `seed-inventory` adds a sense of the existing
  entry (the sense id `dema-<lemma>-<pos>-1`, the synset `dema-syn-<lemma>-<pos>-1`) when the curation lists a project
  sense whose entry has no keyless sense yet: `mount#mounting-fixture` (a bracket or support that holds equipment),
  `bleed#vent` (release trapped air or fluid from a system), `arm#prime-system` (to arm an alarm or a fire suppression
  system), `lead#electrical-lead` (a conductor with a terminal), `flush#fluid-flush` (a cleaning operation that passes
  liquid through a system) and `tap#tapping-point` (a connection point in a line or circuit).
- **Analyses that were missing** (not senses): the verb `torque` (`torque`, `torques`, `torqued`, `torquing`; added in
  `corpus/lexicon/additions.tsv`, so it is a surface-only analysis without a sense) and the adjective analysis of the
  participial adjectives that technical text uses (`damaged`, `drained`, `drilled`, `alloyed`; listed in
  `corpus/lexicon/own-adjectives.tsv`), which a reviewed analysis of the verb form had hidden (`FormGenerator`,
  requirement `WordSense-WordData-FormGenerator-OwnEntries`). 114 such adjective entries exist; the other 110 (`based`,
  `covered`, `changed`, `driven`, ...) were left out on purpose (giving them all an analysis lowered the real-text fine
  top-1 by about two points, see the method of the quota filling in `corpus/README.md`).

`tech-senses.tsv` lists the 62 senses that technical text uses (picked by `gpt-6.1-sol` from the senses of 19 technical
lemmas, with the note of their technical use) and the number of **extra anchors** that wave 3 brings for each (14 for a
core meaning, 8 for a minor one): real FAA text first, then written technical sentences. The quota filling reads this
file (`--tech`). Gaps that remain (found by a review of the FAA sentences that the labeler could not place, by
`gpt-6.1-sol`): the noun `bleed` (bleed air), the
verb `torque` as a sense, the noun `lock` as a mechanism, `arm` as a moment arm, a ground roll and `clean` as
aerodynamically clean.

## Part of speech of a curated sense

The `part_of_speech` of a curated sense must be the part of speech of its sense in `data/` (the part of speech of its
Open English WordNet entry; an adjective satellite is an adjective); `seed-inventory` refuses the curation otherwise.
`right#agreement-yes` was curated as an interjection although Open English WordNet files the sense (`oewn-00151409-r`)
under the adverbs; it is an adverb here, as in the corpus rows. `back#animal-rear` is an adjective on purpose: it is the
adjective satellite `oewn-00132764-s` (`back%5:00:00:posterior:00`, "located at or near the back of an animal", as in
the back legs of a horse); the noun use of a horse's back is `back#body-trunk`.

## Groups

A **group exists only when it is needed**: a set of at least two, but not all, senses of one lemma and one part of
speech that are closely related in meaning and confusable. The curated name of a group in `groups.tsv` is `lemma#slug`;
`seed-inventory` writes it as the group id `lemma#group:slug` (for example `help#assistance` becomes
`help#group:assistance`). The marker `group:` keeps the id of a group apart from the ids of senses, so there is no
collision and no suffix. Group ids are versioned per data release and are not frozen (see `data/README.md`).

**Admission rule.** A group needs the curator's same-meaning judgment **and** evidence: observed confusion (the blind
judge or a classifier mixes the senses up) or near-identical glosses. `groups.tsv` records both:

- `kind` is `equivalent` (near-duplicate senses) or `related` (a genuine family of meanings). The curator's rule for the
  groups that have no judge data: `equivalent` when every non-head member (the member with the highest WordNet tag
  count is the head) is flagged `D` or the glosses are near-identical, otherwise `related`. For the groups that were
  named after one of their members, the kind follows the review of those groups (a family of meanings is `related`, a
  head with near-duplicates is `equivalent`).
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

The result is 140 groups of 307 senses: 110 `equivalent` and 30 `related` (the first batch has 56 groups of 138 senses,
26
`equivalent` and 30 `related`; the second batch 84 `equivalent` groups of 169 senses). The importer replaces the groups
of the lemmas of the curation as a whole and removes any group row whose origin is not `curated` (the retired default
groups). After the import the tables are edited in `data/` and these files are only the record of what was imported.

## Group review

The groups of the first batch were reviewed once (the data had 68 groups of 164 senses before): a group that rests on the
curator's judgment alone (near-duplicate senses with no observed in-group confusion, or one confusion) or that is not
one meaning is dropped (12 groups; the member senses stay and their `group_id` in `senses.tsv` is empty), and every kept
group has the id scheme `lemma#group:slug` (group ids are not frozen, see `data/README.md`). Kept with a decision:
`test#group:trial` is `related` (18/65 in-group confusion); `only#group:merely` is `related` and the `D` flag of
`only#exclusively` was cleared, because it is a major adverb reading (226 WordNet tags) and not a near-duplicate;
`first#group:first-in-order` is `equivalent` (both members are `D`); `still#group:distillation` is `related`.
**Known gaps of the evidence.** Six `related` groups that were named after a member have no observed in-group
confusion (`home#group:domestic`, `home#group:homeland`, `review#group:look-again`, `still#group:distillation`,
`test#group:examine-knowledge` and `test#group:try-out`), and two of them (`home#group:domestic` and
`test#group:try-out`) have no in-group alternate either; they stay on the curator's judgment of related meanings. The
groups without judge data (the other 40) rest on the curation flags and glosses only. These are candidates to drop in a
later release if a larger judge run still shows no confusion.
