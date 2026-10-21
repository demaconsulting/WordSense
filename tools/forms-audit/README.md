# Form audit: verdicts for the rows that `audit-forms` flags

Python scripts (standard library; the checker `lemminflect` is a pip package used at development time only) that prepare
and ingest the verdicts of an adjudicator model for the audit of the generated form table.

<!-- markdownlint-disable MD013 -->

## When to use it

`WordSense.Builder audit-forms` draws a seeded, stratified sample of the generated inflected forms and compares it with the
checker LemmInflect (the method and the gate are in `docs/design/word-sense/word-data/form-auditor.md`). A sampled row that
the checker disagrees with must have a verdict in `tools/WordSense.Builder/audit/adjudications.tsv`; without one the gate
fails. After a change that regenerates the form table (`rebuild-forms`) the sample can draw rows that have no verdict yet:
this workflow gets them. The method of the one-time authoring of the forms (`corpus/lexicon/forms-authored.tsv`) is in
`data/PROVENANCE.md`; its scripts were removed.

## Inputs, commands and outputs

The work folder is outside the repository (the hidden keys must not be reachable by the adjudicating agents): `--home DIR`
or the environment variable `FORMS_AUDIT_HOME` (default `artifacts/forms-audit`, git-ignored, for a dry run only).

| Step | Command | Reads | Writes |
| ---- | ------- | ----- | ------ |
| 1 | `audit-forms --seed N --output <home>/audit/audit-N.md` for each seed (20240601, 99, 7, 2025, 31337) | the form table | the audit reports |
| 2 | `python export.py audit --home DIR` | `<home>/audit/audit-*.md`, `data/`, `adjudications.tsv` | `<home>/work/audit/batch-NN.json` (items with the lemma, part of speech, category, our form and the checker's forms; no stratum, no provenance), the hidden key `<home>/key/audit-key.tsv`, the list of the pending rows `<home>/reports/forms-authored-audit-pending.tsv` |
| 3 | an agent per batch, with `prompts/audit-adjudicator.md`, no tools and no repository | one batch file | `<home>/work/audit-out/batch-NN.tsv` (`id`, `valid` or `invalid`, `high`, `medium` or `low`, one line of evidence) |
| 4 | `python audit_ingest.py check --home DIR` | the answers and the key | validation and the calibration on the controls |
| 5 | `python audit_ingest.py final --home DIR` | the answers and the key | merges the `valid` verdicts of high or medium confidence into `adjudications.tsv`; lists the low-confidence ones in `<home>/work/audit-review.tsv` (a human decides) and the `invalid` ones in `<home>/work/audit-invalid.tsv` |

About 15% of the items are controls (spellings that the old rules gave wrongly, and shipped forms that the checker
confirms), drawn deterministically and listed only in the hidden key; they calibrate the verdicts (an invalid control judged
`valid` is a rubber stamp) and are never written to `adjudications.tsv`.

**Second opinion.** A first adjudicator can stamp known-wrong controls `valid`, so a medium-confidence `valid` verdict is not
trusted on its own. `python export.py recheck --home DIR` writes `<home>/work/audit/recheck-01.json` (the medium-confidence
`valid` rows, the rows that still have no verdict, the stamped controls and fresh controls; hidden key
`<home>/key/audit-recheck-key.tsv`) for a model of another family, which answers `<home>/work/audit-out/recheck-01.tsv`.
`python audit_ingest.py recheck --home DIR` keeps a row `valid` only when both adjudicators say `valid` with high or medium
confidence; any other row is treated as `invalid`: its line is removed from `adjudications.tsv` and it is listed in
`<home>/work/audit-recheck-invalid.tsv` (and `audit-recheck-rejected.tsv`, which `final` reads so that the first verdict is
never merged again); a row without a first verdict is added only with a high-confidence recheck verdict, a medium one goes to
`<home>/work/audit-recheck-review.tsv`.

**Invalid verdicts are corrected at the source, never by adopting the checker's form**: in the generator rule, in
`corpus/lexicon/form-corrections.tsv`, or, for a variant of an authored form, by removing the variant from
`corpus/lexicon/forms-authored.tsv` and noting the removal and the reason in the evidence of its row in
`corpus/lexicon/forms-authored-provenance.tsv`. The audit then no longer draws the row.

## Files

| File | Purpose |
| ---- | ------- |
| `export.py` | `audit` and `recheck`: writes the batches and the hidden keys |
| `audit_ingest.py` | `check`, `final` and `recheck`: validates the answers and merges the verdicts |
| `common.py` | Paths, the work folder, the readers of the data tables, the seeded hash of the controls |
| `prompts/audit-adjudicator.md` | The instructions of the adjudicator (the answers are dedicated to CC0 1.0 in the prompt) |

<!-- markdownlint-enable MD013 -->
