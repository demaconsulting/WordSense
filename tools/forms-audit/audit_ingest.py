"""Ingests the verdicts of the audit adjudicator into `tools/WordSense.Builder/audit/adjudications.tsv`.

  python audit_ingest.py check [--home DIR]            validate work/audit-out/batch-NN.tsv and report the calibration
  python audit_ingest.py final [--home DIR] [--adjudications FILE]
                                                       merge the verdicts of the pending rows into the adjudications file
  python audit_ingest.py recheck [--home DIR] [--adjudications FILE] [--dry-run]
                                                       apply the second adjudicator (work/audit-out/recheck-01.tsv, batch
                                                       work/audit/recheck-01.json from `export.py recheck`)

The batches come from `export.py audit` (items R0001..; the hidden key `<home>/key/audit-key.tsv` maps an id to the shipped
spelling, the lemma, the part of speech and the features, and tells the controls from the pending rows). An answer line has
four tab-separated columns: id, verdict (`valid` or `invalid`), confidence (`high`, `medium`, `low`) and one line of evidence
(1 to 160 characters, no tab). Hard checks: the id belongs to the batch and appears once, the verdict and the confidence are in
the vocabulary, the evidence has a plausible length. A line that fails is rejected (it counts as no answer).

Calibration. The controls (known-invalid spellings that the old rules gave, and shipped forms that the checker confirms) are
never written to the adjudications; `check` prints how many the adjudicator got right (an invalid control judged `valid` is a
rubber stamp).

`final` writes, for every pending row:
  valid, confidence high              a row of adjudications.tsv (spelling, lemma, pos, valid, evidence, features)
  valid, confidence medium            a row only when the recheck (a second model) has answered the same row `valid` with high or
                                      medium confidence; without an answer, or when it disagrees, nothing is written (fail closed:
                                      no answer is listed in work/audit-review.tsv, a disagreement in work/audit-invalid.tsv)
  valid, confidence low               not written: listed in work/audit-review.tsv for a human decision
  invalid                             not written: listed in work/audit-invalid.tsv, a real error that is corrected in the generator
                                      or in corpus/lexicon/form-corrections.tsv (the audit then no longer draws the row)
The evidence of every row that `final` or `recheck` writes ends with the models and the confidences that decided it, for example
`[gpt-6.1-sol high]` or `[gpt-6.1-sol medium + claude-sonnet-5.5 recheck medium]` (the models are named with --first-model and
--recheck-model). An answer whose evidence names a retired source (AGID) is rejected at ingest: the evidence must name our own
sources. The rows of the adjudications file that exist already are kept; a row for the same spelling, lemma, part of speech and
features is replaced.
"""
import argparse
import collections
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

CONFIDENCE = ("high", "medium", "low")
ADJUDICATIONS = os.path.join(common.REPO, "tools", "WordSense.Builder", "audit", "adjudications.tsv")
VERDICTS = ("valid", "invalid")
FIRST_MODEL = "gpt-6.1-sol"
RECHECK_MODEL = "claude-sonnet-5.5"
# Sources that were removed from the project: evidence must not cite them
RETIRED_SOURCES = re.compile(r"\bagid\b|automatically generated inflection database", re.IGNORECASE)


def read_key(home):
    return {r["id"]: r for r in common.read_tsv(os.path.join(home, "key", "audit-key.tsv"))}


def read_answer_file(path, stem, expected, answers, rejects):
    """Reads one answer file of four tab-separated columns into answers (id -> verdict, confidence, evidence) and rejects."""
    for number, line in enumerate(open(path, encoding="utf-8").read().split("\n"), 1):
        line = line.rstrip("\r")
        if not line.strip():
            continue
        cols = line.split("\t")
        if len(cols) != 4:
            rejects.append((stem, number, "columns", line))
            continue
        item, verdict, confidence, evidence = (c.strip() for c in cols)
        if item not in expected:
            rejects.append((stem, number, "unknown id", line))
        elif item in answers:
            rejects.append((stem, number, "duplicate", line))
        elif verdict not in VERDICTS:
            rejects.append((stem, number, "verdict", line))
        elif confidence not in CONFIDENCE:
            rejects.append((stem, number, "confidence", line))
        elif not 1 <= len(evidence) <= 200:
            rejects.append((stem, number, "evidence length", line))
        elif RETIRED_SOURCES.search(evidence):
            rejects.append((stem, number, "retired source", line))
        else:
            answers[item] = (verdict, confidence, evidence)


