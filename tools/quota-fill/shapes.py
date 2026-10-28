"""Shape coverage: written anchors in the common sentence shapes of the noun/verb lemmas.

A lemma that is both a noun and a verb (`check`, `call`, `travel`) is easy to misread when its anchors lack a common
sentence shape: "Do a visual check" reads as a verb when no anchor has a noun after a light verb. This module defines the
shapes, the quota (at least `MIN_ROWS` kept written rows for every lemma, part of speech and shape that has a fitting
sense), the writer batches, the selection and the status. A written row carries its shape in the provenance
(`source_key` = `quota-w<WAVE>-shape:<shape>`); no corpus format changes.

  python shapes.py fit-export --lemmas FILE     work/shapefit/in/*.json: the noun and verb senses of the lemmas, for the judge
  python shapes.py fit-apply                    reads work/shapefit/out/*.tsv (sense id, shapes) into corpus/curation/shape-fit.tsv
  python shapes.py export WAVE --lemmas FILE [--short]
                                                work/w<WAVE>/wr-in/*.json: the writer batches of the units that miss rows
                                                (--short: only the units below the minimum, a second round)
  python shapes.py select WAVE                  the kept rows of work/w<WAVE>/wr-labeled.tsv -> corpus/{anchors,provenance,
                                                choosers/anchors}/quota-w<WAVE>-shape.tsv (never rerun for a checked wave)
  python shapes.py status [--lemmas FILE]       counts of the units met and short

After `export`: writers (`sentence-writer-shape.md`), `written.py ingest WAVE`, the blind labels, `written.py labeled WAVE`,
`shapes.py select WAVE`, then `blindcheck.py export|judge|apply WAVE --kind shape`. The wave id decides the stage of the lemma
(`Corpus.StagesOf`): use a wave of the stage of the lemmas (49 for the base stage, 99 for stage 1, 110 for stage 2).
"""
import argparse
import collections
import csv
import glob
import hashlib
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexindex
import qf
import textutil

MIN_ROWS = 2          # kept written rows needed for every (lemma, part of speech, shape) that has a fitting sense
SENSES_PER_UNIT = 2   # the most frequent fitting senses that share the rows of a unit
ASK_PER_SENSE = 2     # sentences asked per sense when two senses share a unit
ASK_ONE_SENSE = 3     # sentences asked when only one sense fits
ASK_SHORT = 3         # sentences asked per sense in a second round
MAX_PER_SENSE_SHAPE = 3
BATCH_SENTENCES = 220
FIT_BATCH_LEMMAS = 24
MIN_SENSE_ANCHORS = 3  # a shape row goes only to a sense that has anchors already (no new senses)
FIT_FILE = os.path.join(qf.CORPUS, "curation", "shape-fit.tsv")
COVERAGE_FILE = os.path.join(qf.CORPUS, "coverage", "shape-coverage.tsv")

# shape id -> (part of speech of the target, short title, the rule the writer follows)
SHAPES = {
    "n-obj-light": ("Noun", "object of a light verb",
                    "The target noun is the object of a light verb (do, make, run, perform, take, give or have) with a "
                    "determiner and an optional modifier between them, for example `Do a quick {survey} before you leave.`"),
    "n-compound-head": ("Noun", "head after a modifier noun",
                        "The target noun is the head of a noun compound: a noun that modifies it stands directly before it, "
                        "for example `The traffic {survey} took a week.`"),
    "n-modifier": ("Noun", "noun that modifies another noun",
                   "The target noun directly precedes another noun that it modifies, for example `The {survey} results "
                   "were posted online.`"),
    "n-subject-initial": ("Noun", "sentence-initial subject",
                          "The target noun (no article) is the first word of the sentence and its subject, followed by its "
                          "verb, for example `{Surveys} are mailed every spring.`"),
    "n-measure": ("Noun", "measurement or quantity frame",
                  "The target noun is measured or counted: the sentence has a number and, where it fits, a unit, for example "
                  "`Total {survey} cost is 1,200 dollars.`"),
    "v-imperative-initial": ("Verb", "sentence-initial imperative",
                             "The target verb (base form) is the first word of the sentence and gives an instruction, for "
                             "example `Survey the field before you plant.`"),
    "v-after-modal": ("Verb", "verb after a modal or infinitival to",
                      "The target verb (base form) follows a modal (can, could, may, might, must, shall, should, will, would) "
                      "or the infinitive marker to, for example `We will {survey} the land next week.`"),
}
SHAPES_OF_POS = {pos: [s for s, v in SHAPES.items() if v[0] == pos] for pos in ("Noun", "Verb")}

