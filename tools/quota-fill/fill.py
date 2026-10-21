"""Ingest of labeled candidates and selection of the rows that fill the quotas.

  python fill.py ingest WAVE            read work/w<WAVE>/out/*.tsv, validate against the batch meta, write labeled.tsv
  python fill.py select WAVE            choose rows for the senses with a deficit, write the corpus files of the wave
                                        (anchors, holdout, provenance, choosers) named quota-w<WAVE>-real.tsv

Every row written here is TARGETED by nature (a sentence found for a specific sense or form): its provenance row says
`selection = targeted`, the reason (`sense=<id>`) and how it was found. The rows are for training and for classifier
evaluation only, never for usage statistics.
"""
import argparse
import collections
import glob
import hashlib
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import choosers
import lexindex
import pool
import qf
import textutil

PROV_COLUMNS = ["id", "lemma", "sense", "split", "source", "licence", "source_key", "url", "contributor", "origin",
                "selection", "selection_reason", "found_by", "licence_basis"]
PD_US = "Public domain in the United States"
LABEL_MODEL = "gpt-6.1-sol"
SENSE_DOC_CAP = 3
LEMMA_DOC_CAP = 10
GOV = {"voa": ("voa", "VOA Learning English staff-written article (US federal employee work, 17 U.S.C. 105)"),
       "usgs": ("usgs", "USGS web page (US federal employee work, 17 U.S.C. 105; USGS public-domain statement)"),
       "nps": ("nps", "NPS web page (US federal employee work, 17 U.S.C. 105; NPS public-domain statement)"),
       "noaa-nws": ("nws", "NWS web page (US federal employee work, 17 U.S.C. 105; NWS public-domain statement)"),
       "faa": ("faa", "FAA publication (US federal employee work, 17 U.S.C. 105; credited third-party material excluded)")}


def prov_id(lemma, sentence):
    return hashlib.sha256((lemma + "|" + sentence).encode("utf-8")).hexdigest()[:12]


def licence_basis(doc_row, family):
    """The licence basis of the document of a sentence, recorded for every row."""
    basis = doc_row.get("license_basis", "").replace("\t", " ").strip()
    if family == "tatoeba" or "public domain in the United States" in basis:
        return basis
    return f"{PD_US}: {basis}"


def provenance_fields(doc_row, sid, family):
    """(source, licence, source_key, url, contributor) of a pool sentence."""
    if family == "tatoeba":
        tid = sid.split("-", 1)[1]
        return ("tatoeba-cc0", "CC0 1.0", tid, f"https://tatoeba.org/en/sentences/show/{tid}",
                "Tatoeba contributor (official CC0 1.0 export)")
    doc, _, ordinal = sid.partition("#")
    if family == "gutenberg":
        ebook = doc.split("-", 1)[1]
        year = doc_row.get("year", "")
        return ("gutenberg", PD_US, f"pg{ebook}#{ordinal}", f"https://www.gutenberg.org/ebooks/{ebook}",
                f"{doc_row['author']}: {doc_row['title']} ({year}; published 1930 or earlier)")
    source, contributor = GOV[family]
    return (source, PD_US, f"{doc}#{ordinal}", doc_row["url"], contributor + ": " + doc_row["title"])


def wave_dir(wave):
    return os.path.join(qf.WORK, f"w{wave}")