def parse(home):
    """Returns (answers, rejects, expected ids, missing ids); answers maps an id to (verdict, confidence, evidence)."""
    folder = os.path.join(home, "work", "audit")
    out_folder = os.path.join(home, "work", "audit-out")
    answers, rejects, expected_all = {}, [], set()
    for _batch_id, stem, expected in read_batches(folder):
        expected_all |= expected
        path = os.path.join(out_folder, stem + ".tsv")
        if os.path.exists(path):
            read_answer_file(path, stem, expected, answers, rejects)
    return answers, rejects, expected_all, sorted(expected_all - set(answers))


def read_recheck(home):
    """The recheck batch: (key rows by id, answers, rejects, missing ids). The answers file may not exist yet."""
    import json
    key = {r["id"]: r for r in common.read_tsv(os.path.join(home, "key", "audit-recheck-key.tsv"))}
    with open(os.path.join(home, "work", "audit", "recheck-01.json"), encoding="utf-8") as handle:
        expected = {i["id"] for i in json.load(handle)["items"]}
    answers, rejects = {}, []
    path = os.path.join(home, "work", "audit-out", "recheck-01.tsv")
    if os.path.exists(path):
        read_answer_file(path, "recheck-01", expected, answers, rejects)
    return key, answers, rejects, sorted(expected - set(answers))


def recheck_rejected(home):
    """The (spelling, lemma, pos, features) rows that the recheck took back; `final` never merges them again."""
    path = os.path.join(home, "work", "audit-recheck-rejected.tsv")
    if not os.path.exists(path):
        return set()
    return {(r["spelling"], r["lemma"], r["pos"], r["features"]) for r in common.read_tsv(path)}


def recheck_verdicts(home):
    """The answers of the recheck by (spelling, lemma, pos, features): (verdict, confidence). Empty when no recheck was answered."""
    if not os.path.exists(os.path.join(home, "work", "audit", "recheck-01.json")):
        return {}
    key, answers, _rejects, _missing = read_recheck(home)
    return {(key[i]["spelling"], key[i]["lemma"], key[i]["pos"], key[i]["features"]): (v, c) for i, (v, c, _e) in answers.items()}


def with_models(evidence, models):
    """The evidence with the models and the confidences that decided it, e.g. `... [gpt-6.1-sol high]`."""
    return f"{evidence} [{models}]"


def read_batches(folder):
    import json
    result = []
    for path in common.batch_files(folder):
        with open(path, encoding="utf-8") as handle:
            spec = json.load(handle)
        result.append((spec["batch"], os.path.basename(path)[:-5], {i["id"] for i in spec["items"]}))
    return result


def cmd_check(args):
    home = common.home_dir(args.home)
    key = read_key(home)
    answers, rejects, expected, missing = parse(home)
    common.write_tsv(os.path.join(common.work_dir(home), "rejected-audit.tsv"), ["batch", "line", "reason", "text"], rejects)
    print(f"audit: {len(answers)} of {len(expected)} answers valid, {len(missing)} missing, {len(rejects)} rejected "
          f"{dict(collections.Counter(r[2] for r in rejects))}")
    table = collections.Counter()
    for item, (verdict, confidence, _e) in answers.items():
        table[(key[item]["kind"], verdict, confidence)] += 1
    for k, v in sorted(table.items()):
        print("  ", k, v)
    wrong_invalid = sum(v for (kind, verdict, _c), v in table.items() if kind == "control-invalid" and verdict == "valid")
    wrong_valid = sum(v for (kind, verdict, _c), v in table.items() if kind == "control-valid" and verdict == "invalid")
    print(f"calibration: invalid controls judged valid {wrong_invalid}, valid controls judged invalid {wrong_valid}")


