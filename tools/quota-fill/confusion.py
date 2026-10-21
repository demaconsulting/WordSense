"""Chooses the senses that get extra real anchors: the confused sibling senses and the rare senses.

  python confusion.py WAVE      reads work/outcomes-<model>.tsv (evaluate --outcomes), writes work/extra-real.tsv and prints counts

Only the calibration split is read (never the test split), so the progress on the real test split stays independent of
the choice. A calibration row is an error when its most probable sense (the fine top-1) is not its gold sense. Every sense
that takes part in such an error (as the gold or as the predicted sense) is a confused sense: its real-anchor target is 8
(5 when it took part in one error only). A sense of a lemma of the waves up to WAVE that has fewer than 3 real anchors gets
the target 3 (the rare senses). The file lists `sense_id`, the real-anchor target, the number of errors and the reason.
"""
import collections
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qf


def main():
    wave = int(sys.argv[1])
    world = qf.load_world()
    in_wave = qf.lemmas_up_to(world, wave)
    errors = collections.Counter()
    pairs = collections.Counter()
    rows = 0
    for path in glob.glob(os.path.join(qf.WORK, "outcomes-*.tsv")):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                if len(p) < 9 or p[7] != "calibration":
                    continue
                rows += 1
                gold, best = p[2], p[6]
                if best and best != gold and gold in world["senses"] and best in world["senses"]:
                    pairs[(gold, best)] += 1
                    errors[gold] += 1
                    if best.split("#")[0] == gold.split("#")[0]:
                        errors[best] += 1
    have = qf.real_anchor_counts()
    target = {}
    reason = {}
    for sense, n in errors.items():
        lemma = sense.split("#")[0]
        if lemma in in_wave and sense in world["by_lemma"].get(lemma, []):
            target[sense] = 8 if n >= 2 else 5
            reason[sense] = f"confused ({n} calibration errors)"
    for lemma in in_wave:
        for sense in world["by_lemma"].get(lemma, []):
            if sense not in target and have.get(sense, 0) < 3:
                target[sense] = 3
                reason[sense] = "rare (fewer than 3 real anchors)"
    with open(os.path.join(qf.WORK, "extra-real.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for sense in sorted(target):
            handle.write(f"{sense}\t{target[sense]}\t{errors.get(sense, 0)}\t{reason[sense]}\n")
    wanted = qf.extra_real_wanted()
    print("calibration rows", rows, "error pairs", sum(pairs.values()), "senses targeted", len(target),
          "confused", sum(1 for s in target if reason[s].startswith("confused")), "extra real anchors wanted", sum(wanted.values()))
    print(pairs.most_common(10))


if __name__ == "__main__":
    main()
