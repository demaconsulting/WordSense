"""Ingest of labeled candidates and selection of the rows that fill the quotas.

  python fill.py ingest WAVE            read work/w<WAVE>/out/*.tsv, validate against the batch meta, write labeled.tsv
  python fill.py agree WAVE             merge the answers of two blind labelers (out-sol, out-sonnet) into out/: an item keeps
                                        its label only when both name the same sense with high confidence
  python fill.py select WAVE            choose rows for the senses with a deficit, write the corpus files of the wave
                                        (anchors, holdout, provenance, choosers) named quota-w<WAVE>-real.tsv;
                                        --only-lemmas FILE restricts the deficits to the listed lemmas (a new wave must,
                                        because select reads every labeled.tsv, the old waves' included)

Every row written here is TARGETED by nature (a sentence found for a specific sense or form). The provenance row of a
sentence has only the columns that the builder and these tools read (`id`, `split`, `source`, `source_key`); the licence
of a sentence follows from its source (the allowed sources are in tools/WordSense.Builder/ProvenanceSources.cs). The rows
are for training and for classifier evaluation only, never for usage statistics.
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
import qf
import shortfix
import textutil

PROV_COLUMNS = ["id", "split", "source", "source_key"]
SENSE_DOC_CAP = 3
LEMMA_DOC_CAP = 10
# The source of a provenance row must be one of the allowed sources of tools/WordSense.Builder/ProvenanceSources.cs
# (the licence gate: the test CorpusLicenseTests fails on any other source). Pool families that are named differently:
SOURCE_OF = {"voa": "voa", "usgs": "usgs", "nps": "nps", "noaa-nws": "nws", "faa": "faa"}
WRITTEN_SOURCE = "llm-corpus"  # sentences written by a model for the project (CC0 1.0, project-authored)


def prov_id(lemma, sentence):
    return hashlib.sha256((lemma + "|" + sentence).encode("utf-8")).hexdigest()[:12]


def provenance_fields(sid, family):
    """(source, source_key) of a pool sentence: the source is an allowed source, the source key names the document
    (a Tatoeba sentence is its own document; the other keys are the document and the sentence ordinal)."""
    if family == "tatoeba":
        return "tatoeba-cc0", sid.split("-", 1)[1]
    doc, _, ordinal = sid.partition("#")
    if family == "gutenberg":
        return "gutenberg", f"pg{doc.split('-', 1)[1]}#{ordinal}"
    return SOURCE_OF[family], f"{doc}#{ordinal}"


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


def read_labels(path):
    """n -> (sense, confidence) of a labeler output file; an item answered unsure, or without a sense, is absent."""
    labels = {}
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\r\n").split("\t")
            if not parts[0].strip():
                continue
            if len(parts) >= 2 and parts[1].strip() != "unsure":
                labels[parts[0].strip()] = (parts[1].strip(), parts[2].strip() if len(parts) > 2 else "low")
            else:
                labels.setdefault(parts[0].strip(), None)
    return labels


def cmd_agree(args):
    """Merges the answers of two blind labelers (work/w<WAVE>/out-<a>, out-<b>) into out/: an item keeps its label only when
    both name the same sense with high confidence; every other item becomes `unsure`. Writes agreement.tsv by lemma."""
    wdir = wave_dir(args.wave)
    first, second = args.labelers.split(",")
    for name in (first, second):
        if f"real-{name}" in shortfix.STAGES:
            shortfix.require_complete(args.wave, f"real-{name}")
    os.makedirs(os.path.join(wdir, "out"), exist_ok=True)
    per_lemma = collections.defaultdict(collections.Counter)
    missing = 0
    for meta_path in sorted(glob.glob(os.path.join(wdir, "meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        a = read_labels(os.path.join(wdir, f"out-{first}", bid + ".tsv"))
        b = read_labels(os.path.join(wdir, f"out-{second}", bid + ".tsv"))
        if a is None or b is None:
            missing += 1
            continue
        lines = []
        for n, item in sorted(meta["items"].items(), key=lambda kv: int(kv[0])):
            la, lb = a.get(n), b.get(n)
            c = per_lemma[item["lemma"]]
            c["items"] += 1
            if la and lb:
                if la[0] == lb[0]:
                    c["same sense"] += 1
                    if la[1] == "high" and lb[1] == "high":
                        c["agreed high"] += 1
                        lines.append(f"{n}\t{la[0]}\thigh")
                        continue
                else:
                    c["different sense"] += 1
            else:
                c["one or both unsure"] += 1
            lines.append(f"{n}\tunsure")
        with open(os.path.join(wdir, "out", bid + ".tsv"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n".join(lines) + "\n")
    cols = ["items", "same sense", "agreed high", "different sense", "one or both unsure"]
    with open(os.path.join(wdir, "agreement.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("lemma\t" + "\t".join(cols) + "\n")
        for lemma in sorted(per_lemma):
            handle.write(lemma + "\t" + "\t".join(str(per_lemma[lemma][c]) for c in cols) + "\n")
    total = collections.Counter()
    for c in per_lemma.values():
        total.update(c)
    print({c: total[c] for c in cols}, "batches without both outputs:", missing)


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
    world = qf.load_world()
    senses = world["senses"]
    listed = None
    if args.only_lemmas:
        listed = set(qf.read_lemma_list(args.only_lemmas))
        unknown = sorted(listed - set(world["by_lemma"]))
        if unknown:
            raise SystemExit(f"--only-lemmas names lemmas that are not in the scope (corpus/curation/senses.tsv): "
                             f"{', '.join(unknown[:10])}")
    for folder in ("anchors", "holdout", "provenance", os.path.join("choosers", "anchors"),
                   os.path.join("choosers", "holdout")):
        old = os.path.join(qf.CORPUS, folder, tag)
        if os.path.exists(old):
            os.remove(old)
    status = lexindex.quota_status(senses, world["scope"])
    deficit = {r["sense_id"]: (max(0, r["anchor_quota"] - r["anchors"]), max(0, r["holdout_quota"] - r["holdout"]))
               if not args.tier or r["tier"] == args.tier else (0, 0) for r in status}
    blocked = qf.corpus_keys()
    lemma_splits = qf.real_documents_by_lemma() if kind == "real" else {}
    extra = qf.extra_real_wanted() if kind == "real" and not args.tier else {}
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
    if listed is not None:
        # The other lemmas are left alone: the labeled files of earlier waves hold rows that the blind checks removed
        deficit = {s: d for s, d in deficit.items() if senses[s]["lemma"] in listed}
        extra = {s: n for s, n in extra.items() if senses[s]["lemma"] in listed}
    labeled = collections.defaultdict(list)
    vocab = set(world["index"])
    tech_senses = set(qf.read_tech_targets())
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
                else:
                    sid, form, intended, sense, conf, doc, family, sentence = parts
                    if sense != intended:
                        continue
                if conf != "high" or sense not in deficit:
                    continue
                if kind == "real":
                    # the role of the document for the lemma of the labeled sense
                    role = qf.doc_role(qf.prov_document(sid, doc, family), senses[sense]["lemma"], lemma_splits)
                    if role is None:
                        continue
                if args.tech and kind == "real" and family != "faa":
                    continue
                if family == "faa" and (textutil.pdf_damaged(sentence, vocab)
                                        or (args.faa_tech_only and sense not in tech_senses)):
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
            src = provenance_fields(c["sid"], c["family"])
        else:
            src = (WRITTEN_SOURCE, f"quota-w{args.wave}-written")
        out["prov"].append("\t".join([prov_id(lemma, c["sentence"]), split, *src]))
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
    for name in ("ingest", "select", "agree"):
        p = sub.add_parser(name)
        p.add_argument("wave", type=qf.wave_id)
        if name == "agree":
            p.add_argument("--labelers", default="sol,sonnet",
                           help="the two answer folders work/w<WAVE>/out-<name> to merge (default sol,sonnet)")
        if name == "select":
            p.add_argument("--tier", default="", choices=("", "C", "U", "R"),
                           help="fill only the deficits of the senses of this tier (a tier wave, e.g. 4c)")
            p.add_argument("--kind", choices=("real", "written"), default="real")
            p.add_argument("--faa-tech-only", action="store_true",
                           help="FAA sentences only for the technical senses of tech-senses.tsv (general text for the rest)")
            p.add_argument("--tech", action="store_true", help="the technical senses: extra anchors of tech-senses.tsv, real rows from FAA text only")
            p.add_argument("--only-lemmas", default="",
                           help="fill only the senses of these lemmas (a file of lemmas, or a comma-separated list); "
                                "the deficits of every other lemma stay untouched")
    a = ap.parse_args()
    if a.cmd == "select":
        qf.check_tier_wave(ap, a)
    {"ingest": cmd_ingest, "select": cmd_select, "agree": cmd_agree}[a.cmd](a)
