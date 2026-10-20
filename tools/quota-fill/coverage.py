"""Writes the current quota report and the progress counts.

  python coverage.py [WAVE]    writes corpus/coverage/quota-coverage.tsv (the columns of relabel-coverage.tsv, measured on the
                               corpus as it is now) and prints the counts, in total and for the lemmas of the waves up to WAVE
"""
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexindex
import qf


def counts(rows):
    c = collections.Counter(r["status"] for r in rows)
    ma = sum(max(0, r["anchor_quota"] - r["anchors"]) for r in rows)
    mh = sum(max(0, r["holdout_quota"] - r["holdout"]) for r in rows)
    return c, ma, mh


def blind_agreement():
    """sense -> (sentences written, sentences the blind labeler put in the same sense) over every wave."""
    import glob
    stat = collections.defaultdict(lambda: [0, 0])
    for path in glob.glob(os.path.join(qf.WORK, "w*", "wr-labeled.tsv")):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                stat[p[2]][0] += 1
                stat[p[2]][1] += 1 if p[3] == p[2] and p[4] == "high" else 0
    return stat


def write_unfilled(world, rows, wave):
    """The senses that the harvest and the writing could not bring to their quota (corpus/coverage/quota-unfilled.tsv)."""
    ranking, _ = qf.lemma_ranking(world)
    in_wave = {l for i, l in enumerate(ranking) if qf.wave_of_rank(i) <= wave}
    stat = blind_agreement()
    path = os.path.join(qf.CORPUS, "coverage", "quota-unfilled.tsv")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("sense_id\tpart_of_speech\ttier\tanchors\tanchor_quota\tholdout\tholdout_quota\twritten\tconfirmed_blind\tgloss\n")
        n = 0
        for r in rows:
            s = world["senses"][r["sense_id"]]
            if s["lemma"] in in_wave and r["status"] != "met":
                w, c = stat[r["sense_id"]]
                handle.write("\t".join(str(x) for x in [r["sense_id"], r["part_of_speech"], r["tier"], r["anchors"],
                                                        r["anchor_quota"], r["holdout"], r["holdout_quota"], w, c,
                                                        s["gloss"][:140]]) + "\n")
                n += 1
    return n


def main():
    world = qf.load_world()
    rows = lexindex.quota_status(world["senses"], world["scope"])
    lexindex.write_coverage(rows, os.path.join(qf.CORPUS, "coverage", "quota-coverage.tsv"))
    c, ma, mh = counts(rows)
    print("all:", dict(c), "missing anchors", ma, "missing holdout", mh)
    if len(sys.argv) > 1:
        wave = int(sys.argv[1])
        ranking, _ = qf.lemma_ranking(world)
        in_wave = {l for i, l in enumerate(ranking) if qf.wave_of_rank(i) <= wave}
        sub = [r for r in rows if world["senses"][r["sense_id"]]["lemma"] in in_wave]
        c, ma, mh = counts(sub)
        print(f"waves 1-{wave}:", len(in_wave), "lemmas,", dict(c), "missing anchors", ma, "missing holdout", mh)
        print("unfilled senses listed:", write_unfilled(world, rows, wave))


if __name__ == "__main__":
    main()
