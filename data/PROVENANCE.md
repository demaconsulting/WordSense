# Provenance of the word data

## Source

- **Open English WordNet 2025 Edition** (OEWN), <https://github.com/globalwordnet/english-wordnet>, <https://en-word.net>.
- License: **Creative Commons Attribution 4.0 (CC BY 4.0)**. The tables derived from it keep that license; credit
  Open English WordNet when you redistribute them.
- Open English WordNet is itself derived from Princeton WordNet (WordNet 3.1, Copyright 2011 by Princeton University)
  under the WordNet License and requires that its notice (`WNDB_License.txt` of the release) goes with the data; it is
  reproduced in `src/DemaConsulting.WordSense/NOTICE.md`, the notice file of the package that embeds the word image.
- File: `english-wordnet-2025.xml.gz` (the LMF file of the release; **not committed**), SHA-256
  `9CA6D1DCB75F822FDD66617F7D9DA48142ACE38DD544D6AD5E2FECA1674AD3FE`, checked by `import-oewn` against the pin
  `OewnImporter.PinnedSha256`.
- Imported with the `import-oewn` command of `tools/WordSense.Builder` (split threshold 8000 rows per first letter). The
  WordNet 3.0 sense frequency counts are a second source; see the section below.

## Imported

Every lexical entry, sense, synset, definition, example, synset relation, sense relation and pronunciation of the file:
135,969 entries, 185,129 senses, 107,519 synsets, 107,524 glosses, 49,596 examples, 117,405 synset relations,
72,290 sense relations and 43,534 pronunciations (the first stage of this data; see the commits for later additions).

## Derived columns

- `entry.entry_id` is the OEWN entry id; `homograph_no` is 0 when the lemma and part of speech is unique, else 1..n by
  the numeric tail of the id; the part of speech `s` of a satellite entry is stored as `a`.
- `entry.token_count` and `head_idx` are a heuristic (first token of a verb; token before the first preposition of a
  noun; last token otherwise) and can be corrected by hand.
- `synset.head_synset_id` comes from the `similar` relation of a satellite synset; `sense.sense_key` from the sense id.
- Relations: the hyponym-type direction rows of OEWN (`hyponym`, `holo_*`, `has_domain_*`, ...) are stored as their
  inverse and deduplicated; symmetric relations are stored once. The importer reported no missing inverse and no self
  relation for the 2025 file.
- Text: tab or line break in a cell is replaced by a space, text is NFC (11 cells were cleaned).

## Left out

Subcategorization frames, adjective position, syntactic behaviour, ILI definitions and links (`ili`, `ILIDefinition`),
`dc:source`, and the `Form` element. The id map holds the identifiers of the previous release (OEWN 2024, SHA-256
`E1F633B0A93758CAE34EA27C44C4DAD310A8AF2467B155F99DD6673AF697E875`), written with `import-oewn --previous`.

## Project additions

Rows with `source_id` `dema` (MIT, project-authored) are added to cover words and senses that Open English WordNet
does not have: 120 entries, each with one synset, sense and gloss. See [MAPPING.md](MAPPING.md) for how they and the
coarse classes were made. The sense inventory adds 821 project senses, one for each closed-class entry (determiner,
pronoun, preposition, conjunction, interjection, auxiliary; the entries `dema-or-c` and `dema-having-x` were added with
the lexicon fixes), each with a `dema` synset, a generated gloss and no sense
key; four of them (`right#discourse-marker`, `well#discourse-marker`, `help#cry-for-aid` and `however#conjunction`) have
a curated label and an authored gloss (`corpus/curation`).

## WordNet 3.0 sense frequency counts

