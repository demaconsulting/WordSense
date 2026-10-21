#!/usr/bin/env python3
"""Second, independent annotator: UPOS only, through a local Ollama server (Python standard library only).

  annotate_second.py --sentences SENTENCES.tsv --workdir DIR [--host http://localhost:11434] [--model gemma4:26b]

Request: POST /api/chat, stream=false, think=false, temperature 0, fixed seed, JSON-schema constrained output
({"sentences":[{"sid":..., "tags":["0:DET","1:NOUN", ...]}]}), 10 sentences per request, 4 requests in flight.
The system prompt is GUIDELINE.md plus the shared few-shot examples (prompts/fewshot.txt, UPOS only).
Every response is validated (sentence ids, token count, index order, tag set). Sentences that fail are asked once more
(together, then one per request if still failing). Raw responses go to DIR/second/log.jsonl.
Checkpoints every finished request to DIR/second/partial.jsonl (a rerun of the same command resumes where it stopped).
Writes DIR/second/second.tsv (uid, idx, form, upos) and DIR/second/meta.json.
"""
import argparse
import concurrent.futures
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import ROOT, UPOS, read_tsv, write_json, write_tsv
from prepare_primary import load_fewshot

SEED = 42
TAG_PATTERN = "^[0-9]+:(" + "|".join(UPOS) + ")$"
SCHEMA = {
    "type": "object",
    "properties": {"sentences": {"type": "array", "items": {
        "type": "object",
        "properties": {"sid": {"type": "string"},
                       "tags": {"type": "array", "items": {"type": "string", "pattern": TAG_PATTERN}}},
        "required": ["sid", "tags"]}}},
    "required": ["sentences"],
}


def system_prompt():
    guideline = open(os.path.join(ROOT, "GUIDELINE.md"), encoding="utf-8").read()
    shots = []
    for n, ex in enumerate(load_fewshot(), 1):
        shots.append(f"S{n}: " + " ".join(f"{k}:{t[0]}" for k, t in enumerate(ex)))
    answers = [{"sid": f"S{n}", "tags": [f"{k}:{t[2]}" for k, t in enumerate(ex)]} for n, ex in enumerate(load_fewshot(), 1)]
    return (
        "You are a part-of-speech tagger. Tag every token of every sentence with exactly one UPOS tag, following the "
        "annotation guideline below strictly. Sentences are pre-tokenized: never re-tokenize, merge or skip tokens. "
        "Tokens are shown as idx:token. Answer only with JSON of the form "
        '{"sentences":[{"sid":"S1","tags":["0:DET","1:NOUN"]}]} with one "idx:TAG" string per token, in order, '
        "using only these tags: " + " ".join(UPOS) + ".\n\n=== ANNOTATION GUIDELINE ===\n" + guideline +
        "\n=== WORKED EXAMPLES (input, then correct answer) ===\n" + "\n".join(shots) + "\n" +
        json.dumps({"sentences": answers}, ensure_ascii=False) + "\n")


