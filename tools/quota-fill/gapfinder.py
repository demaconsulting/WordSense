"""Finds the meanings that the catalog lacks in the FAA text: the sentences that the labeler could not place.

  python gapfinder.py export     writes work/gaps/in/gap1.json (FAA sentences of technical lemmas that got `unsure` or a low
                                 confidence, with the sense list of the lemma)
  python gapfinder.py report     reads work/gaps/out/gap1.tsv and writes work/gaps/gaps.tsv

The reviewer (gpt-6.1-sol, prompts/gap-finder.md) names, for each lemma, the meanings and the parts of speech that the sense
list does not cover; the result is a report for the curation, not an input of the training.
"""
import collections
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qf

WORK = os.path.join(qf.WORK, "gaps")
PER_LEMMA = 18


def cmd_export():
    os.makedirs(os.path.join(WORK, "in"), exist_ok=True)
    os.makedirs(os.path.join(WORK, "out"), exist_ok=True)
    world = qf.load_world()
    tech = set(qf.TECH_ALL)
    found = collections.defaultdict(list)
    for wave in (2, 3):
        wdir = os.path.join(qf.WORK, f"w{wave}")
        for meta_path in sorted(glob.glob(os.path.join(wdir, "meta", "*.json"))):
            bid = os.path.splitext(os.path.basename(meta_path))[0]
            out = os.path.join(wdir, "out", bid + ".tsv")
            if not os.path.exists(out):
                continue
            meta = qf.read_json(meta_path)
            answers = {}
            with open(out, encoding="utf-8") as handle:
                for line in handle:
                    p = line.rstrip("\n").split("\t")
                    if p and p[0].strip():
                        answers[p[0].strip()] = p
            for n, item in meta["items"].items():
                if item["family"] != "faa" or item["lemma"] not in tech:
                    continue
                a = answers.get(n)
                if a is None or len(a) < 2 or a[1] == "unsure" or (len(a) > 2 and a[2] == "low"):
                    found[item["lemma"]].append(item["sentence"])
    lemmas = []
    for lemma in sorted(found):
        senses = [{"id": s, "pos": world["senses"][s]["pos_name"], "gloss": world["senses"][s]["gloss"][:120]}
                  for s in world["by_lemma"].get(lemma, [])]
        lemmas.append({"lemma": lemma, "senses": senses, "sentences": sorted(set(found[lemma]))[:PER_LEMMA]})
    qf.write_json(os.path.join(WORK, "in", "gap1.json"), {"batch": "gap1", "lemmas": lemmas})
    print(len(lemmas), "lemmas,", sum(len(x["sentences"]) for x in lemmas), "sentences")


def cmd_report():
    path = os.path.join(WORK, "out", "gap1.tsv")
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            p = line.rstrip("\n").split("\t")
            if len(p) >= 3:
                rows.append(p[:4] + [""] * (4 - len(p[:4])))
    with open(os.path.join(WORK, "gaps.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for r in rows:
            handle.write("\t".join(r) + "\n")
    print(len(rows), "gaps")


if __name__ == "__main__":
    {"export": cmd_export, "report": cmd_report}[sys.argv[1]]()