LIGHT = set("do does did done doing make makes made making run runs ran running perform performs performed performing take "
            "takes took taken taking give gives gave given giving have has had having".split())
DETERMINERS = set("a an the some any another each every this that these those no my our your his her their its one two three "
                  "four five several both either neither".split())
PREPOSITIONS = set("of in on at by for with from to into onto over under about after before between through during without "
                   "within against among around as like near off out up down per via than".split())
AUXILIARIES = set("is are was were am be been being has have had do does did will would can could may might must shall should "
                  "cannot".split())
PRONOUNS = set("i you he she it we they me him us them who whom which what there here".split())
CONJUNCTIONS = set("and or but nor so yet if because while although though when whenever where".split())
FUNCTION_WORDS = DETERMINERS | PREPOSITIONS | AUXILIARIES | PRONOUNS | CONJUNCTIONS | {"not", "n't", "very", "too", "also", "just",
                                                                                      "only", "then", "now", "still", "even"}
MODALS = set("can could may might must shall should will would cannot".split())
ADVERBS_AFTER_MODAL = set("not never always also simply first then still just quickly easily soon often usually only".split())
NUMBER_WORDS = set("one two three four five six seven eight nine ten eleven twelve twenty thirty forty fifty sixty hundred "
                   "thousand million half double twice dozen".split())

TOKEN_RE = re.compile(r"\{[A-Za-z]+\}|[A-Za-z0-9]+(?:['\u2019][A-Za-z]+)?|[^\sA-Za-z0-9]")


def words_of(sentence):
    """(lower-case words without punctuation, index of the braced target among them) or (words, None)."""
    words, target = [], None
    for token in TOKEN_RE.findall(sentence):
        if token.startswith("{"):
            target = len(words)
            words.append(token[1:-1].lower())
        elif token[0].isalnum():
            words.append(token.lower())
    return words, target


def check_shape(shape, sentence, lemma):
    """None when the sentence has the shape (a mechanical sanity check of the position and the neighbors of the braced
    target; the blind label judges the sense), else a reason."""
    words, t = words_of(sentence)
    if t is None:
        return "no target"
    prev = words[t - 1] if t >= 1 else None
    nxt = words[t + 1] if t + 1 < len(words) else None
    if shape == "n-obj-light":
        for j in range(max(0, t - 5), t):
            if words[j] in LIGHT and any(w in DETERMINERS for w in words[j + 1:t]):
                return None
        return "no light verb and determiner before the target"
    if shape == "n-compound-head":
        if prev is None or prev in FUNCTION_WORDS or not prev.isalpha():
            return "no modifier noun before the target"
        return None
    if shape == "n-modifier":
        if nxt is None or nxt in FUNCTION_WORDS or not nxt.isalpha():
            return "no noun after the target"
        return None
    if shape == "n-subject-initial":
        if t != 0 or nxt is None or nxt in (PREPOSITIONS | DETERMINERS | PRONOUNS | CONJUNCTIONS | {"to", "not"}):
            return "the target is not the first word followed by its verb"
        return None
    if shape == "n-measure":
        if any(w.isdigit() or w in NUMBER_WORDS or any(c.isdigit() for c in w) for w in words):
            return None
        return "no number"
    if words[t] != lemma.lower():
        return "the verb is not the base form"
    if shape == "v-imperative-initial":
        if t != 0 or nxt is None or nxt in AUXILIARIES or nxt in PRONOUNS - {"it", "them", "him", "her", "us", "me", "you"}:
            return "the target is not the first word of an imperative"
        return None
    if shape == "v-after-modal":
        if prev in MODALS or prev == "to":
            return None
        if t >= 2 and words[t - 2] in MODALS and prev in ADVERBS_AFTER_MODAL:
            return None
        return "no modal or to before the target"
    raise ValueError(shape)


