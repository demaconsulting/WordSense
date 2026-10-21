"""The technical senses of wave 3 (maintenance and engineering instructions).

  python techsenses.py export     writes work/tech/in/tech1.json (the senses of the technical lemmas)
  python techsenses.py apply      reads work/tech/out/tech1.tsv and writes corpus/curation/tech-senses.tsv

`corpus/curation/tech-senses.tsv` lists the senses that technical instructions (aircraft and machine maintenance, STE style:
"Mount the bracket to the frame.", "Torque the bolts to 25 N m.") use, with the number of extra anchors that the quota
filling brings for each (`qf.read_tech_targets`), and the gaps the reviewer found (a meaning the catalog lacks).
"""
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qf

OUT = os.path.join(qf.CORPUS, "curation", "tech-senses.tsv")
WORK = os.path.join(qf.WORK, "tech")


def cmd_export():
    os.makedirs(os.path.join(WORK, "in"), exist_ok=True)
    os.makedirs(os.path.join(WORK, "out"), exist_ok=True)
    world = qf.load_world()
    lemmas = []
    for lemma in qf.TECH_ALL:
        rows = []
        for sid in world["by_lemma"].get(lemma, []):
            s = world["senses"][sid]
            rows.append({"id": sid, "pos": s["pos_name"], "tags": s["tag_count"], "gloss": s["gloss"][:180]})
        lemmas.append({"lemma": lemma, "senses": rows})
    qf.write_json(os.path.join(WORK, "in", "tech1.json"), {"batch": "tech1", "lemmas": lemmas})
    print(len(lemmas), "lemmas")


def cmd_apply():
    path = os.path.join(WORK, "out", "tech1.tsv")
    rows, gaps = [], []
    world = qf.load_world()
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 3 and parts[0] == "GAP":
                gaps.append(parts[1:4] + [""] * (4 - len(parts[1:4])))
            elif len(parts) >= 2 and parts[0] in world["senses"]:
                rows.append([parts[0], parts[1], parts[2] if len(parts) > 2 else ""])
    with open(OUT, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("sense_id\textra_anchors\tnote\n")
        for r in sorted(rows):
            handle.write("\t".join(r) + "\n")
    with open(os.path.join(WORK, "gaps.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for g in gaps:
            handle.write("\t".join(g) + "\n")
    print(len(rows), "technical senses,", len(gaps), "gaps")


if __name__ == "__main__":
    {"export": cmd_export, "apply": cmd_apply}[sys.argv[1]]()
