"""Adds lemmas to the curation (corpus/curation/senses.tsv): the technical lemmas of wave 3.

  python curate_new.py export     writes work/curate/in/*.json (the senses of the lemmas, with their generated labels)
  python curate_new.py apply      reads work/curate/out/*.tsv (sense id, label) and appends the rows to senses.tsv

A row of the curation holds a sense of the data (the Open English WordNet sense, its gloss and tag count) or a project
sense (a use the dictionary lacks, listed in PROJECT below). The tier follows the WordNet 3.0 tag count: 5 or more C,
1 to 4 U, none R. The label is the one a gpt-5.6-luna pass reviewed (a short slug of lower-case ASCII letters, digits and
single hyphens); the generated label stays when the pass gave none. After `apply`, run `seed-inventory`.
"""
import collections
import csv
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata
import qf

LEMMAS = "mount bearing bleed torque run position support install remove attach inspect clean".split()
# (lemma, part of speech name, label, gloss, tier note): senses the dictionary lacks; the seeder adds them to the entry
PROJECT = [
    ("mount", "Noun", "mounting-fixture",
     "a fixture, bracket, or support that holds a component or piece of equipment in place (an engine mount, a motor mount)",
     "project sense: found by the technical-writing probe (mount as a fixture)"),
    ("arm", "Verb", "prime-system",
     "make a device or system ready for operation or activation (to arm the alarm, to arm the fire suppression system)",
     "project sense: found by the technical-writing probe (arm a system)"),
    ("lead", "Noun", "electrical-lead",
     "an electrical conductor or wire with a terminal that connects a component, instrument, or circuit (a meter lead, a test lead)",
     "project sense: found by the technical-writing probe (lead as a conductor)"),
    ("flush", "Noun", "fluid-flush",
     "a procedure that cleans or clears a system by passing liquid through it (a cooling-system flush)",
     "project sense: found by the technical-writing probe (flush as a cleaning operation)"),
    ("tap", "Noun", "tapping-point",
     "a connection point in a line, circuit, or winding from which pressure, signal, or current is taken (a pressure tap, a 24 V tap)",
     "project sense: found by the technical-writing probe (tap as a connection point)"),
    ("bleed", "Verb", "vent",
     "release trapped air, gas, or fluid from a closed system, such as brake lines, a radiator, or an engine (to bleed the air)",
     "project sense: found by the technical-writing probe (bleed air from a system)"),
]
CURATION = os.path.join(lexdata.CORPUS, "curation", "senses.tsv")
WORK = os.path.join(qf.WORK, "curate")
SLUG = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")


def lemma_senses():
    senses = lexdata.load_senses()
    by = collections.defaultdict(list)
    for pid, s in senses.items():
        if s["lemma"] in LEMMAS:
            by[s["lemma"]].append(s)
    return by


def raw_ids():
    """public id -> (oewn sense id, sense key, synset, raw pos) from the sense table."""
    entries = {r["entry_id"]: r for r in lexdata.read_table("entry")}
    labels = {r["sense_id"]: r["label"] for r in lexdata.read_table("sense_label")}
    out = {}
    for r in lexdata.read_table("sense"):
        e = entries.get(r["entry_id"])
        if e and e["lemma"] in LEMMAS and r["sense_id"] in labels:
            out[e["lemma"] + "#" + labels[r["sense_id"]]] = (r["sense_id"], r["sense_key"], r["synset_id"], e["pos"])
    return out