def post(host, payload, timeout=900):
    req = urllib.request.Request(host + "/api/chat", data=json.dumps(payload).encode("utf-8"),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def ask(host, model, system, sents, num_ctx, hint=False):
    user = "Tag these sentences.\n" + "\n".join(
        f"{s['uid']}: " + " ".join(f"{k}:{t}" for k, t in enumerate(s["tokens"].split(" "))) for s in sents)
    if hint:
        user += ("\nEach sentence needs exactly one tag per token listed. Token counts: " +
                 ", ".join(f"{s['uid']}={len(s['tokens'].split(' '))}" for s in sents) + ".")
    payload = {"model": model, "stream": False, "think": False, "format": SCHEMA, "keep_alive": "30m",
               "options": {"temperature": 0, "seed": SEED, "num_ctx": num_ctx, "num_predict": 4096},
               "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
    t0 = time.time()
    resp = post(host, payload)
    return resp["message"]["content"], round(time.time() - t0, 1), resp.get("eval_count"), resp.get("prompt_eval_count")


def parse(content, sents):
    """Returns {uid: [tags]} for the sentences that validate, and the list of failed uids."""
    try:
        data = json.loads(content)["sentences"]
    except Exception:
        return {}, [s["uid"] for s in sents]
    got = {}
    for item in data:
        got[item.get("sid")] = item.get("tags", [])
    ok, bad = {}, []
    for s in sents:
        n = len(s["tokens"].split(" "))
        tags = got.get(s["uid"])
        good = isinstance(tags, list) and len(tags) == n
        if good:
            parsed = []
            for k, t in enumerate(tags):
                idx, _, tag = str(t).partition(":")
                if idx != str(k) or tag not in UPOS:
                    good = False
                    break
                parsed.append(tag)
        if good:
            ok[s["uid"]] = parsed
        else:
            bad.append(s["uid"])
    return ok, bad


def ask_retry(*a, **k):
    last = None
    for attempt in range(4):
        try:
            return ask(*a, **k)
        except Exception as e:  # network or server hiccup: wait and try again
            last = e
            time.sleep(15 * (attempt + 1))
    raise last


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--host", default="http://localhost:11434")
    ap.add_argument("--model", default="gemma4:26b")
    ap.add_argument("--per-request", type=int, default=10)
    ap.add_argument("--concurrency", type=int, default=4)
    ap.add_argument("--num-ctx", type=int, default=16384)
    ap.add_argument("--limit", type=int, default=0, help="only the first N sentences (smoke test)")
    ap.add_argument("--only-missing", action="store_true", help="(kept for compatibility: a run always resumes from its checkpoint)")
    ap.add_argument("--hint-counts", action="store_true", help="re-ask prompts also state the token count of each sentence")
    ap.add_argument("--export-only", action="store_true",
                    help="no network: write second.tsv from the checkpoint and stop (sentences without a result get no second tag, status 'unchecked')")
    args = ap.parse_args()
    rows = read_tsv(args.sentences)
    if args.limit:
        rows = rows[:args.limit]
    out_dir = os.path.join(args.workdir, "second")
    os.makedirs(out_dir, exist_ok=True)
    partial_path, log_path = os.path.join(out_dir, "partial.jsonl"), os.path.join(out_dir, "log.jsonl")
    results = {}
    if os.path.exists(partial_path):  # checkpoint of an earlier (interrupted) run
        for line in open(partial_path, encoding="utf-8"):
            if line.strip():
                e = json.loads(line)
                results[e["uid"]] = e["tags"]
    elif os.path.exists(os.path.join(out_dir, "second.tsv")):  # results of a run that finished before checkpoints existed
        for r in read_tsv(os.path.join(out_dir, "second.tsv")):
            results.setdefault(r["uid"], []).append(r["upos"])
    todo = [s for s in rows if s["uid"] not in results]
    if args.export_only:
        out = [{"uid": s["uid"], "idx": k, "form": f, "upos": t} for s in rows if s["uid"] in results
               for k, (f, t) in enumerate(zip(s["tokens"].split(" "), results[s["uid"]]))]
        write_tsv(os.path.join(out_dir, "second.tsv"), out, ["uid", "idx", "form", "upos"])
        print(f"exported {len(results)} sentences from the checkpoint; {len(todo)} sentences have no second annotation")
        return
    print(f"{len(results)} sentences already done, {len(todo)} to do", flush=True)
    system = system_prompt()
    chunks = [todo[i:i + args.per_request] for i in range(0, len(todo), args.per_request)]

    def work(chunk):
        entries = []
        content, secs, evals, pevals = ask_retry(args.host, args.model, system, chunk, args.num_ctx, hint=args.hint_counts)
        ok, bad = parse(content, chunk)
        entries.append({"uids": [s["uid"] for s in chunk], "attempt": 1, "seconds": secs, "eval_count": evals,
                        "prompt_eval_count": pevals, "failed": bad, "response": content})
        if bad:
            again = [s for s in chunk if s["uid"] in bad]
            content, secs, evals, pevals = ask_retry(args.host, args.model, system, again, args.num_ctx, hint=args.hint_counts)
            ok2, bad = parse(content, again)
            ok.update(ok2)
            entries.append({"uids": [s["uid"] for s in again], "attempt": 2, "seconds": secs, "eval_count": evals,
                            "prompt_eval_count": pevals, "failed": bad, "response": content})
            for s in [s for s in again if s["uid"] in bad]:
                content, secs, evals, pevals = ask_retry(args.host, args.model, system, [s], args.num_ctx, hint=args.hint_counts)
                ok3, bad3 = parse(content, [s])
                ok.update(ok3)
                entries.append({"uids": [s["uid"]], "attempt": 3, "seconds": secs, "eval_count": evals,
                                "prompt_eval_count": pevals, "failed": bad3, "response": content})
        return ok, entries

    t0 = time.time()
    n_req = n_re = n_single = 0
    failed_chunks = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as ex, \
            open(partial_path, "a", encoding="utf-8", newline="\n") as pf, open(log_path, "a", encoding="utf-8", newline="\n") as lf:
        futures = {ex.submit(work, c): c for c in chunks}
        for done_n, fut in enumerate(concurrent.futures.as_completed(futures), 1):
            try:
                ok, entries = fut.result()
            except Exception as e:  # leave the chunk for the next resume
                failed_chunks += 1
                print("chunk failed:", e, flush=True)
                continue
            for uid, tags in ok.items():
                results[uid] = tags
                pf.write(json.dumps({"uid": uid, "tags": tags}) + "\n")
            for e in entries:
                lf.write(json.dumps(e, ensure_ascii=False) + "\n")
                n_req += 1
                n_re += e["attempt"] == 2
                n_single += e["attempt"] == 3
            pf.flush()
            lf.flush()
            if done_n % 10 == 0 or done_n == len(chunks):
                rate = (time.time() - t0) / done_n
                print(f"chunk {done_n}/{len(chunks)}; {len(results)} sentences done; eta {round(rate * (len(chunks) - done_n) / 60)} min", flush=True)
    out, missing = [], []
    for s in rows:
        tags = results.get(s["uid"])
        if tags is None:
            missing.append(s["uid"])
            continue
        for k, (form, tag) in enumerate(zip(s["tokens"].split(" "), tags)):
            out.append({"uid": s["uid"], "idx": k, "form": form, "upos": tag})
    write_tsv(os.path.join(out_dir, "second.tsv"), out, ["uid", "idx", "form", "upos"])
    tags_info = json.loads(urllib.request.urlopen(args.host + "/api/tags", timeout=30).read().decode("utf-8"))
    digest = next((m["digest"] for m in tags_info["models"] if m["name"] == args.model), "")
    meta_path = os.path.join(out_dir, "meta.json")
    runs = json.load(open(meta_path, encoding="utf-8")).get("runs", []) if os.path.exists(meta_path) else []
    runs.append({"requests": n_req, "second_attempts": n_re, "single_sentence_attempts": n_single, "failed_chunks": failed_chunks,
                 "sentences_done_in_this_run": len(todo) - len(missing), "wall_seconds": round(time.time() - t0, 1)})
    write_json(meta_path, {"model": args.model, "digest": digest, "host": args.host, "seed": SEED, "temperature": 0, "think": False,
                           "num_ctx": args.num_ctx, "sentences_per_request": args.per_request, "concurrency": args.concurrency,
                           "missing_sentences": missing, "runs": runs})
    print(f"done: {len(rows) - len(missing)}/{len(rows)} sentences, missing {len(missing)}, {round(time.time() - t0)}s")


if __name__ == "__main__":
    main()
