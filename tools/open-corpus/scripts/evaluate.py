#!/usr/bin/env python3
"""Agreement, adjudication statistics and (optionally) accuracy against a gold file, written as JSON.

  evaluate.py --workdir DIR [--sentences SENTENCES.tsv] [--gold GOLD.tsv] --out scores.json

Needs DIR/final.tsv (finalize.py). Agreement is between the primary annotator and the second annotator on UPOS (and on
the collapsed tag). With --gold (columns uid, idx, form, lemma, upos; used only for the UD EWT dev accuracy
measurement) it also reports UPOS and collapsed accuracy of the primary annotator alone, the second annotator alone,
and the final consensus, overall and by consensus status, plus lemma accuracy of the primary annotator and the most
frequent UPOS confusions (tag pairs only, no text).
"""
import argparse
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import COLLAPSE, read_tsv


def kappa(pairs):
    """Return Cohen's kappa of a list of (tag, tag) pairs, or None when there are no pairs (nothing was compared)."""
    n = len(pairs)
    if n == 0:
        return None
    po = sum(a == b for a, b in pairs) / n
    ca, cb = collections.Counter(a for a, _ in pairs), collections.Counter(b for _, b in pairs)
    pe = sum(ca[k] * cb[k] for k in ca) / (n * n)
    return (po - pe) / (1 - pe) if pe < 1 else 1.0


def rounded(x, digits):
    """Round a number, keeping None (no value) as None."""
    return None if x is None else round(x, digits)


def pct(x, n):
    """Return x as a percentage of n, rounded to two places, or None when n is 0."""
    return round(100.0 * x / n, 2) if n else None