- **WordNet 3.0** (Princeton University, <https://wordnet.princeton.edu>), the file `dict/index.sense` of the Princeton
  distribution `WordNet-3.0.tar.gz` (<https://wordnetcode.princeton.edu/3.0/WordNet-3.0.tar.gz>, SHA-256
  `640DB279C949A88F61F851DD54EBBB22D003F8B90B85267042EF85A3781D3A52`). The SHA-256 of `index.sense` is
  `68B3A468CDDFD8E92134B9B0624339A02A1B837159243C297C5F138A3D618392` (206,941 lines), checked by `import-wn30-counts`
  against the pin `OewnImporter.PinnedCountsSha256`. The file is not committed; extract it from the tarball into
  `artifacts/inputs/wn30`.
- Only the tag count of each line (the fourth field) is used. The count is joined to the `sense` table on the sense key
  and written to `sense_frequency` with the source `wn30`: 185,129 rows (one for each sense that has a key), 34,842 with
  a count above 0 and 150,287 with the count 0 (the file lists no line, or lists 0). 27,255 lines of the file match no
  sense key of Open English WordNet 2025 (636 of them with a count above 0, 4,270 of the 249,492 tags), because
  the 2025 release changed some keys; those counts are not used.
- The counts size the training data of each sense, feed the usage prior of the encoder training and, as a 16-bit count for
  each sense in the word image of the core package, the usage prior with which the Basic model ranks the senses of the
  lemmas that no model scores; they do not order the senses of a lemma (the image keeps the order of Open English
  WordNet) and they are never
  a sense label. SemCor 3.0, the sense-annotated text whose tags WordNet counts come from, is an **evaluation source
  only**: the builder downloads it to a cache to measure the models; it is never committed, redistributed or used for
  training.
- **License notice of WordNet 3.0**, reproduced as the license requires:

  ```text
  WordNet Release 3.0

  This software and database is being provided to you, the LICENSEE, by
  Princeton University under the following license.  By obtaining, using
  and/or copying this software and database, you agree that you have
  read, understood, and will comply with these terms and conditions.:

  Permission to use, copy, modify and distribute this software and
  database and its documentation for any purpose and without fee or
  royalty is hereby granted, provided that you agree to comply with
  the following copyright notice and statements, including the disclaimer,
  and that the same appear on ALL copies of the software, database and
  documentation, including modifications that you make for internal
  use or for distribution.

  WordNet 3.0 Copyright 2006 by Princeton University.  All rights reserved.

  THIS SOFTWARE AND DATABASE IS PROVIDED "AS IS" AND PRINCETON
  UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR
  IMPLIED.  BY WAY OF EXAMPLE, BUT NOT LIMITATION, PRINCETON
  UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES OF MERCHANT-
  ABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE
  OF THE LICENSED SOFTWARE, DATABASE OR DOCUMENTATION WILL NOT
  INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR
  OTHER RIGHTS.

  The name of Princeton University or Princeton may not be used in
  advertising or publicity pertaining to distribution of the software
  and/or database.  Title to copyright in this software, database and
  any associated documentation shall at all times remain with
  Princeton University and LICENSEE agrees to preserve same.
  ```

- **SemCor 3.0 (evaluation only).** The archive `semcor3.0.tar.gz`
  (<https://web.eecs.umich.edu/~mihalcea/downloads/semcor/semcor3.0.tar.gz>, SHA-256
  `A8000014D6FC864F8BD9D83C62BE601151CADD617C6554A39A1AD38B4B3F017B`, checked by `evaluate-senses` against the pin
  `SemCor.ArchiveSha256`) holds 352 sense-annotated texts of the Brown Corpus: Rada Mihalcea's automatic mapping to
  WordNet 3.0 of the Princeton SemCor 1.6 ("SemCor 1.6 was created and is property of Princeton University"). Its terms
  are those of WordNet: the archive carries the Princeton license of WordNet 1.6 and the Semantic Concordance (the text
  of the notice above with "WordNet 1.6 Copyright 1997 by Princeton University. All rights reserved."), and its README
  says "In agreement with the license from Princeton University, you are granted permission to use, copy, modify and
  distribute this database for any purpose and without fee and royalty is hereby granted, provided that you agree to
  comply with the Princeton copyright notice and statements, including the disclaimer, and that the same appear on ALL
  copies". That is permissive for the evaluation use made of it here. The builder downloads the archive to the
  git-ignored cache `tools/open-corpus/cache`, reads it, reports scores and never writes a sentence of it to any file
  of the repository; the notices stay in the archive, and are reproduced here for credit. SemCor is not used for
  training or tuning anything; the mapping from WordNet 1.6 to 3.0 is automatic (some senses were dropped, `wnsn=0`)
  and the Open English WordNet 2025 changed some sense keys, so the evaluation reports how many annotations it could
  map.

## Sense inventory (labels, groups, project senses)

- `sense_label` has 129,433 rows: every sense of a lemma of one token (128,486 of Open English WordNet) and every
  project sense (947, six of them added to entries of the dictionary for technical text). 6,615 labels are curated
  (`corpus/curation`: 770 lemmas, 6,760 senses of the curation file including the project senses); the other 121,871
  labels are generated by `label-senses` (they were never published: a curated label replaces a generated one). The
  senses of lemmas of several tokens have no label or group. (The counts of the groups below are those of the first
  stage; the 140 groups of 307 senses are in `corpus/curation/README.md`.)
- `sense_group` and `group_sense` have the 56 curated groups of 138 senses (26 `equivalent`, 30 `related`) and nothing
  else: a group exists only when it is curated, with the curator's same-meaning judgment and evidence (observed confusion
  or near-identical glosses), as a set of at least two, but not all, confusable senses of one lemma and part of speech.
  Of the 194 groups that the curation of the 20 lemmas gave (191 reviewed groups, three of them split by part of
  speech), 122 had one sense and 4 had every sense of their lemma and part of speech; they are left out, and so are the
  83,727 default groups that the first version of the inventory made for the other lemmas, and 12 curated groups that
  the cleanup pass dropped (`corpus/curation/README.md`). The id of a group is `lemma#group:slug`; it is versioned per
  data release and not frozen, unlike the ids of senses. `sense_label_map` starts empty: no label
  has been published yet. The table `sense_respelling` of the pronunciation-era inventory was empty and is removed.
