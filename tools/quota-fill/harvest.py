"""Harvest of real sentences for the quota filling.

  python harvest.py candidates WAVE [--cap N] [--min N]   sample candidate sentences for the lemmas of a wave and write the
                                                          labeling batches work/w<WAVE>/in/*.json (+ meta)
  python harvest.py status WAVE                           the lemmas and deficits of a wave

Candidates come from the pool of public-domain text (pool.py). A candidate is a sentence in which one word is a form of
a target lemma. The batch of a form lists the scope senses of every (lemma, part of speech) the form can be, so the
labeler chooses among exactly the senses the lexicon allows for it.
"""
import argparse
import collections
import hashlib
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexindex
import pool
import qf
import textutil

FAMILY_SHARE = {"gutenberg": 0.55, "tatoeba": 0.15, "other": 0.30}
OTHER = {"voa", "usgs", "nps", "noaa-nws"}
DOC_CAP = 5
BATCH_ITEMS = 230
BATCH_SENSES = 420


def family_class(family):
    return "gutenberg" if family == "gutenberg" else ("tatoeba" if family == "tatoeba" else "other")


def tokens_of(text):
    """Yield (start, end, word) of the words that may be a target: plain alphabetic words, no hyphen or contraction."""
    for m in re.finditer(r"[A-Za-z]+", text):
        s, e = m.span()
        before = text[s - 1] if s > 0 else " "
        after = text[e] if e < len(text) else " "
        if before == "-" or after == "-":
            continue
        if before == "'" and s > 1 and text[s - 2].isalpha():
            continue
        if after == "'" and e + 1 < len(text) and text[e + 1].isalpha() and text[e + 1] != "s":
            continue
        if after == "'" and e + 2 < len(text) and text[e + 1] == "s" and text[e + 2].isalpha():
            continue
        word = m.group(0)
        if len(word) > 1 and word.isupper():
            continue
        if word[0].isupper() and not re.match(r"""^["'(]*$""", text[:s]):
            continue
        yield s, e, word


def scan_pool(rows, forms):
    """form -> list of (row index, start, end)."""
    hits = collections.defaultdict(list)
    for i, (_sid, _doc, _family, text) in enumerate(rows):
        for s, e, word in tokens_of(text):
            f = word.lower()
            if f in forms:
                hits[f].append((i, s, e))
    return hits


def water_fill(weights, caps, total):
    """Spread `total` over keys proportionally to weights without exceeding the caps."""
    alloc = {k: 0 for k in weights}
    remaining = total
    active = {k for k in weights if caps[k] > 0}
    while remaining > 0 and active:
        wsum = sum(weights[k] for k in active)
        progressed = 0
        for k in sorted(active):
            give = min(caps[k] - alloc[k], max(1, int(remaining * weights[k] / wsum)))
            give = min(give, remaining - progressed)
            alloc[k] += give
            progressed += give
            if progressed >= remaining:
                break
        remaining -= progressed
        active = {k for k in active if alloc[k] < caps[k]}
        if progressed == 0:
            break
    return alloc


def pick_for_form(rng, hits, rows, n, doc_counts, used):
    """Choose up to n hits of one form: family shares, per-document cap, distinct sentences."""
    buckets = {"gutenberg": [], "tatoeba": [], "other": []}
    for h in hits:
        buckets[family_class(rows[h[0]][2])].append(h)
    for b in buckets.values():
        rng.shuffle(b)
    quota = {c: int(round(n * FAMILY_SHARE[c])) for c in buckets}
    chosen = []

    def take(bucket, k):
        got = 0
        for h in bucket:
            if got >= k:
                break
            sid, doc = rows[h[0]][0], rows[h[0]][1]
            if sid in used:
                continue
            cap = DOC_CAP if doc != "tatoeba-cc0" else max(DOC_CAP, n)
            if doc_counts[doc] >= cap:
                continue
            used.add(sid)
            doc_counts[doc] += 1
            chosen.append(h)
            got += 1
        return got

    for c in ("other", "tatoeba", "gutenberg"):
        take(buckets[c], quota[c])
    short = n - len(chosen)
    for c in ("gutenberg", "other", "tatoeba"):
        if short <= 0:
            break
        short -= take(buckets[c], short)
    return chosen


