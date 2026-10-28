"""Adds lemmas and their groups to the curation (corpus/curation/senses.tsv, groups.tsv).

  python curate_new.py apply --lemmas FILE     appends the senses of the listed lemmas to senses.tsv
  python curate_new.py export --lemmas FILE    writes work/curate/in/*.json (the senses with their labels, for a label review)
  python curate_new.py groups FILE             appends reviewed groups to groups.tsv and marks their members in senses.tsv

FILE of --lemmas is a list of lemmas (one per line, or a TSV whose first column is the lemma, see qf.read_lemma_list).
A row of senses.tsv holds a sense of the data: the Open English WordNet sense with its gloss and tag count, or a project
sense (a sense without a sense key, which the dictionary lacks). The tier follows the WordNet 3.0 tag count: 5 or more C,
1 to 4 U, none R, and every project sense U. The label is the one of the data (`sense_label`); `apply` replaces it only
when work/curate/out/*.tsv (sense id, label) holds a reviewed one (a short slug of lower-case ASCII letters, digits and
single hyphens). A lemma that has rows in senses.tsv already is skipped, so `apply` can be rerun. After `apply` or
`groups`, run `seed-inventory`.

FILE of `groups` is a TSV with the columns of groups.tsv (`group_id` as lemma#slug, `lemma`, `part_of_speech`,
`member_sense_ids`, `kind`, `evidence`); a group that is in groups.tsv already is skipped.
"""
import argparse
import collections
import csv
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata
import qf

CURATION = os.path.join(lexdata.CORPUS, "curation", "senses.tsv")
GROUPS = os.path.join(lexdata.CORPUS, "curation", "groups.tsv")
WORK = os.path.join(qf.WORK, "curate")
SLUG = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
PROJECT_NOTE = "project sense: a use the dictionary lacks (listed by the data)"
BATCH_SENSES = 120
GROUP_KINDS = ("equivalent", "related")


def lemma_senses(lemmas):
    wanted = set(lemmas)
    senses = lexdata.load_senses()
    by = collections.defaultdict(list)
    for s in senses.values():
        if s["lemma"] in wanted:
            by[s["lemma"]].append(s)
    missing = [l for l in lemmas if l not in by]
    if missing:
        raise SystemExit(f"lemmas without senses in the data: {', '.join(missing[:10])}")
    for rows in by.values():
        rows.sort(key=lambda x: (x["pos"], -x["tag_count"], x["id"]))
    return by


def raw_ids(lemmas):
    """public id -> (oewn sense id, sense key, synset, raw pos) from the sense table."""
    wanted = set(lemmas)
    entries = {r["entry_id"]: r for r in lexdata.read_table("entry")}
    labels = {r["sense_id"]: r["label"] for r in lexdata.read_table("sense_label")}
    out = {}
    for r in lexdata.read_table("sense"):
        e = entries.get(r["entry_id"])
        if e and e["lemma"] in wanted and r["sense_id"] in labels:
            out[e["lemma"] + "#" + labels[r["sense_id"]]] = (r["sense_id"], r["sense_key"], r["synset_id"], e["pos"])
    return out


def cmd_export(args):
    lemmas = qf.read_lemma_list(args.lemmas)
    os.makedirs(os.path.join(WORK, "in"), exist_ok=True)
    os.makedirs(os.path.join(WORK, "out"), exist_ok=True)
    by = lemma_senses(lemmas)
    batches, cur, count = [], [], 0
    for lemma in lemmas:
        cur.append({"lemma": lemma, "senses": [{"id": s["id"], "pos": s["pos_name"], "tags": s["tag_count"],
                                                "gloss": s["gloss"][:200]} for s in by[lemma]]})
        count += len(by[lemma])
        if count >= BATCH_SENSES:
            batches.append(cur)
            cur, count = [], 0
    if cur:
        batches.append(cur)
    for i, items in enumerate(batches, 1):
        qf.write_json(os.path.join(WORK, "in", f"cu{i}.json"), {"batch": f"cu{i}", "lemmas": items})
    print(len(batches), "batches")


def read_reviewed_labels():
    labels = {}
    folder = os.path.join(WORK, "out")
    for name in sorted(os.listdir(folder)) if os.path.isdir(folder) else []:
        with open(os.path.join(folder, name), encoding="utf-8") as handle:
            for line in handle:
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 2:
                    labels[parts[0]] = parts[1]
    return labels