- The class tables of the earlier inventory (`coarse_class`, `class_sense`, `class_entry`) and `form_sense_scope` are
  retired; the temporary record of their mapping was deleted with the relabeling of our own corpus (see MAPPING.md).

## Forms (written forms and their analyses)

The `form` table is regenerated by the `rebuild-forms` command (`FormGenerator`) from the entries, the Open English
WordNet forms, the closed lists of `corpus/lexicon` and the build-time sources below. Each `form` row records its
provenance (`oewn_form`, `irregular_list`, `contraction`, `hand`, `carried`, `rule`, `rule_variant`, `llm`,
`moby_reverse`). The current table has 365,141 spellings and 400,062 analyses. Provenance counts: oewn_form 138,980;
rule 161,513; moby_reverse 88,793; carried 5,223; llm 3,528; rule_variant 1,251; hand 514; irregular_list 169;
contraction 91. Five analyses were added for the heteronym table: the adjective `graduate` and the noun `separate`
through the additions `graduate J` and `separate N` of `corpus/lexicon/additions.tsv`, and `separates` as a noun and
`axes` as the noun and verb forms of `axe` through the flag `open` of those two reviewed spellings. (Before the authored
forms replaced the third-party inflection database the table had 365,504
spellings and 400,216 analyses. Against that table 1,611 keys, that is (spelling, entry, features), were lost and 1,410
gained: the losses are mostly wrong forms that the fixed rules no longer make (919 keys: `-men` for compounds of man,
the plural of a lemma in `-men`, doubling and `-ie` verbs), forms that the authors reject (549) and spellings that only
the old database attested (117, and 78 plurals spelled like the singular); the gains are corrected rule forms (767),
authored forms and variants (532) and plain base rows of dictionary words that lost a reverse analysis (91).)

The regeneration is reproducible: the same inputs give identical bytes (a unit test on small fixture inputs checks it),
so `rebuild-forms` is idempotent. To regenerate, fetch the one pinned input below, run
`dotnet run --project tools/WordSense.Builder -- rebuild-forms --oewn <english-wordnet-2025.xml.gz>`
and check `git diff data`: any change is drift that a reviewer sees as a normal data diff. The reviewed analyses of the
earlier tables are the committed input
`corpus/lexicon/form-frozen.tsv` (rows `carried`), so the generator never reads its own output. The pinned file is not
committed; fetch it into `artifacts/inputs` (ignored by git): `english-wordnet-2025.xml.gz` from the Open English
WordNet 2025 release (SHA-256 pinned in `OewnImporter`). It is checked against its pin before anything is generated.
The other inputs are committed: the authored forms (`corpus/lexicon/forms-authored.tsv`) and the open corpus.

- Sources: Open English WordNet forms (CC BY 4.0); the closed and hand lists (`irregular-closed.tsv`,
  `irregular-forms.tsv`, `contractions.tsv`, `form-overrides.tsv`; general knowledge, MIT); the pinned Moby word list
  (public domain) read in reverse; and the forms written for the project (`corpus/lexicon/forms-authored.tsv`,
  CC0 1.0, written by the model gpt-5.6-luna from the lemma, part of speech and category alone and verified blind by
  gpt-6.1-sol; the provenance of every row is in `corpus/lexicon/forms-authored-provenance.tsv`; method below). The tagged
  tokens of the open corpus (CC0) attest forms as well. No
  treebank-derived list and no third-party inflection database is a source of the table.
