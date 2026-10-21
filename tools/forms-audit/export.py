"""Writes the batches of the audit verdicts for the adjudicator (the format mirrors tools/quota-fill/written.py: JSON in,
TSV out).

  python export.py audit [--batch-size 100]     work/audit/batch-NN.json: the audit rows that wait for a verdict
  python export.py recheck                      work/audit/recheck-01.json: second opinion on the medium-confidence valid verdicts

`audit-forms` fails its gate while a sampled row that the checker (LemmInflect) disagrees with has no verdict in
`tools/WordSense.Builder/audit/adjudications.tsv`. The adjudicator (a language model, `prompts/audit-adjudicator.md`) is
shown our form and the checker's forms for each such row, mixed with controls (known-invalid spellings and checker-confirmed
forms, about 15% of the items) that calibrate the verdicts; the controls are listed in a hidden key and never written to the
adjudications.

The agents get no tool beyond reading their batch and writing their answer file, and no repository access: the batch is
all they know. The batch files hold only the lemma, the part of speech name, the category, our form and the checker's
forms; the hidden key stays outside the repository.
"""
import argparse
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common


def write_batch(path, batch_id, role, items, key):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write('{"batch": ' + json.dumps(batch_id) + ', "role": ' + json.dumps(role)
                     + ', "output": ' + json.dumps(os.path.basename(path)[:-5] + ".tsv") + ', "' + key + '": [\n')
        handle.write(",\n".join(json.dumps(i, ensure_ascii=True) for i in items))
        handle.write("\n]}\n")


AUDIT_TAGS = {"plural": ["NNS"], "third": ["VBZ"], "gerund": ["VBG"], "past": ["VBD"], "participle": ["VBN"],
              "comparative": ["JJR", "RBR"], "superlative": ["JJS", "RBS"]}
# Spellings that the rules of the builder gave wrongly before the D4 fixes: known-invalid controls of the audit batch
AUDIT_INVALID_CONTROLS = [
    ("bagmens", "bagman", "n", "Number=Plur"), ("icewomans", "icewoman", "n", "Number=Plur"),
    ("obsequys", "obsequy", "n", "Number=Plur"), ("superheros", "superhero", "n", "Number=Plur"),
    ("birdiing", "birdie", "v", "VerbForm=Ger"), ("wiretaped", "wiretap", "v", "Tense=Past+VerbForm=Part"),
    ("chairmaned", "chairman", "v", "Tense=Past+VerbForm=Part"), ("hertzes", "hertz", "n", "Number=Plur"),
    ("prostatitises", "prostatitis", "n", "Number=Plur"), ("newses", "news", "n", "Number=Plur"),
    ("germen", "german", "n", "Number=Plur"), ("upsetted", "upset", "v", "Tense=Past+VerbForm=Part")]


def oracle_forms(lemma, pos, category):
    """The forms that LemmInflect (MIT, dev-time checker) gives for a lemma, part of speech and category."""
    from lemminflect import getInflection
    forms = []
    for tag in AUDIT_TAGS[category]:
        if (tag.startswith("RB")) != (pos == "r"):
            continue
        try:
            for f in getInflection(lemma.lower(), tag=tag):
                if f.lower() not in [x.lower() for x in forms]:
                    forms.append(f)
        except Exception:
            pass
    return forms


def read_audit_errors(folder):
    """Distinct unadjudicated error rows of the audit-SEED.md files: (spelling, lemma, pos, features) -> (strata, seeds)."""
    import glob
    import re
    rows = {}
    for path in sorted(glob.glob(os.path.join(folder, "audit-*.md"))):
        seed = re.search(r"audit-(\d+)\.md$", path).group(1)
        inside = False
        for line in open(path, encoding="utf-8").read().split("\n"):
            if line.startswith("```"):
                inside = not inside
                continue
            parts = line.split("\t")
            if inside and len(parts) == 6 and parts[5] == "unadjudicated":
                entry = rows.setdefault((parts[1], parts[2], parts[3], parts[4]), (set(), []))
                entry[0].add(parts[0])
                entry[1].append(seed)
    return rows


