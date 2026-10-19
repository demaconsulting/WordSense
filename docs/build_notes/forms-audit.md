# Forms Audit

This chapter records how the quality of the generated inflected forms is measured, the result on the committed data,
and what the measurement can and cannot show.

## Method

The `audit-forms` command of the builder draws a seeded stratified sample of the `form` rows that carry an inflectional
feature: 300 rows (or all rows of a smaller stratum) for each pair of a provenance and a kind (`entry`: the spelling
belongs to an entry of the dictionary; `surface`: a surface-only row). Each sampled row (spelling, lemma, part of
speech, features) is given to LemmInflect, a pip-installed library that is used at development time only, as a checker
that flags rows to look at (`pip install lemminflect`). It is not a dependency and nothing is shipped from it: its forms
are never adopted and its raw output is not committed. A row the library accepts is valid. For every disagreement, we
check the row against our own sources (the OEWN entries and forms, AGID, the Moby word list, our lists and rules) and
decide: a real error is corrected in the generator or in `corpus/lexicon/form-corrections.tsv`, or the row is left out;
a valid row (a Latin plural, a `-men` compound, a rare word the library lacks) is recorded as valid, with the source that
decides it, in `tools/WordSense.Builder/audit/adjudications.tsv`. These verdicts are ours. A disagreement that is not
adjudicated is an error. The command refuses a sample size below one, an empty or malformed oracle file, a verdict it does
not know and a sampled row without an oracle verdict, and it fails when a generated stratum has rows but no checked row.
The report gives, for each stratum, the population, the rows checked (rows the library cannot
judge are left out), the disagreements, the errors, the error rate and its 95% Wilson interval. The command fails when
the error rate of a generated stratum (`rule`, `rule_variant`, `agid`, `moby_reverse`) reaches 1%. The weighted rate is
the error rate of the generated strata weighted by their population.

```text
dotnet run --project tools/WordSense.Builder -c Release -- audit-forms --seed 20240601 --output audit.md
```

## Result

Committed data after the corrections below, 300 rows per stratum, errors after our adjudication (errors / rows checked,
95% Wilson interval). Every seed passes; the weighted rate of the generated strata is 0.000% on all five.

| Seed | agid | moby_reverse entry | moby_reverse surface | rule entry | rule surface | rule_variant | Gate |
| ---- | ---- | ------------------ | -------------------- | ---------- | ------------ | ------------ | ---- |
| 20240601 | 0/300 | 0/253 | 0/300 | 0/185 | 0/300 | 0/300 | pass |
| 99 | 0/300 | 0/253 | 0/300 | 0/166 | 0/300 | 0/300 | pass |
| 7 | 0/300 | 0/253 | 0/300 | 0/161 | 0/300 | 0/300 | pass |
| 2025 | 0/300 | 0/253 | 0/300 | 0/177 | 0/300 | 0/300 | pass |
| 31337 | 0/300 | 0/253 | 0/300 | 0/175 | 0/300 | 0/300 | pass |

The 95% Wilson interval of 0 errors is 0% to 1.26% for 300 rows, 1.50% for 253, 2.03% to 2.33% for 161 to 185 rows
(`rule` entry: 2.03% seed 20240601, 2.26% seed 99, 2.33% seed 7, 2.12% seed 2025, 2.15% seed 31337).

The strata that are not gated have no error either: `hand` 0/300, `oewn_form` 0/300, `irregular_list` 0/128 and `carried`
0/254 to 0/263 on every seed (95% upper bounds 1.26%, 1.26%, 2.91% and 1.49%). The checker disagreed with 53 to 73
rows of `agid`, 30 to 39 of `hand`, 39 to 55 of `oewn_form`, 6 of `irregular_list`, 11 to 17 of `rule_variant`, 7 of
`moby_reverse/entry` and 8 to 15 of `moby_reverse/surface` per seed; our verdicts found them valid (mostly Latin and
Greek plurals, `-men` and `-women` compounds and irregular pasts that the library lacks).