def main():
    """Command line: compute the agreement and the accuracy statistics of DIR/final.tsv and write them as JSON (see the module text)."""
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--sentences")
    ap.add_argument("--gold")
    ap.add_argument("--policy", default="keep-primary", help="policy used by finalize.py (keep-primary or a judge name)")
    ap.add_argument("--convention-map", action="store_true", help="also report accuracy after mapping the reference to the lexicon conventions (needs --lexicon-data)")
    ap.add_argument("--lexicon-data", help="WordSense repository data/ folder (read-only), for the convention map")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    final = read_tsv(os.path.join(args.workdir, "final.tsv"))
    n = len(final)
    res = {"tokens": n, "sentences": len({r["uid"] for r in final})}
    pairs = [(r["primary_upos"], r["second_upos"]) for r in final if r["second_upos"]]
    cpairs = [(COLLAPSE[a], COLLAPSE[b]) for a, b in pairs]
    nc = len(pairs)
    res["tokens_with_second_annotation"] = nc
    res["agreement"] = {"upos_percent": pct(sum(a == b for a, b in pairs), nc), "upos_kappa": rounded(kappa(pairs), 4),
                        "collapsed_percent": pct(sum(a == b for a, b in cpairs), nc), "collapsed_kappa": rounded(kappa(cpairs), 4)}
    status = collections.Counter(r["status"] for r in final)
    res["status"] = {k: {"tokens": v, "percent": pct(v, n)} for k, v in sorted(status.items())}
    adj_path = os.path.join(args.workdir, "adjudication", f"adjudicated_{args.policy}.tsv")
    if args.policy != "keep-primary" and os.path.exists(adj_path):
        live = {r["uid"] for r in final}
        adj = [a for a in read_tsv(adj_path) if a["uid"] in live]
        key = {r["item"]: r for r in read_tsv(os.path.join(args.workdir, "adjudication", "key.tsv"))}
        chose = collections.Counter()
        for a in adj:
            if a["choice"] == "U":
                chose["unresolved"] += 1
            else:
                is_primary = (a["choice"] == "A") == (key[a["item"]]["a_is"] == "primary")
                chose["primary tag chosen" if is_primary else "second tag chosen"] += 1
        res["adjudication"] = {"items": len(adj), **dict(chose)}
        same_collapsed = sum(1 for a in adj if COLLAPSE[a["primary_upos"]] == COLLAPSE[a["second_upos"]])
        res["adjudication"]["items_within_one_collapsed_class"] = same_collapsed
    if args.sentences:
        groups = {r["uid"]: r["group"] for r in read_tsv(args.sentences)}
        by = collections.defaultdict(lambda: [0, 0, 0])
        for r in final:
            g = groups[r["uid"]]
            by[g][2] += 1
            if r["second_upos"]:
                by[g][0] += 1
                by[g][1] += r["primary_upos"] == r["second_upos"]
        res["agreement_by_group"] = {g: {"tokens_checked": t, "tokens_total": tot, "upos_percent": pct(a, t)} for g, (t, a, tot) in sorted(by.items())}
    if args.gold:
        gold = {(r["uid"], int(r["idx"])): r for r in read_tsv(args.gold)}
        acc = {"primary": 0, "second": 0, "final": 0, "primary_collapsed": 0, "second_collapsed": 0, "final_collapsed": 0, "lemma_primary": 0}
        by_status = collections.defaultdict(lambda: collections.Counter())
        conf = collections.Counter()
        for r in final:
            g = gold[(r["uid"], int(r["idx"]))]
            if g["form"] != r["form"]:
                raise SystemExit("form mismatch with gold")
            gu = g["upos"]
            acc["primary"] += r["primary_upos"] == gu
            acc["second"] += r["second_upos"] == gu
            acc["final"] += r["upos"] == gu
            acc["primary_collapsed"] += COLLAPSE[r["primary_upos"]] == COLLAPSE[gu]
            acc["second_collapsed"] += bool(r["second_upos"]) and COLLAPSE[r["second_upos"]] == COLLAPSE[gu]
            acc["final_collapsed"] += COLLAPSE[r["upos"]] == COLLAPSE[gu]
            acc["lemma_primary"] += r["lemma"].lower() == g["lemma"].lower()
            s = by_status[r["status"]]
            s["tokens"] += 1
            s["upos_correct"] += r["upos"] == gu
            s["collapsed_correct"] += COLLAPSE[r["upos"]] == COLLAPSE[gu]
            if r["upos"] != gu:
                conf[f"{gu}->{r['upos']}"] += 1
        res["accuracy_vs_gold"] = {k: pct(v, n) for k, v in acc.items()}
        res["accuracy_by_status"] = {st: {"tokens": c["tokens"], "upos_percent": pct(c["upos_correct"], c["tokens"]),
                                          "collapsed_percent": pct(c["collapsed_correct"], c["tokens"])}
                                     for st, c in sorted(by_status.items())}
        excl = [r for r in final if r["status"] != "unresolved"]
        res["accuracy_excluding_unresolved"] = {
            "tokens": len(excl),
            "upos_percent": pct(sum(r["upos"] == gold[(r["uid"], int(r["idx"]))]["upos"] for r in excl), len(excl)),
            "collapsed_percent": pct(sum(COLLAPSE[r["upos"]] == COLLAPSE[gold[(r["uid"], int(r["idx"]))]["upos"]] for r in excl), len(excl))}
        dis = [r for r in final if r["second_upos"] and r["primary_upos"] != r["second_upos"]]
        gd = lambda r: gold[(r["uid"], int(r["idx"]))]["upos"]
        res["disagreement_tokens_vs_gold"] = {
            "tokens": len(dis), "primary_correct": sum(r["primary_upos"] == gd(r) for r in dis),
            "second_correct": sum(r["second_upos"] == gd(r) for r in dis),
            "neither_correct": sum(gd(r) not in (r["primary_upos"], r["second_upos"]) for r in dis),
            "final_correct": sum(r["upos"] == gd(r) for r in dis)}
        res["top_confusions_gold_to_final"] = conf.most_common(15)
        if args.convention_map:
            from convention_map import lexicon_analyses, map_reference
            grows = read_tsv(args.gold)
            lex = lexicon_analyses(args.lexicon_data, {r["form"] for r in grows if r["upos"] == "SCONJ"})
            mapped, counts = map_reference(grows, lex)
            gm = {(r["uid"], int(r["idx"])): m for r, m in zip(grows, mapped)}
            ok = sum(r["upos"] == gm[(r["uid"], int(r["idx"]))] for r in final)
            okc = sum(COLLAPSE[r["upos"]] == COLLAPSE[gm[(r["uid"], int(r["idx"]))]] for r in final)
            st = collections.defaultdict(lambda: collections.Counter())
            conf2 = collections.Counter()
            for r in final:
                g2 = gm[(r["uid"], int(r["idx"]))]
                st[r["status"]]["tokens"] += 1
                st[r["status"]]["ok"] += r["upos"] == g2
                st[r["status"]]["okc"] += COLLAPSE[r["upos"]] == COLLAPSE[g2]
                if r["upos"] != g2:
                    conf2[f"{g2}->{r['upos']}"] += 1
            dis2 = [r for r in final if r["second_upos"] and r["primary_upos"] != r["second_upos"]]
            res["accuracy_vs_gold_convention_mapped"] = {
                "upos_percent": pct(ok, n), "collapsed_percent": pct(okc, n), "reference_tokens_remapped": counts,
                "reference_tokens_remapped_total": sum(counts.values()),
                "by_status": {k: {"tokens": c["tokens"], "upos_percent": pct(c["ok"], c["tokens"]), "collapsed_percent": pct(c["okc"], c["tokens"])} for k, c in sorted(st.items())},
                "second_annotator_alone_upos_percent": pct(sum(r["second_upos"] == gm[(r["uid"], int(r["idx"]))] for r in final), n),
                "disagreement_tokens": {"tokens": len(dis2), "primary_correct": sum(r["primary_upos"] == gm[(r["uid"], int(r["idx"]))] for r in dis2),
                                        "second_correct": sum(r["second_upos"] == gm[(r["uid"], int(r["idx"]))] for r in dis2)},
                "top_confusions": conf2.most_common(15)}
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(res, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(json.dumps(res, indent=1, ensure_ascii=False))


if __name__ == "__main__":
    main()
