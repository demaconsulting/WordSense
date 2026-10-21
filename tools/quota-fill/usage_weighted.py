"""Usage-weighted real-text metric: a diagnostic of the fine-sense accuracy that weighs the real test sentences by how often
their senses are used.

  python usage_weighted.py [--work DIR] [--smoothing K] LABEL=OUTCOMES.tsv [LABEL=OUTCOMES.tsv ...]

The real test split is balanced by sense quota like the training anchors, so its accuracy says little about running text,
where the frequent sense of a lemma is far more common. This script re-weights the test rows of each outcomes file
(`evaluate <model> --outcomes FILE`, the lines of the set "real text, test split (headline)") by an independent prior of the
senses: the share of each sense among the high-confidence labels of the randomly harvested candidate sentences of the quota
fill waves (`WORK/w1`, `w2` and `w3`, file `labeled.tsv`), with the test sentences excluded and add-K smoothing (default 1)
over the senses of the lemma that have test rows. A row of a sense gets the weight `harvest share / test share` of its
lemma, so every lemma keeps its total weight and only the mix of its senses changes. It prints the fine top-1 accuracy, the
share answered at the sense level and the precision of those answers, weighted and unweighted.

The labels are LLM labels of harvested sentences (see `corpus/README.md`, "Method of the quota filling") and the harvest is not the training data, so the prior
is independent of the WordNet counts that the model's usage prior uses. The harvest cache is not committed; without it the
script cannot run. The figures are a diagnostic, not a headline: the weights come from the same genres as the test sentences.
"""
import argparse
import collections
import os
import re


def norm(text):
    return " ".join(re.findall(r"[A-Za-z0-9]+", text.lower()))


def load(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            c = line.rstrip("\n").split("\t")
            if len(c) < 9 or c[7] != "test":
                continue
            rows.append(dict(sense=c[2], answer=c[3], ok=c[5] == "1", best=c[6], sent=c[8]))
    return rows


def harvest_counts(work, excluded):
    counts = collections.Counter()
    for wave in ("w1", "w2", "w3"):
        path = os.path.join(work, wave, "labeled.tsv")
        with open(path, encoding="utf-8") as f:
            for line in f:
                c = line.rstrip("\n").split("\t")
                if len(c) < 8 or c[4] != "high" or norm(c[7]) in excluded:
                    continue
                counts[c[3]] += 1
    return counts


def weights(rows, counts, k):
    by_lemma = collections.defaultdict(list)
    for i, r in enumerate(rows):
        by_lemma[r["sense"].split("#")[0]].append(i)
    w = [0.0] * len(rows)
    for idx in by_lemma.values():
        senses = collections.Counter(rows[i]["sense"] for i in idx)
        total = sum(counts[s] for s in senses) + k * len(senses)
        for i in idx:
            s = rows[i]["sense"]
            pi = (counts[s] + k) / total
            t = senses[s] / len(idx)
            w[i] = pi / t
    return w


def metrics(rows, w):
    total = sum(w)
    fine = sum(wi for wi, r in zip(w, rows) if r["best"] == r["sense"]) / total
    answered = [(wi, r) for wi, r in zip(w, rows) if r["answer"].startswith("Sense:")]
    answered_w = sum(wi for wi, _ in answered)
    share = answered_w / total
    precision = sum(wi for wi, r in answered if r["ok"]) / max(1e-9, answered_w)
    return fine, share, precision


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    here = os.path.dirname(os.path.abspath(__file__))
    parser.add_argument("--work", default=os.path.join(here, "cache", "quota", "work"))
    parser.add_argument("--smoothing", type=float, default=1.0)
    parser.add_argument("outcomes", nargs="+", help="LABEL=FILE of an evaluate --outcomes file")
    args = parser.parse_args()

    inputs = [a.split("=", 1) for a in args.outcomes]
    first = load(inputs[0][1])
    counts = harvest_counts(args.work, {norm(r["sent"]) for r in first})
    print(f"Usage-weighted real test (weights: harvest share with K = {args.smoothing}); unweighted in brackets")
    for label, path in inputs:
        rows = load(path)
        w = weights(rows, counts, args.smoothing)
        fine, share, precision = metrics(rows, w)
        fine0, share0, precision0 = metrics(rows, [1.0] * len(rows))
        print(
            f"{label:28s} weighted fine {100 * fine:5.2f}  sense {100 * share:5.2f}% @ {100 * precision:5.2f}"
            f"   [unweighted fine {100 * fine0:5.2f}  {100 * share0:5.2f}% @ {100 * precision0:5.2f}]"
        )


if __name__ == "__main__":
    main()