def cmd_ingest(args):
    world = qf.load_world()
    wdir = wave_dir(args.wave)
    stats = collections.Counter()
    out_rows = []
    for meta_path in sorted(glob.glob(os.path.join(wdir, "meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        out_path = os.path.join(wdir, "out", bid + ".tsv")
        if not os.path.exists(out_path):
            stats["batch without output"] += 1
            continue
        allowed = []
        for pairs in meta["groups"]:
            ids = set()
            for lemma, pos in pairs:
                for sid in world["by_lemma"][lemma]:
                    if world["senses"][sid]["pos"] == pos:
                        ids.add(sid)
            allowed.append(ids)
        seen = set()
        with open(out_path, encoding="utf-8") as handle:
            for line in handle:
                line = line.rstrip("\n").rstrip("\r")
                if not line.strip():
                    continue
                parts = line.split("\t")
                n = parts[0].strip()
                if n not in meta["items"] or n in seen:
                    stats["bad item number"] += 1
                    continue
                seen.add(n)
                item = meta["items"][n]
                stats["items"] += 1
                if len(parts) < 2 or parts[1].strip() == "unsure":
                    stats["unsure"] += 1
                    continue
                sense = parts[1].strip()
                conf = parts[2].strip() if len(parts) > 2 else "low"
                if sense not in allowed[item["group"] - 1]:
                    stats["sense not in the group"] += 1
                    continue
                stats["labeled"] += 1
                stats["high" if conf == "high" else "low"] += 1
                out_rows.append((item["sid"], item["form"], item["lemma"], sense, conf, item["doc"], item["family"],
                                 item["sentence"]))
        stats["missing lines"] += len(meta["items"]) - len(seen)
    with open(os.path.join(wdir, "labeled.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for r in out_rows:
            handle.write("\t".join(r) + "\n")
    print(dict(stats))


def existing_token_sets(lemmas):
    sets = collections.defaultdict(list)
    for folder in ("anchors", "holdout", "realtext"):
        for path in qf.corpus_files(folder):
            with open(path, encoding="utf-8") as handle:
                for line in handle:
                    p = line.rstrip("\n").split("\t")
                    if len(p) == 4 and p[0] in lemmas:
                        sets[p[0]].append(textutil.jaccard_tokens(p[3]))
    return sets


def suite_token_sets():
    import csv
    sets = collections.defaultdict(list)
    path = os.path.join(qf.QDIR, "suite.tsv")
    with open(path, encoding="utf-8", newline="") as handle:
        for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
            sets[r["lemma"]].append(textutil.jaccard_tokens(r["sentence"]))
    return sets


def chooser_forms_ok(fl):
    """Forms whose chooser labels are plain lemmas: two or more lemmas and no lemma with two feature sets of one entry."""
    ok = set()
    split = choosers.tense_split_forms()
    for form, per in fl.items():
        if len(per) >= 2 and form not in split:
            ok.add(form)
    return ok


def doc_cap(family, normal, faa):
    """Sentences of one document that a sense (or a lemma) may take: a handbook is a long document of its own."""
    if family in ("tatoeba", "written"):
        return 10 ** 6
    return faa if family == "faa" else normal


def spaced(n, h):
    """h evenly spaced indices of range(n)."""
    return {int((j + 0.5) * n / h) for j in range(h)} if h else set()


def cmd_select(args):
    kind = args.kind
    tag = f"quota-w{args.wave}-{kind}.tsv"
    for folder in ("anchors", "holdout", "provenance", os.path.join("choosers", "anchors"),
                   os.path.join("choosers", "holdout")):
        old = os.path.join(qf.CORPUS, folder, tag)
        if os.path.exists(old):
            os.remove(old)
    world = qf.load_world()
    senses = world["senses"]
    status = lexindex.quota_status(senses, world["scope"])
    deficit = {r["sense_id"]: (max(0, r["anchor_quota"] - r["anchors"]), max(0, r["holdout_quota"] - r["holdout"]))
               for r in status}
    blocked = qf.corpus_keys()
    _rows, docs = pool.load_pool()
    doc_splits = qf.real_documents_by_split() if kind == "real" else {}
    extra = qf.extra_real_wanted() if kind == "real" else {}
    if args.tech:
        # the technical senses: extra anchors beyond the quota; the written ones fill what real FAA text did not
        extra = dict(qf.read_tech_targets())
        if kind == "written":
            real_file = os.path.join(qf.CORPUS, "anchors", f"quota-w{args.wave}-real.tsv")
            have = collections.Counter()
            if os.path.exists(real_file):
                with open(real_file, encoding="utf-8") as handle:
                    for line in handle:
                        parts = line.rstrip("\n").split("\t")
                        if len(parts) == 4:
                            have[parts[1]] += 1
            extra = {s: max(8, n - have[s]) for s, n in extra.items()}
    labeled = collections.defaultdict(list)
    if kind == "real":
        paths = sorted(glob.glob(os.path.join(qf.WORK, "w*", "labeled.tsv")))
    else:
        paths = [os.path.join(wave_dir(args.wave), "wr-labeled.tsv")]
    for path in paths:
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                parts = line.rstrip("\n").split("\t")
                role = None
                if kind == "real":
                    sid, form, tl, sense, conf, doc, family, sentence = parts[:8]
                    pdoc = qf.prov_document(sid, doc, family)
                    splits = doc_splits.get(pdoc, set())
                    if qf.is_eval_document(pdoc):
                        role = "eval" if "anchors" not in splits else None
                    else:
                        role = "train" if "holdout" not in splits else None
                    if role is None:
                        continue
                else:
                    sid, form, intended, sense, conf, doc, family, sentence = parts
                    if sense != intended:
                        continue
                if conf != "high" or sense not in deficit:
                    continue
                if args.tech and kind == "real" and family != "faa":
                    continue
                s = senses[sense]
                if (s["lemma"], s["pos"]) not in world["index"].get(form, ()):
                    continue
                key = textutil.key_of(sentence)
                if key in blocked:
                    continue
                labeled[sense].append({"sid": sid, "form": form, "sense": sense, "doc": doc, "family": family,
                                       "sentence": sentence, "key": key, "tokens": textutil.jaccard_tokens(sentence),
                                       "role": role})
    lemmas = {senses[s]["lemma"] for s in labeled}
    existing = existing_token_sets(lemmas)
    suite = suite_token_sets()
    lemma_doc = collections.defaultdict(collections.Counter)
    taken = set()
    picked = []  # (sense, split, cand)
    short = collections.Counter()
    order = sorted(labeled, key=lambda s: (senses[s]["lemma"], s))
    for sense in order:
        da, dh = deficit[sense]
        if kind == "real" or args.tech:
            da += extra.get(sense, 0)
        if da + dh == 0:
            continue
        lemma = senses[sense]["lemma"]
        rng = random.Random(int(hashlib.sha1(sense.encode()).hexdigest()[:12], 16))
        cands = labeled[sense][:]
        rng.shuffle(cands)
        sets = list(existing[lemma]) + suite[lemma]

        def pick(pool_, need):
            form_count, doc_count = collections.Counter(), collections.Counter()
            chosen_ = []
            while len(chosen_) < need:
                best, best_score = None, None
                for c in pool_:
                    if c["key"] in taken:
                        continue
                    if doc_count[c["doc"]] >= doc_cap(c["family"], SENSE_DOC_CAP, 8):
                        continue
                    if lemma_doc[lemma][c["doc"]] >= doc_cap(c["family"], LEMMA_DOC_CAP, 60):
                        continue
                    score = (form_count[c["form"]], doc_count[c["doc"]], 0 if c["family"] != "gutenberg" else 1)
                    if best_score is None or score < best_score:
                        if any(textutil.jaccard(c["tokens"], t) >= 0.8 for t in sets):
                            continue
                        best, best_score = c, score
                if best is None:
                    break
                taken.add(best["key"])
                sets.append(best["tokens"])
                form_count[best["form"]] += 1
                doc_count[best["doc"]] += 1
                lemma_doc[lemma][best["doc"]] += 1
                chosen_.append(best)
            return chosen_

        if kind == "real":
            # evaluation documents supply held-out rows only, every other document anchors only
            hold_rows = pick([c for c in cands if c["role"] == "eval"], dh)
            anchor_rows = pick([c for c in cands if c["role"] == "train"], da)
            for c in hold_rows:
                picked.append((sense, "holdout", c))
            for c in anchor_rows:
                picked.append((sense, "anchors", c))
            short[sense] = da + dh - len(hold_rows) - len(anchor_rows)
        else:
            chosen = pick(cands, da + dh)
            hold = spaced(len(chosen), min(dh, len(chosen)))
            for i, c in enumerate(chosen):
                picked.append((sense, "holdout" if i in hold else "anchors", c))
            short[sense] = da + dh - len(chosen)
    # write files
    fl = choosers.form_lemmas()
    ch_ok = chooser_forms_ok(fl)
    tag = f"quota-w{args.wave}-{kind}.tsv"
    out = {"anchors": [], "holdout": [], "prov": [], "ch_anchors": [], "ch_holdout": []}
    for sense, split, c in sorted(picked, key=lambda t: (senses[t[0]]["lemma"], t[0], t[1], t[2]["sid"])):
        s = senses[sense]
        lemma = s["lemma"]
        out[split].append("\t".join([lemma, sense, s["pos_name"], c["sentence"]]))
        if kind == "real":
            doc = c["doc"]
            doc_row = docs[doc]
            src = provenance_fields(doc_row, c["sid"], c["family"])
            found = (f"harvest: pool sentence with the form '{c['form']}' of the lemma, found in {doc}; "
                     f"labeled {LABEL_MODEL} (high confidence, full sense list of the form)")
            origin = "real"
            basis = licence_basis(doc_row, c["family"])
        else:
            src = ("llm-corpus", "MIT (project-authored)", f"quota-w{args.wave}-written", "-",
                   f"gpt-5.6-luna (LLM-written); verified by {LABEL_MODEL} blind label")
            found = (f"written: gpt-5.6-luna wrote it for the sense (form '{c['form']}'); "
                     f"{LABEL_MODEL} labeled it blind with the same sense (high confidence)")
            origin = "llm-corpus"
            basis = "project-authored (MIT); no third-party text"
        out["prov"].append("\t".join([prov_id(lemma, c["sentence"]), lemma, sense, split, *src, origin, "targeted",
                                      f"sense={sense}", found, basis]))
        if c["form"] in ch_ok:
            out["ch_" + split].append("\t".join([c["form"], lemma, c["sentence"]]))
    for key, folder, header in (("anchors", "anchors", None), ("holdout", "holdout", None),
                                ("ch_anchors", os.path.join("choosers", "anchors"), None),
                                ("ch_holdout", os.path.join("choosers", "holdout"), None)):
        write_lines(os.path.join(qf.CORPUS, folder, tag), out[key])
    write_lines(os.path.join(qf.CORPUS, "provenance", tag), out["prov"], "\t".join(PROV_COLUMNS))
    n_real = collections.Counter(sp for _s, sp, _c in picked)
    fam = collections.Counter(c["family"] for _s, _sp, c in picked)
    print("picked", dict(n_real), "families", dict(fam), "chooser rows", len(out["ch_anchors"]), len(out["ch_holdout"]),
          "senses with candidates", len(labeled), "still short slots", sum(short.values()))


def write_lines(path, lines, header=None):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        if header:
            handle.write(header + "\n")
        for line in lines:
            handle.write(line + "\n")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("ingest", "select"):
        p = sub.add_parser(name)
        p.add_argument("wave", type=int)
        if name == "select":
            p.add_argument("--kind", choices=("real", "written"), default="real")
            p.add_argument("--tech", action="store_true", help="the technical senses: extra anchors of tech-senses.tsv, real rows from FAA text only")
    a = ap.parse_args()
    {"ingest": cmd_ingest, "select": cmd_select}[a.cmd](a)