def group_key(world, form):
    pairs = sorted((l, p) for (l, p) in world["index"].get(form, ()) if l in world["by_lemma"])
    return tuple(pairs)


def senses_of_pairs(world, pairs):
    out = []
    for lemma, pos in pairs:
        for sid in world["by_lemma"][lemma]:
            s = world["senses"][sid]
            if s["pos"] == pos:
                out.append(sid)
    return out


def lemma_plan(world, rank_wave):
    ranking, tags = qf.lemma_ranking(world)
    status = lexindex.quota_status(world["senses"], world["scope"])
    missing = collections.Counter()
    for r in status:
        lemma = world["senses"][r["sense_id"]]["lemma"]
        missing[lemma] += max(0, r["anchor_quota"] - r["anchors"]) + max(0, r["holdout_quota"] - r["holdout"])
    return [(l, tags[l], missing[l]) for i, l in enumerate(ranking) if qf.wave_of_rank(i) == rank_wave], missing


def load_used(wave_dir):
    path = os.path.join(qf.WORK, "used-sids.txt")
    used = set()
    if os.path.exists(path):
        with open(path, encoding="utf-8") as handle:
            used = {l.strip() for l in handle if l.strip()}
    return used


def save_used(used):
    with open(os.path.join(qf.WORK, "used-sids.txt"), "w", encoding="utf-8", newline="\n") as handle:
        for sid in sorted(used):
            handle.write(sid + "\n")


def read_extras():
    """lemma -> number of extra real anchors still wanted (see qf.extra_real_wanted)."""
    result = collections.Counter()
    for sense, n in qf.extra_real_wanted().items():
        result[sense.split("#")[0]] += n
    return result