def read_fit():
    """sense id -> set of shape ids of corpus/curation/shape-fit.tsv."""
    fit = {}
    if os.path.exists(FIT_FILE):
        with open(FIT_FILE, encoding="utf-8", newline="") as handle:
            for row in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
                fit[row["sense_id"]] = {s for s in row["shapes"].split(",") if s}
    return fit


def anchor_counts():
    counts = collections.Counter()
    for _f, _lemma, sid, _pos, _sentence in lexindex.corpus_rows("anchors"):
        counts[sid] += 1
    return counts


def shape_counts():
    """(lemma, part of speech, shape) -> number of written shape rows in the corpus, from the provenance tag joined to the
    anchors by the hash of `lemma|sentence`."""
    tag = {}
    for rid, _split, source, key in qf.provenance_rows():
        if source in qf.WRITTEN_SOURCES and ":" in key and key.startswith("quota-w") and "-shape:" in key:
            tag[rid] = key.split("-shape:", 1)[1]
    counts = collections.Counter()
    for _f, lemma, _sid, pos, sentence in lexindex.corpus_rows("anchors"):
        shape = tag.get(hashlib.sha256((lemma + "|" + sentence).encode("utf-8")).hexdigest()[:12])
        if shape:
            counts[(lemma, pos, shape)] += 1
    return counts


def shape_lemmas(world):
    """The lemmas of the scope that have a Noun and a Verb sense."""
    result = []
    for lemma, ids in world["by_lemma"].items():
        poses = {world["senses"][s]["pos_name"] for s in ids}
        if {"Noun", "Verb"} <= poses:
            result.append(lemma)
    return sorted(result)


def adjective_lemmas(world):
    """The lemmas that have an Adjective sense in the lexicon (the shape n-modifier is skipped for them)."""
    return {s["lemma"] for s in world["senses"].values() if s["pos_name"] == "Adjective"}


def units(world, fit, lemmas, anchors=None, written=None):
    """The (lemma, part of speech, shape) units of the lemmas that have a fitting sense: dicts with `lemma`, `pos`, `shape`,
    `senses` (the most frequent fitting senses with at least MIN_SENSE_ANCHORS anchors, as ids) and `have` (kept written
    rows, 0 without `written`)."""
    anchors = anchors if anchors is not None else anchor_counts()
    adjective = adjective_lemmas(world)
    result = []
    for lemma in lemmas:
        ids = world["by_lemma"].get(lemma, [])
        for pos, shapes in SHAPES_OF_POS.items():
            for shape in shapes:
                if shape == "n-modifier" and lemma in adjective:
                    continue
                fitting = [s for s in ids if world["senses"][s]["pos_name"] == pos and shape in fit.get(s, ())
                           and anchors.get(s, 0) >= MIN_SENSE_ANCHORS]
                if not fitting:
                    continue
                fitting.sort(key=lambda s: (-world["senses"][s]["tag_count"], s))
                result.append({"lemma": lemma, "pos": pos, "shape": shape, "senses": fitting[:SENSES_PER_UNIT],
                               "have": (written or {}).get((lemma, pos, shape), 0)})
    return result