All distinct disagreements of the five seeds were adjudicated by reading each row against our sources (the
`molochs` row that seed 7 picked after the last data change included). Before the
corrections they held about two dozen real errors (and 79 more rows found by a sweep of the phrases): in the sample of
seed 20240601 the gated strata had 1 error in 300 `agid` rows, 1 in 300 `rule_variant` rows and 1 in 263 `carried` rows,
and the other seeds found more of the same kinds. The corrections are:

- OEWN form typos, where AGID and Moby give the right plural: `araglis` (argalis), `madornos` (madronos), `cercariiae`
  (cercariae), `filariiae` (filariae); `muskallunge` is a singular, `gan` and `quick-froze` are pasts only.
- AGID or Moby mistakes: the plurals `kindling`, `tabes` of `tabis`, `trades` of `trad`, `behooves` of `behoof`, the
  misspelling `unmaned`, and `photosynthesises`. The nonstandard `formating` stays, adjudicated valid: the Moby word
  list holds it, and the dictionary must know every word of that list.
- Generator rules: a spelling in `-s` of a verb is a third person, never a past (`chevies`, `gasses`, `programmes`,
  `swops`, `quizzes`, `whizzes`, `tramels`, `nonplusses`, `co-ordinates`); a spelling of the base itself is not a past
  (`co-ordinate`, `abye`); `unrip` doubles its consonant (`unripped`, not `unriped`); a phrase whose head is an
  irregular verb has no regular past (`goed ahead`, `doed away with`, `bed active`: 79 rows); and the override `cates`
  as a plural of the acronym CAT was removed.
- Corroboration by the inflection database is keyed by lemma and part of speech, so a verb form of AGID no longer
  corroborates a noun, adjective or adverb analysis of the same lemma, and an AGID form that is no regular inflection
  (`outdone`, `segueing`) needs a second source: 91 analyses went, 3 came back as the plain base-word row of the
  dictionary, and 47 spellings left the table.

The five seeds are the regression gate: a change that adds invalid rows makes a new disagreement that has no
adjudication and fails the gate. The self-adjudication of one reviewer is a limit of the method, and the strata that the
checker cannot judge well (curated rows and rows of the OEWN) are reported but not gated.

Known weaker rows that the generator keeps because they cannot be told from valid ones without word frequency: `Past+Part`
for an OEWN verb form of an irregular verb that gives no type (for example `snuck`). The AGID `-ing` plurals of mass
nouns are no longer kept (see the next section).

## Reproducible rebuild and -ing mass nouns

`rebuild-forms` is now idempotent: the reviewed rows of the earlier tables are the committed input
`corpus/lexicon/form-frozen.tsv`, the ranks are a deterministic function of the Moby letters and explicit tie-breaks, and
the generator no longer reads its own output, so `verify-forms` regenerates the tables from the committed inputs and the
two pinned files and finds no difference. Besides the -ing plurals below, the change removed a few rows that earlier
generator versions had written and that no current rule produces (the OEWN noun and adverb `am`, a gerund `during`);
the table has 365,503 spellings (50 more than before) and 400,205 analyses (9 fewer).
The 59 AGID plurals spelled like their singular on mass nouns in -ing (`bowling`, `brisling`, `catling`, `sibling`,
`feeling`) are replaced by the regular plural (`bowlings`); `herring`, `offspring`, `ling` and `whiting` keep theirs.
The audit gate passes with the seeds 20240601, 99, 7, 2025 and 31337 (weighted rate 0.000% to 0.001% before the
adjudication of four AGID plurals that the checker did not know, `earthmen`, `lochs`, `tunicae` and `cowmen`, all valid
by AGID and OEWN, which earlier seeds had not drawn).
The regular forms with a single source (`canallers`, `complices`, `dismasted`) are valid words that stand on the rule and
the base; they stay.

## Coverage cost

Requiring two independent sources removed about 12,000 candidate rows; the table has 13,199 fewer spellings than the
first generator build without the requirement (378,652 to 365,453) and 13,402 fewer analyses (413,616 to 400,214).
Regular -s, -ed and -ing forms of real lemmas are kept, so the
common words stay resolved.