def cmd_candidates(args):
    world = qf.load_world()
    plan, missing = lemma_plan(world, args.wave)
    wave_dir = os.path.join(qf.WORK, f"w{args.wave}")
    for sub in ("in", "out", "meta"):
        os.makedirs(os.path.join(wave_dir, sub), exist_ok=True)
    rows, _docs = pool.load_pool()
    if args.family:
        rows = [r for r in rows if r[2] == args.family]
    blocked = qf.corpus_keys()
    keep = [i for i, r in enumerate(rows) if textutil.key_of(r[3]) not in blocked]
    rows = [rows[i] for i in keep]
    print(len(rows), "pool sentences after the corpus and suite filter", flush=True)
    used = load_used(wave_dir)
    lemma_forms = collections.defaultdict(set)
    for form, pairs in world["index"].items():
        if not form.isalpha():
            continue
        for lemma, _pos in pairs:
            lemma_forms[lemma].add(form)
    extras_by_lemma = read_extras()
    planned = {l for l, _t, _m in plan}
    tags_of = {l: t for l, t, _m in plan}
    targets = [(l, tags, miss) for (l, tags, miss) in plan if miss > 0 or extras_by_lemma.get(l, 0) > 0]
    targets += [(l, 0, 0) for l in sorted(extras_by_lemma) if l not in planned and l in world["by_lemma"]]
    if args.only_lemmas:
        names = [x for x in args.only_lemmas.split(",") if x in world["by_lemma"]]
        targets = [(l, 0, missing[l]) for l in names if missing[l] > 0 or extras_by_lemma.get(l, 0) > 0]
    wanted = set()
    for l, _t, _m in targets:
        wanted |= lemma_forms[l]
    hits = scan_pool(rows, wanted)
    print(len(targets), "lemmas;", sum(len(v) for v in hits.values()), "hits of", len(hits), "forms", flush=True)
    doc_splits = qf.real_documents_by_split()
    role_cache = {}

    def role_of(h):
        sid, doc, family = rows[h[0]][0], rows[h[0]][1], rows[h[0]][2]
        pdoc = qf.prov_document(sid, doc, family)
        if pdoc not in role_cache:
            splits = doc_splits.get(pdoc, set())
            if qf.is_eval_document(pdoc):
                role_cache[pdoc] = "eval" if "anchors" not in splits else None
            else:
                role_cache[pdoc] = "train" if "holdout" not in splits else None
        return role_cache[pdoc]

    selected = []  # (lemma, form, row index, start, end, role)
    extras = read_extras()
    for lemma, _tags, miss in targets:
        n_total = max(args.min, min(args.cap, 4 * (miss + extras.get(lemma, 0))))
        rng = random.Random(int(hashlib.sha1(("w%d|%s" % (args.wave, lemma)).encode()).hexdigest()[:12], 16))
        forms = sorted(f for f in lemma_forms[lemma] if hits.get(f))
        if not forms:
            continue
        weights = {f: len(hits[f]) ** 0.5 for f in forms}
        caps = {f: len(hits[f]) for f in forms}
        alloc = water_fill(weights, caps, n_total)
        doc_counts = collections.Counter()
        for f in forms:
            if alloc[f] <= 0:
                continue
            by_role = {"eval": [], "train": []}
            for h in hits[f]:
                r = role_of(h)
                if r:
                    by_role[r].append(h)
            n_eval = int(round(alloc[f] * 0.25))
            for role, n in (("eval", n_eval), ("train", alloc[f] - n_eval)):
                if n <= 0 or not by_role[role]:
                    continue
                for h in pick_for_form(rng, by_role[role], rows, n, doc_counts, used):
                    selected.append((lemma, f, h[0], h[1], h[2], role))
    print(len(selected), "candidates", flush=True)
    # batches
    groups = collections.defaultdict(list)
    for lemma, f, i, s, e, role in selected:
        key = group_key(world, f)
        if key:
            groups[key].append((lemma, f, i, s, e, role))
    batches, cur, cur_items, cur_senses = [], [], 0, 0
    for key in sorted(groups):
        sense_ids = senses_of_pairs(world, key)
        items = groups[key]
        pos = 0
        while pos < len(items):
            room = BATCH_ITEMS - cur_items
            if room <= 0 or cur_senses + len(sense_ids) > BATCH_SENSES and cur:
                batches.append(cur)
                cur, cur_items, cur_senses = [], 0, 0
                room = BATCH_ITEMS
            part = items[pos:pos + room]
            cur.append((key, sense_ids, part))
            cur_items += len(part)
            cur_senses += len(sense_ids)
            pos += len(part)
    if cur:
        batches.append(cur)
    total = 0
    for b, batch in enumerate(batches, args.first_batch):
        bid = f"w{args.wave}b{b:03d}"
        groups_json, meta, n = [], {}, 0
        for gi, (key, sense_ids, part) in enumerate(batch, 1):
            items = []
            for lemma, f, i, s, e, role in part:
                n += 1
                sid, doc, family, text = rows[i]
                braced = text[:s] + "{" + text[s:e] + "}" + text[e:]
                items.append({"n": n, "sentence": braced})
                meta[str(n)] = {"sid": sid, "form": f, "doc": doc, "family": family, "sentence": braced,
                                "lemma": lemma, "group": gi, "role": role}
            groups_json.append({"senses": [qf.sense_line(world, x) for x in sense_ids], "items": items})
        qf.write_json(os.path.join(wave_dir, "in", bid + ".json"), {"batch": bid, "groups": groups_json})
        qf.write_json(os.path.join(wave_dir, "meta", bid + ".json"),
                      {"groups": [[list(p) for p in key] for key, _s, _p in batch], "items": meta})
        total += n
    save_used(used)
    print(len(batches), "batches,", total, "items ->", wave_dir)


def cmd_status(args):
    world = qf.load_world()
    plan, _ = lemma_plan(world, args.wave)
    print(len(plan), "lemmas;", sum(m for _l, _t, m in plan), "missing slots")
    for l, t, m in plan[:80]:
        print(l, t, m)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("candidates")
    c.add_argument("wave", type=int)
    c.add_argument("--cap", type=int, default=450)
    c.add_argument("--min", type=int, default=150)
    c.add_argument("--family", default="", help="restrict the pool to one family (supplementary batch)")
    c.add_argument("--only-lemmas", default="", help="comma-separated lemmas that replace the wave plan")
    c.add_argument("--first-batch", type=int, default=1, help="number of the first batch (appends to a wave)")
    s = sub.add_parser("status")
    s.add_argument("wave", type=int)
    a = ap.parse_args()
    {"candidates": cmd_candidates, "status": cmd_status}[a.cmd](a)