def cmd_export():
    os.makedirs(os.path.join(WORK, "in"), exist_ok=True)
    os.makedirs(os.path.join(WORK, "out"), exist_ok=True)
    by = lemma_senses()
    ids = raw_ids()
    batches = [LEMMAS[0:3], LEMMAS[3:4] + LEMMAS[7:9], LEMMAS[4:5], LEMMAS[5:7], LEMMAS[9:12]]
    for i, group in enumerate(batches, 1):
        items = []
        for lemma in group:
            rows = []
            for s in sorted(by[lemma], key=lambda x: (x["pos"], -x["tag_count"], x["id"])):
                rows.append({"id": s["id"], "pos": s["pos_name"], "tags": s["tag_count"], "gloss": s["gloss"][:200]})
            for pl, pos, label, gloss, _note in PROJECT:
                if pl == lemma:
                    rows.append({"id": f"{lemma}#{label}", "pos": pos, "tags": 0, "gloss": gloss[:200]})
            items.append({"lemma": lemma, "senses": rows})
        qf.write_json(os.path.join(WORK, "in", f"cu{i}.json"), {"batch": f"cu{i}", "lemmas": items})
    print(len(batches), "batches")


def cmd_apply():
    by = lemma_senses()
    ids = raw_ids()
    labels = {}
    for name in sorted(os.listdir(os.path.join(WORK, "out"))):
        with open(os.path.join(WORK, "out", name), encoding="utf-8") as handle:
            for line in handle:
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 2:
                    labels[parts[0]] = parts[1]
    with open(CURATION, encoding="utf-8", newline="") as handle:
        existing = {r["sense_id"] for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)}
    forms = collections.defaultdict(set)
    every = set(LEMMAS) | {p[0] for p in PROJECT}
    entry_lemma = {r["entry_id"]: r["lemma"] for r in lexdata.read_table("entry") if r["lemma"] in every}
    for r in lexdata.read_table("form"):
        lemma = entry_lemma.get(r["entry_id"]) or (r["lemma"] if r["lemma"] in every and not r["entry_id"] else None)
        if lemma:
            forms[lemma].add(r["spelling"])
    rows = []
    used = collections.defaultdict(set)
    for lemma in LEMMAS:
        if any(e.startswith(lemma + "#") for e in existing):
            continue  # already curated: the labels of the data are the curated ones
        planned = []
        for s in sorted(by[lemma], key=lambda x: (x["pos"], -x["tag_count"], x["id"])):
            label = labels.get(s["id"], s["id"].split("#", 1)[1])
            planned.append((s, label))
        for lab in (l for _s, l in planned):
            if not SLUG.match(lab):
                raise SystemExit(f"bad label {lab} for {lemma}")
        for s, label in planned:
            pid = f"{lemma}#{label}"
            if pid in used[lemma]:
                raise SystemExit(f"duplicate label {pid}")
            used[lemma].add(pid)
            if pid in existing:
                continue
            old = ids[s["id"]]
            tag = s["tag_count"]
            tier = "C" if tag >= 5 else ("U" if tag >= 1 else "R")
            rows.append([pid, old[0], old[1], old[2], old[3] if old[3] != "s" else "a", s["pos_name"], s["gloss"], str(tag), tier,
                         "wn30-tag-count", "oewn", "", "", "", ",".join(sorted(forms[lemma]))])
        for pl, pos, label, gloss, note in PROJECT:
            if pl != lemma:
                continue
            label = labels.get(f"{lemma}#{label}", label)
            pid = f"{lemma}#{label}"
            if pid in existing or pid in used[lemma]:
                continue
            used[lemma].add(pid)
            rows.append([pid, "", "", "", pos[0].lower(), pos, gloss, "", "U", note, "project", "", "", "", ",".join(sorted(forms[lemma]))])
    for pl, pos, label, gloss, note in PROJECT:
        if pl in LEMMAS:
            continue
        pid = f"{pl}#{label}"
        if pid in existing:
            continue
        rows.append([pid, "", "", "", pos[0].lower(), pos, gloss, "", "U", note, "project", "", "", "", ",".join(sorted(forms[pl]))])
    with open(CURATION, "a", encoding="utf-8", newline="") as handle:
        for r in rows:
            handle.write("\t".join(x.replace("\t", " ") for x in r) + "\n")
    print(len(rows), "rows appended")


if __name__ == "__main__":
    {"export": cmd_export, "apply": cmd_apply}[sys.argv[1]]()
