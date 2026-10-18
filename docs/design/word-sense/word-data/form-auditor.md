### FormAuditor Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`FormAuditor` measures the quality of the generated form rows: it draws a seeded stratified sample, asks an independent
checker for a verdict on each row, and reports the error rate per stratum with a Wilson interval. It is a regression
gate.

#### Data Model

Strata are the provenance of a row and whether the spelling is shared with an entry (`rule/entry`, `rule/surface`,
`agid/entry`, `moby_reverse/surface` and so on). The default sample is 300 rows per stratum. The checker is the
LemmInflect library, run from a Python script under `tools/WordSense.Builder/audit`. It is a checker only; nothing from
it is shipped. A disagreement is an error unless `audit/adjudications.tsv` records the row as valid (a reviewed
decision with a note).

#### Key Methods

- `Select(data, sample, seed)`: the sample and the population of each stratum.
- `RunOracle` / `ReadVerdicts`: the checker verdicts.
- `Evaluate(...)`: the error counts, the weighted rate and the gate.
- `Wilson(errors, n)`: the 95% interval.
- `Render(...)`: the Markdown report.

#### Error Handling

A missing checker (Python or the library) throws with the install instruction. The command exits with a failure when a
generated stratum reaches the limit.

#### Dependencies

`DataSchema`, `DataTables`, Python 3 with LemmInflect.

#### Callers

The `audit-forms` command.