def cmd_fit_export(args):
    world = qf.load_world()
    lemmas = [l for l in qf.read_lemma_list(args.lemmas) if l in world["by_lemma"]]
    folder = os.path.join(qf.WORK, "shapefit")
    for sub in ("in", "out"):
        os.makedirs(os.path.join(folder, sub), exist_ok=True)
    entries = []
    for lemma in lemmas:
        senses = [world["senses"][s] for s in world["by_lemma"][lemma] if world["senses"][s]["pos_name"] in ("Noun", "Verb")]
        senses.sort(key=lambda s: (s["pos_name"], -s["tag_count"], s["id"]))
        if senses:
            entries.append({"lemma": lemma, "senses": [{"id": s["id"], "pos": s["pos_name"], "tags": s["tag_count"],
                                                         "gloss": s["gloss"][:170]} for s in senses]})
    batches = [entries[i:i + FIT_BATCH_LEMMAS] for i in range(0, len(entries), FIT_BATCH_LEMMAS)]
    shapes = {s: {"pos": v[0], "title": v[1], "rule": v[2]} for s, v in SHAPES.items()}
    for b, batch in enumerate(batches, 1):
        bid = f"sf{b:03d}"
        qf.write_json(os.path.join(folder, "in", bid + ".json"), {"batch": bid, "shapes": shapes, "lemmas": batch})
    print(len(entries), "lemmas,", len(batches), "judge batches")