- Corroboration: a generated row is written only when two independent sources support it (a regular rule plus the
  word list, the authored forms or the open corpus), except that a regular -s, -ed or -ing form of a real lemma stands on
  the rule and its base. Uncorroborated candidates are left out, so those spellings stay unknown.
- Quality: `audit-forms` draws a seeded stratified sample (300 rows per source) and reports each source's error rate with
  a 95% Wilson interval; the method and the gate are in the design of the form auditor
  (`docs/design/word-sense/word-data/form-auditor.md`). The adjudications in
  `tools/WordSense.Builder/audit/adjudications.tsv` are our own verdicts, decided from OEWN, the authored forms, the Moby
  list and our lists and rules (the evidence text of the older rows names the third-party database that was a source
  then; it is history and no longer a source). Real errors are corrected in the generator or in
  `corpus/lexicon/form-corrections.tsv` (each with its evidence), never by adopting a form from the checker.
- LemmInflect (a pip package, not a dependency) is used at development time only, as a checker in the audit: it flags
  rows to look at. Nothing ships from it: no form, list or text of it is in the tables, the image, the package or the
  repository, and its output is not committed.
- Rows of source `dema` are MIT.

**Authored forms: why, method and independence.** An earlier table took the irregular forms of about 3,800 lemmas from a
third-party inflection database (AGID) and let it corroborate about 4,800 regular-looking forms (`cleaned`, `highest`,
`brothers`) that no other source attests. Its license is a permission notice that carries the notices of two older
dictionaries, which the project no longer wants to carry, so the database, its importer, its pin and its notice file were
removed and a file of forms written for the project (CC0 1.0) replaced it:

- *Worklist.* One task per lemma and part of speech whose shipped analyses would be lost or changed without the old
  database, minus what the written forms of Open English WordNet and the training splits of the open corpus already
  attest, plus 140 prefixed shapes of closed-list irregular verbs (`overtake`) and about 15% unmarked controls of known
  answers. The worklist (5,351 tasks and no inflected form) was a one-time input and is not kept; the expected forms
  stayed in a hidden key outside the repository.
- *Writing.* A model (gpt-5.6-luna) wrote the primary form, the accepted variants and a confidence for every category of
  a task (`NONE` where a form does not exist), from the lemma and the part of speech alone. A second model (gpt-6.1-sol)
  answered the same tasks blind, in another order, without seeing a candidate, because a judge tends to accept what it is
  shown and independent production gives an agreement measure per form. A third pass adjudicated the disagreements.
  The agents had no tools and no files beyond their batch, and each output is dedicated to CC0 in the prompt.
- *Merge.* Equal primary forms are accepted (`agree`, 6,377 decisions). One `NONE` against a form that equals the regular
  rule form is accepted without adjudication (`regular-accepted`, 1,829): productive regular inflection is a judgment of
  existence and frequency, where omission loses analyses and inclusion is harmless. Everything else went to the
  adjudication (`adjudicated`, 425). The variants of both authors are united; a variant that only one author listed and
  that the fixed rules classify as a known defect (`chairmans`) is dropped. An agreed `NONE` is recorded in the sidecar
  and vetoes the regular rule for that lemma and kind.
- *Independence.* The old database input was moved out of the repository before any batch was written; the batches carry
  the lemma, the part of speech and the categories only (all categories, so a batch does not show which one the old
  database supported); the hidden key was used only afterwards, to score the result; and the controls calibrate the
  false-accept and false-`NONE` rates of the two models. The builder reads the authored file and no file of the old
  database. A limit remains: the models know English from their training, which may include text derived from that
  database or from the dictionaries it cites, so the file is independent of the database file, not of everything a model
  has read.
- *Result.* Without the old database 5,649 keys of the committed table were lost and not attested elsewhere; the authored
  forms restore 5,210 (92.2%). The authors reject 199 of the lost keys as invalid (Greek `-itis` plurals in `-es`,
  `-mans` compounds, wrong headwords), and a criterion that counts those as lost would reward keeping errors, so against
  the restated criterion 5,210 of 5,450 keys (95.6%) are restored. The plan asked for 97%, which is **not met**; the
  unrestored keys are old-database spellings that the authors do not reproduce and spelling variants beside another form,
  and they stay lost for independence.
