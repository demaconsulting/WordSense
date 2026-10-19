# Forms Audit

This chapter records how the quality of the generated inflected forms is measured, the result on the committed data,
and what the measurement can and cannot show.

## Method

The `audit-forms` command of the builder draws a seeded stratified sample of the `form` rows that carry an inflectional
feature: 300 rows (or all rows of a smaller stratum) for each pair of a provenance and a kind (`entry`: the spelling
belongs to an entry of the dictionary; `surface`: a surface-only row). Each sampled row (spelling, lemma, part of
speech, features) is given to LemmInflect, a pip-installed library that is used as an independent checker only
(`pip install lemminflect`); nothing from it is shipped. A row is accepted when the library produces the spelling from
the lemma for those features. Every disagreement is an error unless a reviewer recorded it as valid in
`tools/WordSense.Builder/audit/adjudications.tsv` (with a note: a rare word that the library does not know, a Latin
plural, and so on). The report gives, for each stratum, the population, the rows checked (rows the library cannot judge
are left out), the disagreements, the errors, the error rate and its 95% Wilson interval. The command fails when the error
rate of a generated stratum (`rule`, `rule_variant`, `agid`, `moby_reverse`) reaches 1%. The weighted rate is the error
rate of the generated strata weighted by their population.

```text
dotnet run --project tools/WordSense.Builder -c Release -- audit-forms --seed 20240601 --output audit.md
```

## Result

Committed data, 300 rows per stratum, errors after adjudication:

| Seed | agid | moby_reverse (entry / surface) | rule (entry / surface) | rule_variant | Weighted rate | Gate |
| ---- | ---- | ------------------------------ | ---------------------- | ------------ | ------------- | ---- |
| 20240601 | 0.33% (0.06-1.86) | 0% / 0% | 0% / 0% | 0.33% (0.06-1.86) | 0.005% | pass |
| 99 | 0.67% (0.18-2.40) | 0% / 0.33% | 0% / 0% | 0.33% (0.06-1.86) | 0.008% | pass |
| 7 (not adjudicated) | 9.33% (6.54-13.16) | 0% / 1.33% | 0% / 0.33% | 3.33% (1.82-6.03) | 0.17% | fails |
| 2025 (not adjudicated) | 8.00% (5.43-11.63) | 0% / 2.67% | 0% / 0.67% | 4.00% (2.30-6.86) | 0.26% | fails |

The two adjudicated seeds are the regression gate: a change that adds invalid rows makes a new disagreement that has no
adjudication and fails the gate. Seeds 7 and 2025 were drawn after the adjudication and are not adjudicated. They are
out-of-sample checks: the disagreements there are mostly valid rare words that the checker lacks (for example the
`-men` compounds `bandsmen` and `jurymen`, `tomatilloes`, `linguini`), which a reviewer would adjudicate as valid, so
the per-stratum rates are an upper bound. A weighted rate below 0.3% on every seed shows that the generated rows
as a whole are far below the 1% target, but the unadjudicated per-stratum rates for the AGID and `rule_variant`
strata (a few hundred rows each) are not proven below 1%. The self-adjudication of one reviewer is a limit of the method.

Known invalid rows that the generator keeps because they cannot be told from valid ones without word frequency:
`kindling` (plural), `tabes`, `behooves` and two misspelt spellings (a doubled-letter and a dropped-letter case).

Strata the checker cannot judge well (curated rows and rows of the OEWN) are reported but not gated: `carried`,
`hand`, `irregular_list` and `oewn_form` disagree at 1% to 20% because the library lacks the word or makes a different
choice, not because the rows are wrong.

## Coverage cost

Requiring two independent sources removed about 12,000 candidate rows; the table has 13,074 fewer spellings than before
(378,652 to 365,581) and 413,616 to 400,395 analyses. Regular -s, -ed and -ing forms of real lemmas are kept, so the
common words stay resolved.