def cmd_audit(args):
    """Exports the audit rows that wait for a verdict as batches for the adjudicator (work/audit/batch-NN.json).

    An item shows the spelling that the form table ships (ours), the lemma, the part of speech and the category, and the forms
    that the checker LemmInflect gives; it does not show the provenance or the stratum. About 15% of the items are controls
    (checker-confirmed shipped forms and spellings that the old rules gave wrongly), drawn deterministically and listed in the
    hidden key `<home>/key/audit-key.tsv`; they calibrate the verdicts and are never written to adjudications.tsv."""
    home = common.home_dir(args.home)
    rows = read_audit_errors(args.audit_dir or os.path.join(home, "audit"))
    entries = common.read_entries(common.DATA)
    pending = [{"spelling": s, "lemma": l, "pos": p, "features": f, "kind": "pending"} for (s, l, p, f) in sorted(rows)]
    n_controls = round(len(pending) * 0.15 / 0.85)
    n_invalid = min(len(AUDIT_INVALID_CONTROLS), n_controls // 2)
    n_valid = n_controls - n_invalid
    controls = [{"spelling": s, "lemma": l, "pos": p, "features": f, "kind": "control-invalid"}
                for (s, l, p, f) in sorted(AUDIT_INVALID_CONTROLS, key=lambda r: common.stable_hash("audit-invalid", r[0]))[:n_invalid]]
    # known-valid controls: shipped forms that the checker confirms, in the mix of parts of speech of the pending rows
    taken = {(c["spelling"], c["lemma"], c["pos"]) for c in pending}
    mix = collections.Counter(p["pos"] for p in pending)
    candidates = []
    for spelling, key, features, prov in common.read_form_table(common.DATA):
        kinds = common.feature_kinds(features)
        lemma, pos = common.lemma_pos_of(key, entries)
        if (kinds and prov in ("rule", "llm", "oewn_form") and lemma.isalpha() and lemma.islower() and spelling.islower()
                and pos in common.CATEGORIES and spelling != lemma):
            candidates.append((common.stable_hash("audit-valid", spelling, lemma, pos), spelling, lemma, pos, features, kinds))
    candidates.sort()
    quota = {p: round(n_valid * n / len(pending)) for p, n in mix.items()}
    while sum(quota.values()) < n_valid:
        quota[max(mix, key=mix.get)] += 1
    while sum(quota.values()) > n_valid:
        quota[max(quota, key=quota.get)] -= 1
    for _h, spelling, lemma, pos, features, kinds in candidates:
        if quota.get(pos, 0) <= 0 or (spelling, lemma, pos) in taken:
            continue
        if all(spelling.lower() in [f.lower() for f in oracle_forms(lemma, pos, k)] for k in kinds):
            controls.append({"spelling": spelling, "lemma": lemma, "pos": pos, "features": features, "kind": "control-valid"})
            taken.add((spelling, lemma, pos))
            quota[pos] -= 1
        if sum(quota.values()) <= 0:
            break
    items = sorted(pending + controls, key=lambda i: common.stable_hash("audit-order", i["spelling"], i["lemma"], i["pos"]))
    batch_items, key_rows = [], []
    for number, item in enumerate(items, 1):
        item_id = f"R{number:04d}"
        kinds = common.feature_kinds(item["features"])
        oracle = []
        for k in kinds:
            for f in oracle_forms(item["lemma"], item["pos"], k):
                if f not in oracle:
                    oracle.append(f)
        batch_items.append({"id": item_id, "lemma": item["lemma"], "pos": common.POS_NAME[item["pos"]],
                            "category": " and ".join(kinds), "ours": item["spelling"], "checker": oracle})
        strata, seeds = rows.get((item["spelling"], item["lemma"], item["pos"], item["features"]), (set(), []))
        key_rows.append([item_id, item["kind"], item["spelling"], item["lemma"], item["pos"], item["features"],
                         " ".join(sorted(strata)) or "-", " ".join(sorted(set(seeds))) or "-"])
    out = common.work_dir(home, "audit")
    for old in common.batch_files(out):
        os.remove(old)
    count = 0
    for start in range(0, len(batch_items), args.batch_size):
        count += 1
        write_batch(os.path.join(out, f"batch-{count:02d}.json"), f"r{count:02d}", "audit-adjudicator",
                    batch_items[start:start + args.batch_size], "items")
    common.write_tsv(os.path.join(home, "key", "audit-key.tsv"),
                     ["id", "kind", "spelling", "lemma", "pos", "features", "strata", "seeds"], key_rows,
                     ["HIDDEN KEY of the audit batches: never show to an agent."])
    pending_file = args.pending_report or common.report_path(home, "forms-authored-audit-pending.tsv")
    report = [[sorted(rows[k][0])[0]] + list(k) + [" ".join(sorted(set(rows[k][1])))]
              for k in sorted(rows, key=lambda k: (sorted(rows[k][0])[0], k))]
    common.write_tsv(pending_file, ["stratum", "spelling", "lemma", "pos", "features", "seeds"], report,
                     ["Rows that the five audit samples (seeds 20240601, 99, 7, 2025, 31337) of the rebuilt form table drew and that the "
                      "LemmInflect checker disagrees with,",
                      "and that have no verdict in tools/WordSense.Builder/audit/adjudications.tsv yet. Each needs a verdict (valid or "
                      "invalid) that names our own source;",
                      "none is decided here. Until they are adjudicated, audit-forms fails the gate. A work output, not committed."])
    print(f"{len(pending)} audit rows + {len(controls)} controls ({n_invalid} invalid, {len(controls) - n_invalid} valid) = {len(items)} items, "
          f"{count} batches in {out}")
    print("pending rows by stratum:", dict(collections.Counter(sorted(rows[k][0])[0] for k in rows)))


AUDIT_RECHECK_INVALID_CONTROLS = [
    ("dairywomens", "dairywoman", "n", "Number=Plur"), ("policemans", "policeman", "n", "Number=Plur"),
    ("ventriloquys", "ventriloquy", "n", "Number=Plur"), ("bufferred", "buffer", "v", "Tense=Past+VerbForm=Part"),
    ("suntaned", "suntan", "v", "Tense=Past+VerbForm=Part"), ("hogtiing", "hogtie", "v", "VerbForm=Ger")]


def cmd_recheck(args):
    """Exports the recheck batch (work/audit/recheck-01.json): the rows whose first verdict was `valid` with medium confidence,
    the audit rows that still have no verdict, the controls that the first adjudicator stamped `valid` although they are
    wrong, and fresh controls (spellings that the old rules gave wrongly, forms that the checker confirms). The existing audit
    batches and their key are not touched; the key of this batch is `<home>/key/audit-recheck-key.tsv`."""
    import audit_ingest
    home = common.home_dir(args.home)
    key = audit_ingest.read_key(home)
    answers = audit_ingest.parse(home)[0]
    entries = common.read_entries(common.DATA)
    items = []
    used = {(k["spelling"], k["lemma"], k["pos"]) for k in key.values()}
    for item_id, (verdict, confidence, _evidence) in sorted(answers.items()):
        k = key[item_id]
        if k["kind"] == "pending" and verdict == "valid" and confidence == "medium":
            items.append(("medium-valid", k, item_id))
        elif k["kind"] == "control-invalid" and verdict == "valid":
            items.append(("control-stamped", k, item_id))
    adjudicated = set()
    for line in open(audit_ingest.ADJUDICATIONS, encoding="utf-8").read().split("\n"):
        if line.strip() and not line.startswith("#"):
            c = line.split("\t")
            adjudicated.add((c[0], c[1], c[2], c[5] if len(c) > 5 else ""))
    listed = {(k["spelling"], k["lemma"], k["pos"]) for _t, k, _i in items}
    for (spelling, lemma, pos, features), (strata, _seeds) in sorted(
            read_audit_errors(args.audit_dir or os.path.join(home, "audit")).items()):
        if (spelling, lemma, pos, features) not in adjudicated and (spelling, lemma, pos) not in listed:
            items.append(("new-pending", {"spelling": spelling, "lemma": lemma, "pos": pos, "features": features,
                                          "strata": " ".join(sorted(strata))}, "-"))
    for spelling, lemma, pos, features in AUDIT_RECHECK_INVALID_CONTROLS:
        items.append(("control-invalid", {"spelling": spelling, "lemma": lemma, "pos": pos, "features": features, "strata": "-"}, "-"))
        used.add((spelling, lemma, pos))
    candidates = []
    for spelling, row_key, features, prov in common.read_form_table(common.DATA):
        kinds = common.feature_kinds(features)
        lemma, pos = common.lemma_pos_of(row_key, entries)
        if (kinds and prov in ("rule", "llm", "oewn_form") and lemma.isalpha() and lemma.islower() and spelling.islower()
                and pos in common.CATEGORIES and spelling != lemma and (spelling, lemma, pos) not in used):
            candidates.append((common.stable_hash("recheck-valid", spelling, lemma, pos), spelling, lemma, pos, features, kinds))
    candidates.sort()
    quota = {"n": 3, "v": 2, "a": 1}
    taken = 0
    for _h, spelling, lemma, pos, features, kinds in candidates:
        if quota.get(pos, 0) <= 0:
            continue
        if all(spelling.lower() in [f.lower() for f in oracle_forms(lemma, pos, k)] for k in kinds):
            items.append(("control-valid", {"spelling": spelling, "lemma": lemma, "pos": pos, "features": features, "strata": "-"}, "-"))
            quota[pos] -= 1
            taken += 1
        if taken == 6:
            break
    items.sort(key=lambda t: common.stable_hash("recheck-order", t[1]["spelling"], t[1]["lemma"], t[1]["pos"]))
    batch_items, key_rows = [], []
    for number, (kind, k, first_id) in enumerate(items, 1):
        item_id = f"Q{number:04d}"
        kinds = common.feature_kinds(k["features"])
        checker = []
        for kind_name in kinds:
            for f in oracle_forms(k["lemma"], k["pos"], kind_name):
                if f not in checker:
                    checker.append(f)
        batch_items.append({"id": item_id, "lemma": k["lemma"], "pos": common.POS_NAME[k["pos"]], "category": " and ".join(kinds),
                            "ours": k["spelling"], "checker": checker})
        first = answers.get(first_id)
        key_rows.append([item_id, kind, k["spelling"], k["lemma"], k["pos"], k["features"], k.get("strata", "-") or "-", first_id,
                         first[0] if first else "-", first[1] if first else "-"])
    out = common.work_dir(home, "audit")
    path = os.path.join(out, "recheck-01.json")
    if os.path.exists(path) and not args.force:
        raise SystemExit(f"{path} exists: use --force to replace it")
    write_batch(path, "q01", "audit-adjudicator", batch_items, "items")
    common.write_tsv(os.path.join(home, "key", "audit-recheck-key.tsv"),
                     ["id", "kind", "spelling", "lemma", "pos", "features", "strata", "first_id", "first_verdict", "first_confidence"],
                     key_rows, ["HIDDEN KEY of the recheck batch: never show to an agent."])
    print(f"{len(batch_items)} items in {path}:", dict(collections.Counter(k for k, _a, _b in items)))


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["audit", "recheck"])
    common.add_home_argument(ap)
    ap.add_argument("--audit-dir", default=None, help="folder with the audit-SEED.md files (default <home>/audit)")
    ap.add_argument("--pending-report", default=None,
                    help="audit: write the list of the pending rows to this TSV file (default <home>/reports/forms-authored-audit-pending.tsv)")
    ap.add_argument("--batch-size", type=int, default=common.BATCH_TASKS)
    ap.add_argument("--force", action="store_true", help="recheck: replace an existing recheck batch")
    a = ap.parse_args()
    if a.command == "audit":
        cmd_audit(a)
    else:
        cmd_recheck(a)