def cmd_final(args):
    home = common.home_dir(args.home)
    key = read_key(home)
    answers, _rejects, _expected, missing = parse(home)
    if missing:
        print(f"{len(missing)} items have no answer yet; they stay unadjudicated")
    new_rows, review, invalid = {}, [], []
    taken_back = recheck_rejected(home)
    second = recheck_verdicts(home)
    for item, (verdict, confidence, evidence) in sorted(answers.items()):
        k = key[item]
        if k["kind"] != "pending":
            continue
        identity = (k["spelling"], k["lemma"], k["pos"], k["features"])
        if identity in taken_back:
            continue
        row = [k["spelling"], k["lemma"], k["pos"], "valid", with_models(evidence, f"{args.first_model} {confidence}"), k["features"]]
        listed = [item, k["strata"], k["spelling"], k["lemma"], k["pos"], k["features"], confidence, evidence]
        if verdict == "invalid":
            invalid.append(listed)
        elif confidence == "low":
            review.append(listed)
        elif confidence == "medium":
            # A medium-confidence verdict is merged only when the second model answered the same row and agrees (fail closed)
            answer = second.get(identity)
            if answer is None:
                review.append(listed)
            elif answer[0] == "valid" and answer[1] in ("high", "medium"):
                row[4] = with_models(evidence, f"{args.first_model} medium + {args.recheck_model} recheck {answer[1]}")
                new_rows[identity] = row
            else:
                invalid.append(listed)
        else:
            new_rows[identity] = row
    path = args.adjudications or ADJUDICATIONS
    comments, rows = [], {}
    for line in open(path, encoding="utf-8").read().split("\n"):
        if line.startswith("#"):
            comments.append(line)
        elif line.strip():
            c = line.split("\t")
            rows[(c[0], c[1], c[2], c[5] if len(c) > 5 else "")] = c
    kept_before = len(rows)
    rows.update(new_rows)
    ordered = sorted(rows.values(), key=lambda c: (c[0].lower(), c[0], c[1], c[2], c[5] if len(c) > 5 else ""))
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        for c in comments:
            handle.write(c + "\n")
        for c in ordered:
            handle.write("\t".join(c) + "\n")
    names = ["item", "strata", "spelling", "lemma", "pos", "features", "confidence", "evidence"]
    common.write_tsv(os.path.join(common.work_dir(home), "audit-review.tsv"), names, review)
    common.write_tsv(os.path.join(common.work_dir(home), "audit-invalid.tsv"), names, invalid)
    print(f"adjudications: {kept_before} rows before, {len(new_rows)} valid verdicts merged, {len(rows)} rows now; "
          f"{len(review)} low-confidence valid verdicts to review (work/audit-review.tsv); "
          f"{len(invalid)} invalid verdicts to correct (work/audit-invalid.tsv)")


