"""Writes the current quota report and the progress counts.

  python coverage.py [WAVE]    writes corpus/coverage/quota-coverage.tsv (the columns of relabel-coverage.tsv, measured on the
                               corpus as it is now) and prints the counts, in total and for the lemmas of the waves up to WAVE
                               (a rank wave; it also writes quota-unfilled.tsv for them)
  python coverage.py --only-lemmas FILE   the same for a file of lemmas (the stage 1 waves): quota-unfilled.tsv gets the open
                               senses of these lemmas and keeps the rows of every other lemma
Both forms also write corpus/coverage/shape-coverage.tsv (the shape units, see shapes.py) and print the units met and short,
once corpus/curation/shape-fit.tsv exists.
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexindex
import qf
import shapes


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
    in_wave = qf.lemmas_up_to(world, wave)
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


def write_unfilled_for(world, rows, lemmas):
    """Replaces the rows of quota-unfilled.tsv of the given lemmas by the senses that are not met now; the rows of every
    other lemma stay."""
    path = os.path.join(qf.CORPUS, "coverage", "quota-unfilled.tsv")
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\n")
        kept = [l.rstrip("\n") for l in handle if l.split("\t", 1)[0].split("#", 1)[0] not in lemmas]
    stat = blind_agreement()
    new = []
    for r in rows:
        s = world["senses"][r["sense_id"]]
        if s["lemma"] in lemmas and r["status"] != "met":
            w, c = stat[r["sense_id"]]
            new.append("\t".join(str(x) for x in [r["sense_id"], r["part_of_speech"], r["tier"], r["anchors"],
                                                  r["anchor_quota"], r["holdout"], r["holdout_quota"], w, c, s["gloss"][:140]]))
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(header + "\n")
        for line in kept + sorted(new):
            handle.write(line + "\n")
    return len(new)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("wave", nargs="?", type=int, help="the rank wave whose lemmas quota-unfilled.tsv lists")
    ap.add_argument("--only-lemmas", default="",
                    help="a file of lemmas (stage 1 waves): quota-unfilled.tsv gets the open senses of these lemmas")
    args = ap.parse_args()
    world = qf.load_world()
    rows = lexindex.quota_status(world["senses"], world["scope"])
    lexindex.write_coverage(rows, os.path.join(qf.CORPUS, "coverage", "quota-coverage.tsv"))
    c, ma, mh = counts(rows)
    print("all:", dict(c), "missing anchors", ma, "missing holdout", mh)
    if args.only_lemmas:
        lemmas = set(qf.read_lemma_list(args.only_lemmas))
        sub = [r for r in rows if world["senses"][r["sense_id"]]["lemma"] in lemmas]
        c, ma, mh = counts(sub)
        print(len(lemmas), "lemmas,", dict(c), "missing anchors", ma, "missing holdout", mh)
        print("unfilled senses listed for them:", write_unfilled_for(world, rows, lemmas))
    elif args.wave:
        wave = args.wave
        in_wave = qf.lemmas_up_to(world, wave)
        sub = [r for r in rows if world["senses"][r["sense_id"]]["lemma"] in in_wave]
        c, ma, mh = counts(sub)
        print(f"waves 1-{wave}:", len(in_wave), "lemmas,", dict(c), "missing anchors", ma, "missing holdout", mh)
        print("unfilled senses listed:", write_unfilled(world, rows, wave))
    if os.path.exists(shapes.FIT_FILE):
        unit_rows = shapes.status(world)
        shapes.write_coverage(unit_rows)
        print(shapes.summary(unit_rows))


if __name__ == "__main__":
    main()