def cmd_fit_apply(args):
    world = qf.load_world()
    folder = os.path.join(qf.WORK, "shapefit")
    fit = read_fit()
    valid = set(SHAPES)
    seen, stats = {}, collections.Counter()
    for path in sorted(glob.glob(os.path.join(folder, "in", "*.json"))):
        bid = os.path.splitext(os.path.basename(path))[0]
        spec = qf.read_json(path)
        expected = {s["id"]: s["pos"] for e in spec["lemmas"] for s in e["senses"]}
        out = os.path.join(folder, "out", bid + ".tsv")
        if not os.path.exists(out):
            stats["batch without output"] += 1
            continue
        with open(out, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\r\n").split("\t")
                if len(p) == 3 and not p[1]:  # a doubled tab
                    p = [p[0], p[2]]
                if len(p) != 2 or p[0] not in expected:
                    stats["bad line"] += 1
                    continue
                shapes = {x.strip() for x in p[1].split(",") if x.strip() and x.strip() != "-"}
                wrong = {x for x in shapes if x not in valid or SHAPES[x][0] != expected[p[0]]}
                if wrong:
                    stats["shape of the other part of speech or unknown"] += len(wrong)
                    shapes -= wrong
                seen[p[0]] = shapes
        stats["senses answered"] = len(seen)
    missing = 0
    for path in sorted(glob.glob(os.path.join(folder, "in", "*.json"))):
        for e in qf.read_json(path)["lemmas"]:
            missing += sum(1 for s in e["senses"] if s["id"] not in seen)
    fit.update(seen)
    with open(FIT_FILE, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("sense_id\tshapes\n")
        for sid in sorted(fit):
            handle.write(sid + "\t" + ",".join(s for s in SHAPES if s in fit[sid]) + "\n")
    print(dict(stats), "senses without an answer:", missing, "rows in the table:", len(fit))


def ask_for(unit, short):
    n = len(unit["senses"])
    if short:
        return ASK_SHORT
    return ASK_ONE_SENSE if n == 1 else ASK_PER_SENSE


def cmd_export(args):
    import written
    world = qf.load_world()
    senses = world["senses"]
    lemmas = qf.read_lemma_list(args.lemmas)
    fit = read_fit()
    written_counts = shape_counts()
    todo = [u for u in units(world, fit, lemmas, written=written_counts)
            if (u["have"] < MIN_ROWS if args.short else u["have"] == 0)]
    wdir = os.path.join(qf.WORK, f"w{args.wave}")
    for sub in ("wr-in", "wr-out", "wr-meta", "wr-label"):
        os.makedirs(os.path.join(wdir, sub), exist_ok=True)
    entries = []
    for u in sorted(todo, key=lambda u: (u["lemma"], u["pos"], u["shape"])):
        for sid in u["senses"]:
            s = senses[sid]
            forms = written.allowed_forms(world, s["lemma"], s["pos"])
            if s["pos_name"] == "Verb" and u["shape"].startswith("v-"):
                forms = [s["lemma"].lower()] if s["lemma"].lower() in forms else forms[:1]
            others = [qf.sense_line(world, o) for o in world["by_lemma"][s["lemma"]] if o != sid][:25]
            entries.append({"sense": sid, "lemma": s["lemma"], "pos": s["pos_name"], "gloss": s["gloss"],
                            "examples": s["examples"][:2], "forms": forms[:14], "others": others, "shape": u["shape"],
                            "shape_title": SHAPES[u["shape"]][1], "shape_rule": SHAPES[u["shape"]][2],
                            "count": ask_for(u, args.short)})
    batches, cur, total = [], [], 0
    for e in entries:
        if total + e["count"] > BATCH_SENTENCES and cur:
            batches.append(cur)
            cur, total = [], 0
        cur.append(e)
        total += e["count"]
    if cur:
        batches.append(cur)
    for b, batch in enumerate(batches, 1):
        bid = f"w{args.wave}r{b:03d}"
        qf.write_json(os.path.join(wdir, "wr-in", bid + ".json"), {"batch": bid, "senses": batch})
    print(len(todo), "units,", len(entries), "entries,", sum(e["count"] for e in entries), "sentences,", len(batches),
          "writer batches")


def probe_token_sets():
    """lemma -> token sets of the sentences of the shape probe set (a written row must not be a near copy of one)."""
    sets = collections.defaultdict(list)
    path = os.path.join(qf.CORPUS, "shapes", "probe.tsv")
    if os.path.exists(path):
        with open(path, encoding="utf-8", newline="") as handle:
            for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
                sets[r["lemma"]].append(textutil.jaccard_tokens(r["sentence"]))
    return sets


def cmd_select(args):
    import fill
    world = qf.load_world()
    senses = world["senses"]
    wdir = os.path.join(qf.WORK, f"w{args.wave}")
    tag = f"quota-w{args.wave}-shape.tsv"
    for folder in ("anchors", "provenance", os.path.join("choosers", "anchors")):
        old = os.path.join(qf.CORPUS, folder, tag)
        if os.path.exists(old):
            os.remove(old)
    blocked = qf.corpus_keys()
    cands = collections.defaultdict(list)
    stats = collections.Counter()
    with open(os.path.join(wdir, "wr-labeled.tsv"), encoding="utf-8") as handle:
        for line in handle:
            p = line.rstrip("\n").split("\t")
            if len(p) != 9:
                stats["row without a shape"] += 1
                continue
            sid, form, intended, sense, conf, _doc, _family, sentence, shape = p
            if sense != intended or conf != "high":
                stats["not confirmed"] += 1
                continue
            if textutil.key_of(sentence) in blocked:
                stats["duplicate of the corpus"] += 1
                continue
            cands[(intended, shape)].append({"form": form, "sentence": sentence, "key": textutil.key_of(sentence),
                                             "tokens": textutil.jaccard_tokens(sentence), "sid": sid})
    existing = fill.existing_token_sets({senses[sense]["lemma"] for sense, _shape in cands})
    probe = probe_token_sets()
    picked = []
    taken = set()
    sets_by_lemma = {}
    for (sense, shape), items in sorted(cands.items()):
        lemma = senses[sense]["lemma"]
        sets = sets_by_lemma.setdefault(lemma, list(existing.get(lemma, [])) + probe.get(lemma, []))
        chosen = 0
        for c in sorted(items, key=lambda c: c["sid"]):
            if chosen >= MAX_PER_SENSE_SHAPE:
                break
            if c["key"] in taken or any(textutil.jaccard(c["tokens"], t) >= 0.8 for t in sets):
                stats["near duplicate"] += 1
                continue
            taken.add(c["key"])
            sets.append(c["tokens"])
            picked.append((sense, shape, c))
            chosen += 1
    import choosers
    ch_ok = fill.chooser_forms_ok(choosers.form_lemmas())
    out = {"anchors": [], "prov": [], "choosers": []}
    for sense, shape, c in sorted(picked, key=lambda t: (senses[t[0]]["lemma"], t[0], t[1], t[2]["sid"])):
        s = senses[sense]
        out["anchors"].append("\t".join([s["lemma"], sense, s["pos_name"], c["sentence"]]))
        out["prov"].append("\t".join([fill.prov_id(s["lemma"], c["sentence"]), "anchors", fill.WRITTEN_SOURCE,
                                      f"quota-w{args.wave}-shape:{shape}"]))
        if c["form"] in ch_ok:
            out["choosers"].append("\t".join([c["form"], s["lemma"], c["sentence"]]))
    fill.write_lines(os.path.join(qf.CORPUS, "anchors", tag), out["anchors"])
    fill.write_lines(os.path.join(qf.CORPUS, "provenance", tag), out["prov"], "\t".join(fill.PROV_COLUMNS))
    fill.write_lines(os.path.join(qf.CORPUS, "choosers", "anchors", tag), out["choosers"])
    per_shape = collections.Counter(shape for _s, shape, _c in picked)
    print(dict(stats), "picked", len(picked), dict(per_shape))


def status(world, lemmas=None):
    """The unit rows of coverage: dicts of `units` with `status` met or short."""
    fit = read_fit()
    if lemmas is None:
        lemmas = sorted({world["senses"][s]["lemma"] for s in fit if s in world["senses"]})
    rows = units(world, fit, lemmas, written=shape_counts())
    for r in rows:
        r["status"] = "met" if r["have"] >= MIN_ROWS else "short"
    return rows


def write_coverage(rows, path=COVERAGE_FILE):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("lemma\tpart_of_speech\tshape\tfitting_senses\twritten_rows\tstatus\n")
        for r in sorted(rows, key=lambda r: (r["lemma"], r["pos"], r["shape"])):
            handle.write("\t".join([r["lemma"], r["pos"], r["shape"], ",".join(r["senses"]), str(r["have"]), r["status"]]) + "\n")


def summary(rows):
    by_shape = collections.defaultdict(collections.Counter)
    for r in rows:
        by_shape[r["shape"]][r["status"]] += 1
    lines = [f"shape units: {len(rows)}, met {sum(1 for r in rows if r['status'] == 'met')}, "
             f"short {sum(1 for r in rows if r['status'] == 'short')}"]
    for shape in SHAPES:
        c = by_shape[shape]
        lines.append(f"  {shape}: met {c['met']}, short {c['short']}")
    return "\n".join(lines)


def cmd_status(args):
    world = qf.load_world()
    lemmas = qf.read_lemma_list(args.lemmas) if args.lemmas else None
    print(summary(status(world, lemmas)))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("fit-export")
    p.add_argument("--lemmas", required=True)
    sub.add_parser("fit-apply")
    p = sub.add_parser("export")
    p.add_argument("wave", type=qf.wave_id)
    p.add_argument("--lemmas", required=True)
    p.add_argument("--short", action="store_true", help="only the units below the minimum (a second round)")
    sub.add_parser("select").add_argument("wave", type=qf.wave_id)
    sub.add_parser("status").add_argument("--lemmas", default="")
    a = ap.parse_args()
    {"fit-export": cmd_fit_export, "fit-apply": cmd_fit_apply, "export": cmd_export, "select": cmd_select,
     "status": cmd_status}[a.cmd](a)