def cmd_apply(args):
    lemmas = qf.read_lemma_list(args.lemmas)
    by = lemma_senses(lemmas)
    ids = raw_ids(lemmas)
    labels = read_reviewed_labels()
    with open(CURATION, encoding="utf-8", newline="") as handle:
        existing = {r["sense_id"] for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)}
    forms = collections.defaultdict(set)
    wanted = set(lemmas)
    entry_lemma = {r["entry_id"]: r["lemma"] for r in lexdata.read_table("entry") if r["lemma"] in wanted}
    for r in lexdata.read_table("form"):
        lemma = entry_lemma.get(r["entry_id"]) or (r["lemma"] if r["lemma"] in wanted and not r["entry_id"] else None)
        if lemma:
            forms[lemma].add(r["spelling"])
    rows = []
    for lemma in lemmas:
        if any(e.startswith(lemma + "#") for e in existing):
            continue  # already curated: the labels of the data are the curated ones
        planned = [(s, labels.get(s["id"], s["id"].split("#", 1)[1])) for s in by[lemma]]
        used = set()
        for _s, label in planned:
            if not SLUG.match(label):
                raise SystemExit(f"bad label {label} for {lemma}")
            if label in used:
                raise SystemExit(f"duplicate label {lemma}#{label}")
            used.add(label)
        for s, label in planned:
            pid = f"{lemma}#{label}"
            old = ids[s["id"]]
            tag = s["tag_count"]
            if not old[1]:  # no sense key: a project sense
                rows.append([pid, "", "", "", s["pos"], s["pos_name"], s["gloss"], "", "U", PROJECT_NOTE, "project", "", "", "",
                             ",".join(sorted(forms[lemma]))])
                continue
            tier = "C" if tag >= 5 else ("U" if tag >= 1 else "R")
            rows.append([pid, old[0], old[1], old[2], old[3] if old[3] != "s" else "a", s["pos_name"], s["gloss"], str(tag),
                         tier, "wn30-tag-count", "oewn", "", "", "", ",".join(sorted(forms[lemma]))])
    with open(CURATION, "a", encoding="utf-8", newline="") as handle:
        for r in rows:
            handle.write("\t".join(x.replace("\t", " ").replace("\n", " ") for x in r) + "\n")
    print(len(rows), "rows appended")


def cmd_groups(args):
    with open(args.file, encoding="utf-8", newline="") as handle:
        proposed = list(csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE))
    with open(CURATION, encoding="utf-8", newline="") as handle:
        lines = handle.read().split("\n")
    header = lines[0].split("\t")
    col = {name: i for i, name in enumerate(header)}
    by_id = {}
    for i, line in enumerate(lines[1:], 1):
        if line:
            by_id[line.split("\t", 1)[0]] = i
    with open(GROUPS, encoding="utf-8", newline="") as handle:
        group_header = handle.readline().rstrip("\n").split("\t")
        known = {line.split("\t", 1)[0] for line in handle}
    out = []
    for g in proposed:
        gid, members = g["group_id"], g["member_sense_ids"].split(",")
        if gid in known:
            continue
        if g["kind"] not in GROUP_KINDS or not g["evidence"].strip():
            raise SystemExit(f"group {gid}: kind must be one of {GROUP_KINDS} and the evidence must not be empty")
        if not SLUG.match(gid.split("#", 1)[1]) or len(members) < 2:
            raise SystemExit(f"group {gid}: needs a slug and two or more members")
        glosses = []
        for m in members:
            if m not in by_id:
                raise SystemExit(f"group {gid}: {m} is not a sense of senses.tsv")
            cells = lines[by_id[m]].split("\t")
            glosses.append(cells[col["gloss"]][:80])
            cells[col["group_id"]], cells[col["group_reason"]] = gid, g["evidence"]
            lines[by_id[m]] = "\t".join(cells)
        row = {**g, "gloss": " | ".join(glosses)}
        out.append("\t".join(row.get(name, "") for name in group_header))
    with open(CURATION, "w", encoding="utf-8", newline="") as handle:
        handle.write("\n".join(lines))
    with open(GROUPS, "a", encoding="utf-8", newline="") as handle:
        for line in out:
            handle.write(line + "\n")
    print(len(out), "groups appended")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("export", "apply"):
        p = sub.add_parser(name)
        p.add_argument("--lemmas", required=True, help="a file of lemmas (a list, or a TSV with the lemma first)")
    sub.add_parser("groups").add_argument("file")
    a = ap.parse_args()
    {"export": cmd_export, "apply": cmd_apply, "groups": cmd_groups}[a.cmd](a)