def cmd_recheck(args):
    """Applies the recheck verdicts (a second adjudicator, another model family) to the first-round verdicts.

    A row stays `valid` only when both the first adjudicator and the recheck say `valid` with high or medium confidence;
    otherwise it is treated as `invalid`: its line is removed from the adjudications file, it is listed in
    work/audit-recheck-invalid.tsv (to be corrected at its source: the generator rule, corpus/lexicon/form-corrections.tsv, or
    the variant list of corpus/lexicon/forms-authored.tsv with the removal noted in the evidence of its sidecar row) and
    in work/audit-recheck-rejected.tsv, which `final` reads so that the first-round verdict is never merged again. A row that
    had no first verdict (it was drawn by a later audit run) is added as `valid` only with a high-confidence recheck verdict; a
    medium one goes to work/audit-recheck-review.tsv for a human decision, an invalid or low one to the invalid list."""
    home = common.home_dir(args.home)
    key, answers, rejects, missing = read_recheck(home)
    common.write_tsv(os.path.join(common.work_dir(home), "rejected-recheck.tsv"), ["batch", "line", "reason", "text"], rejects)
    print(f"recheck: {len(answers)} of {len(key)} answers valid, {len(missing)} missing, {len(rejects)} rejected "
          f"{dict(collections.Counter(r[2] for r in rejects))}")
    table = collections.Counter((key[i]["kind"], v, c) for i, (v, c, _e) in answers.items())
    for k, v in sorted(table.items()):
        print("  ", k, v)
    stamped = sum(v for (kind, verdict, _c), v in table.items() if kind in ("control-invalid", "control-stamped") and verdict == "valid")
    missed = sum(v for (kind, verdict, _c), v in table.items() if kind == "control-valid" and verdict == "invalid")
    print(f"calibration: wrong spellings judged valid {stamped} (of "
          f"{sum(1 for k in key.values() if k['kind'] in ('control-invalid', 'control-stamped'))}), "
          f"valid controls judged invalid {missed}")
    if missing:
        print("missing answers: the recheck is not applied")
        return
    names = ["item", "kind", "spelling", "lemma", "pos", "features", "first_confidence", "recheck_verdict", "recheck_confidence",
             "evidence"]
    invalid, review, rejected, keep = [], [], [], []
    for item, (verdict, confidence, evidence) in sorted(answers.items()):
        k = key[item]
        row = [item, k["kind"], k["spelling"], k["lemma"], k["pos"], k["features"], k["first_confidence"], verdict, confidence, evidence]
        identity = (k["spelling"], k["lemma"], k["pos"], k["features"])
        if k["kind"] == "medium-valid":
            if verdict == "valid" and confidence in ("high", "medium"):
                keep.append(row)
            else:
                invalid.append(row)
                rejected.append(list(identity))
        elif k["kind"] == "new-pending":
            if verdict == "valid" and confidence == "high":
                keep.append(row)
            elif verdict == "valid" and confidence == "medium":
                review.append(row)
            else:
                invalid.append(row)
                rejected.append(list(identity))
    work = common.work_dir(home)
    common.write_tsv(os.path.join(work, "audit-recheck-invalid.tsv"), names, invalid)
    common.write_tsv(os.path.join(work, "audit-recheck-review.tsv"), names, review)
    common.write_tsv(os.path.join(work, "audit-recheck-rejected.tsv"), ["spelling", "lemma", "pos", "features"], rejected)
    path = args.adjudications or ADJUDICATIONS
    comments, rows = [], {}
    for line in open(path, encoding="utf-8").read().split("\n"):
        if line.startswith("#"):
            comments.append(line)
        elif line.strip():
            c = line.split("\t")
            rows[(c[0], c[1], c[2], c[5] if len(c) > 5 else "")] = c
    removed = [tuple(r) for r in rejected if tuple(r) in rows]
    added = 0
    for row in keep:
        if row[1] == "new-pending":
            rows[(row[2], row[3], row[4], row[5])] = [row[2], row[3], row[4], "valid", with_models(row[9], f"{args.recheck_model} recheck high"), row[5]]
            added += 1
    for identity in removed:
        del rows[identity]
    if not args.dry_run:
        ordered = sorted(rows.values(), key=lambda c: (c[0].lower(), c[0], c[1], c[2], c[5] if len(c) > 5 else ""))
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            for c in comments:
                handle.write(c + "\n")
            for c in ordered:
                handle.write("\t".join(c) + "\n")
    print(f"{'dry run: ' if args.dry_run else ''}medium-valid rows kept {sum(1 for r in keep if r[1] == 'medium-valid')}, "
          f"taken back {sum(1 for r in invalid if r[1] == 'medium-valid')}; new rows: {added} added as valid, "
          f"{len(review)} for review, {sum(1 for r in invalid if r[1] == 'new-pending')} invalid; "
          f"{len(removed)} lines removed from the adjudications ({len(rows)} rows now)")
    print("to correct at the source (work/audit-recheck-invalid.tsv):", " ".join(r[2] for r in invalid))


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("check", "final", "recheck"):
        sp = sub.add_parser(name)
        common.add_home_argument(sp)
        if name in ("final", "recheck"):
            sp.add_argument("--adjudications", default=None, help="file to update (default the audit adjudications of the builder)")
            sp.add_argument("--first-model", default=FIRST_MODEL, help="the first adjudicator, recorded in the evidence (default %(default)s)")
            sp.add_argument("--recheck-model", default=RECHECK_MODEL, help="the second adjudicator, recorded in the evidence (default %(default)s)")
        if name == "recheck":
            sp.add_argument("--dry-run", action="store_true", help="report only; do not change the adjudications file")
    a = ap.parse_args()
    {"check": cmd_check, "final": cmd_final, "recheck": cmd_recheck}[a.cmd](a)
