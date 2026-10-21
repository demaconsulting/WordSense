### FormAuditor Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`FormAuditor` measures the quality of the generated form rows: it draws a seeded stratified sample, asks an independent
checker for a verdict on each row, and reports the error rate per stratum with a Wilson interval. It is a regression
gate.

#### Data Model

Strata are the provenance of a row and whether the spelling is shared with an entry (`rule/entry`, `rule/surface`,
`llm/entry`, `moby_reverse/surface` and so on). The default sample is 300 rows per stratum. The checker is the
LemmInflect library, run from a Python script under `tools/WordSense.Builder/audit`. It is a development-time checker
only: it
flags rows to look at, nothing is shipped from it, its forms are never adopted and its raw output is not committed. A
disagreement is an error unless `audit/adjudications.tsv` records our own verdict (valid, decided from OEWN, the authored
forms, Moby or our lists and rules; optionally restricted to one feature set). A row the checker accepts is never an error.

The report gives, for each stratum, the population, the rows checked (rows that the checker cannot judge are left out),
the disagreements, the errors, the error rate and its 95% Wilson interval, and the weighted error rate of the generated
strata (population weighted). The gate fails when the error rate of a generated stratum (`rule`, `rule_variant`, `llm`,
`moby_reverse`) reaches 1%. The curated strata (`hand`, `oewn_form`, `irregular_list`, `carried`) are reported but not
gated, because the checker judges them poorly (Latin and Greek plurals, `-men` compounds and rare irregular pasts that
it lacks are mostly valid).

The five seeds 20240601, 99, 7, 2025 and 31337 are the regression gate: a change that adds invalid rows produces a
disagreement that has no adjudication and fails it. On the committed data every seed passes with a weighted error rate
of 0.000%; the 95% Wilson interval of zero errors is 0% to 1.26% for 300 rows and about 2% to 2.3% for the 160 to 190
rows of the `rule` entry stratum. The method has limits: the verdicts are ours (the audit of the regenerated table used
an adjudicator model with the independence measures of the authoring, and a second model of another family rechecked
every verdict of medium confidence; see `tools/forms-audit/README.md`), and a reviewer who adjudicates his own sample
can be wrong. Rows known to be weaker but kept because they cannot be told from valid ones without word frequency include
a `Past+Part` form of an irregular verb form that gives no type (`snuck`).

#### Key Methods

- `Select(data, sample, seed)`: the sample and the population of each stratum.
- `RunOracle` / `ReadVerdicts`: the checker verdicts.
- `Evaluate(...)`: the error counts, the weighted rate and the gate.
- `Wilson(errors, n)`: the 95% interval.
- `Render(...)`: the Markdown report.

#### Error Handling

A missing checker (Python or the library) throws with the install instruction, and so does an oracle file in which one
row has two verdicts. The command refuses a sample size below one, an empty or malformed oracle file, a verdict it does
not know and a sampled row without an oracle verdict, and it fails when a generated stratum has rows but no checked row.
The command exits with a failure when a generated stratum reaches the limit.

#### Dependencies

`DataSchema`, `DataTables`, Python 3 with LemmInflect.

#### Callers

The `audit-forms` command.
