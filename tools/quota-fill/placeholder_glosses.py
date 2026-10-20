"""Placeholder glosses of project senses: detection, export for the writers, and application to the data.

Detection rule (a gloss is a placeholder when the sense is a project sense, source `dema`, and the gloss is):
  1. empty;
  2. a bare word or hyphenated pair of words without a meaning ("noun", "verb", "adjective", "adj-noun", "row", "wick");
  3. the seeder template ("The preposition "about"; a project sense, because Open English WordNet has no ...");
  4. "X (noun); no definition recorded".
Glosses of OEWN senses are never touched.

Commands:
  python placeholder_glosses.py detect                      -> counts
  python placeholder_glosses.py export OUTDIR [batch_size]  -> OUTDIR/in/batchNN.json
  python placeholder_glosses.py apply  FILE...              -> reads OUTDIR/out/*.tsv (sense_id TAB gloss), rewrites
                                                              data/gloss and corpus/curation/senses.tsv, writes the record
"""
import csv
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata

TEMPLATE = re.compile(r'^The [a-z ]+ ".*"; a project sense, because Open English WordNet has no .* sense of this word\.$')
BARE = re.compile(r"^[A-Za-z]+(-[A-Za-z]+)?$")
NODEF = re.compile(r"no definition recorded$")

ROLE = {"u": "pronoun (closed class)", "p": "preposition (closed class)", "c": "conjunction (closed class)",
        "i": "interjection", "d": "determiner (closed class)", "x": "auxiliary verb (closed class)",
        "n": "noun", "v": "verb", "a": "adjective", "r": "adverb"}


def is_placeholder(sense):
    if sense["source"] != "dema":
        return False
    gloss = sense["gloss"].strip()
    return (not gloss) or bool(BARE.match(gloss)) or bool(TEMPLATE.match(gloss)) or bool(NODEF.search(gloss))


def corpus_examples():
    """sense_id -> up to 3 example sentences from the anchors, holdout and real-text rows."""
    found = {}
    for folder in ("anchors", "holdout", "realtext"):
        for path in sorted(glob.glob(os.path.join(lexdata.CORPUS, folder, "*.tsv"))):
            with open(path, encoding="utf-8") as handle:
                for line in handle:
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) == 4:
                        lst = found.setdefault(parts[1], [])
                        if len(lst) < 3:
                            lst.append(parts[3])
    return found


def detect(senses):
    return [s for s in senses.values() if is_placeholder(s)]


def curation_forms():
    forms = {}
    with open(os.path.join(lexdata.CORPUS, "curation", "senses.tsv"), encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
            forms[row["sense_id"]] = row["forms"]
    return forms


def export(outdir, batch_size=40):
    senses = lexdata.load_senses()
    items = sorted(detect(senses), key=lambda s: s["id"])
    examples = corpus_examples()
    by_lemma = {}
    for s in senses.values():
        by_lemma.setdefault(s["lemma"], []).append(s)
    forms = curation_forms()
    os.makedirs(os.path.join(outdir, "in"), exist_ok=True)
    os.makedirs(os.path.join(outdir, "out"), exist_ok=True)
    records = []
    for s in items:
        others = [f'{o["id"]} ({o["pos_name"]}): {o["gloss"][:110]}' for o in by_lemma[s["lemma"]]
                  if o["id"] != s["id"] and not is_placeholder(o)][:6]
        label = s["id"].split("#", 1)[1]
        records.append({
            "id": s["id"], "lemma": s["lemma"], "pos": s["pos_name"], "role": ROLE.get(s["pos"], s["pos"]),
            "label": label, "current": s["gloss"], "forms": forms.get(s["id"], ""),
            "other_senses_of_lemma": others, "examples": examples.get(s["id"], [])[:2],
        })
    for start in range(0, len(records), batch_size):
        number = start // batch_size + 1
        with open(os.path.join(outdir, "in", f"batch{number:02d}.json"), "w", encoding="utf-8", newline="\n") as handle:
            json.dump(records[start:start + batch_size], handle, ensure_ascii=False, indent=1)
    return len(records)


def apply(outdir):
    senses = lexdata.load_senses()
    new = {}
    for path in sorted(glob.glob(os.path.join(outdir, "out", "*.tsv"))):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.rstrip("\n")
                if not line.strip():
                    continue
                sid, gloss = line.split("\t", 1)
                new[sid] = gloss.strip()
    expected = {s["id"] for s in detect(senses)}
    missing = expected - set(new)
    extra = set(new) - expected
    if missing or extra:
        raise SystemExit(f"missing {len(missing)} extra {len(extra)}: {sorted(missing)[:5]} {sorted(extra)[:5]}")
    for sid, gloss in new.items():
        if ("\t" in gloss) or ("\n" in gloss) or len(gloss) < 8 or len(gloss) > 220 or BARE.match(gloss) or gloss != gloss.strip():
            raise SystemExit(f"bad gloss for {sid}: {gloss!r}")
    synset_text = {senses[sid]["synset"]: gloss for sid, gloss in new.items()}
    changed = 0
    for path in glob.glob(os.path.join(lexdata.DATA, "gloss", "*.tsv")):
        with open(path, encoding="utf-8", newline="") as handle:
            lines = handle.read().split("\n")
        dirty = False
        for i, line in enumerate(lines):
            parts = line.split("\t")
            if len(parts) == 3 and parts[0] in synset_text and parts[1] == "1":
                lines[i] = "\t".join([parts[0], parts[1], synset_text[parts[0]]])
                dirty = True
                changed += 1
        if dirty:
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write("\n".join(lines))
    # curated project senses: the seeder re-applies the curation gloss, so the curation file must carry the new one
    cpath = os.path.join(lexdata.CORPUS, "curation", "senses.tsv")
    with open(cpath, encoding="utf-8", newline="") as handle:
        lines = handle.read().split("\n")
    header = lines[0].split("\t")
    gi, si = header.index("gloss"), header.index("sense_id")
    curated = 0
    for i, line in enumerate(lines[1:], 1):
        parts = line.split("\t")
        if len(parts) > gi and parts[si] in new:
            parts[gi] = new[parts[si]]
            lines[i] = "\t".join(parts)
            curated += 1
    with open(cpath, "w", encoding="utf-8", newline="") as handle:
        handle.write("\n".join(lines))
    record = os.path.join(lexdata.CORPUS, "curation", "project-glosses.tsv")
    with open(record, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("sense_id\tpart_of_speech\told_gloss\tgloss\n")
        for sid in sorted(new, key=lambda k: k.encode("utf-8")):
            handle.write("\t".join([sid, senses[sid]["pos_name"], senses[sid]["gloss"], new[sid]]) + "\n")
    print(f"glosses rewritten in data/gloss: {changed}; curation rows: {curated}; record rows: {len(new)}")


if __name__ == "__main__":
    command = sys.argv[1]
    if command == "detect":
        sn = lexdata.load_senses()
        d = detect(sn)
        print(len(d), "placeholder glosses")
    elif command == "export":
        print(export(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 40), "senses exported")
    elif command == "apply":
        apply(sys.argv[2])