- *Audit of the regenerated table.* The first `audit-forms` run with the five seeds failed the gate: the `llm/entry` and
  `rule_variant/entry` strata had 3% to 10.7% disagreements that no adjudication covered. The 181 distinct rows were
  judged by an adjudicator model blind to the stratum and the provenance, with about 15% controls (known wrong and known
  right spellings); 172 were `valid` and 9 `invalid`. Because the adjudicator passed 3 of 12 known-wrong controls, a second
  model of another family rechecked the 16 medium-confidence `valid` verdicts, and a row stays valid only when both agree.
  The 19 rows judged invalid were corrected at their source (the removal of the variant from
  `corpus/lexicon/forms-authored.tsv`, noted in the evidence of its sidecar row, or `corpus/lexicon/form-corrections.tsv`).
  After the regeneration the five seeds pass with no unadjudicated row. A second audit round followed the fixes of the
  generator (authored forms now join the forms of the lists and of Open English WordNet instead of being dropped, and the
  agreed `NONE` decision also stops the reverse rule): the five seeds drew 30 further rows without a verdict, which
  gpt-6.1-sol judged (26 `valid`, 4 `invalid`; the 5 controls were judged right) and claude-sonnet-5.5 rechecked (the 4
  medium-confidence `valid` verdicts and the 4 `invalid` ones; 12 controls, all judged right). 23 rows are `valid` in
  both passes and are recorded with the models and confidences in their evidence; 7 were corrected at their source (the
  variants `viri`, `signiori`, `sahuaroes`, `magnifici`, `fenestrases` and, because the two models disagreed,
  `candelabras` and `calliopsis` were removed from `corpus/lexicon/forms-authored.tsv`, each noted in its sidecar row).
  The five seeds then pass the gate; the sample of seed 7 holds one row that no verdict covers (`caddieing`, below the
  limit of 1% of the weighted error). The scripts of the one-time authoring were removed
  once the file was written (this section is the record of the method); `tools/forms-audit` keeps the workflow of the
  audit verdicts.

An earlier version of the tables was bootstrapped from the legacy corpus builder; its reviewed rows are the frozen input
`corpus/lexicon/form-frozen.tsv` (a one-time derivation from the reviewed table before the generator existed), and the
rows that the rules can still derive are `carried`.

- A dictionary word that has no Open English WordNet entry for its lemma and part of speech gets a new entry of source
  `dema` and the id `dema-<lemma>-<pos>`, with no sense (167,583 entries). These entries exist only so that forms
  refer to a lemma; they are not WordNet data.
- `features` are inferred from the regular English endings (`Number=Plur`, `Person=3+Tense=Pres`, `VerbForm=Ger`,
  `Degree=Cmp`, ...); a row that is inferred has `reviewed` 0, and a row of a labeled class of the earlier inventory
  had `reviewed` 1. Irregular forms can have empty features.
- The corpus listed 19 inflected forms under a lemma that cannot produce them (see MAPPING.md). `wick`, `wicks` and
  `wicking` now use a verb `wick`, and `re-sign`, `re-signs`, `re-signed` and `re-signing` a verb `re-sign` (new `dema`
  entries, `reviewed` 0). `supposes`, `supposing`, `blesses` and `blessing` use `suppose` and `bless` (the entries of
  their lemma-split class of the earlier inventory). For `conjuncts`, `pustulates`, `tiered`,
  `exited`, `exiting`, `ironed`, `ironing` and `stricken` the lemma of the legacy answer was already a valid lemma of
  an entry (OEWN or `dema`), so the analysis uses it unchanged.
- A lemma-split class of the earlier inventory with several linked entries of the right part of speech used the entry
  whose lemma the class label names, else the alphabetically first lemma.
- `or` has the conjunction analysis `dema-or-c` first (a `frozen` row of `form-frozen.tsv`), followed by the surface
  analyses of the dictionary; each of the 27 auxiliary verbs of `corpus/lexicon/auxiliaries.tsv` has its entry
  `dema-<word>-x` with the project sense `<word>#auxiliary`, and an Auxiliary analysis (the inflected forms `am`, `is`,
  `was`, `has`, `having`, `does`, ... analyse as the auxiliary lemma `be`, `have` or `do`).
