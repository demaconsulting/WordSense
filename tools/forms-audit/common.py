"""Shared helpers of the forms-audit tools: paths, categories and the readers of the data tables and the TSV files.

The audit batches, their hidden keys and the answers are kept OUTSIDE the repository (the hidden key must not be reachable
by the adjudicating agents). The folder is chosen with --home, or the environment variable FORMS_AUDIT_HOME; without either,
`artifacts/forms-audit` (git-ignored) is used, which is fine for a dry run only.

Layout below the home folder:

  audit/audit-SEED.md                the reports of `audit-forms` (input of export.py audit)
  work/audit/batch-NN.json           audit batches (input of the agents), recheck-01.json for the second opinion
  work/audit-out/batch-NN.tsv        the answers of the adjudicator (recheck-01.tsv for the second opinion)
  work/audit-*.tsv                   the results of audit_ingest.py (review, invalid, recheck lists)
  key/audit-key.tsv                  the hidden keys (never shown to an agent), audit-recheck-key.tsv for the recheck
  reports/                           forms-authored-audit-pending.tsv (export.py audit): a report, never committed
"""
import glob
import hashlib
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DATA = os.path.join(REPO, "data")

BATCH_TASKS = 100
SEED = "forms-authoring-v1"  # the seed of the controls and of the order of the batches; changing it changes the batches

# POS letter of the data tables -> name used in the prompts, and the categories asked for it
POS_NAME = {"n": "noun", "v": "verb", "a": "adjective", "r": "adverb"}
CATEGORIES = {
    "n": ["plural"],
    "v": ["third", "past", "participle", "gerund"],
    "a": ["comparative", "superlative"],
    "r": ["comparative", "superlative"],
}


def home_dir(arg=None):
    path = arg or os.environ.get("FORMS_AUDIT_HOME") or os.path.join(REPO, "artifacts", "forms-audit")
    return os.path.abspath(path)


def work_dir(home, *parts):
    path = os.path.join(home, "work", *parts)
    os.makedirs(path, exist_ok=True)
    return path


def report_path(home, name):
    """A report file below `<home>/reports`: the reports are work outputs and never go into the repository."""
    return os.path.join(home, "reports", name)


def add_home_argument(parser):
    parser.add_argument("--home", default=None,
                        help="work folder outside the repository (default: $FORMS_AUDIT_HOME or artifacts/forms-audit)")


def stable_hash(*parts):
    return hashlib.sha256("|".join((SEED,) + tuple(str(p) for p in parts)).encode("utf-8")).hexdigest()


def read_tsv(path):
    """Rows of a TSV file as dicts of the header names; comment lines (#) and blank lines are skipped."""
    with open(path, encoding="utf-8", newline="") as handle:
        lines = [ln.rstrip("\r\n") for ln in handle]
    rows = [ln.split("\t") for ln in lines if ln.strip() and not ln.startswith("#")]
    names = rows[0]
    return [dict(zip(names, r + [""] * (len(names) - len(r)))) for r in rows[1:]]


def write_tsv(path, names, rows, comments=()):
    """Writes a TSV file with comment lines, then the header (omitted when names is None) and the rows."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        for c in comments:
            handle.write("# " + c + "\n")
        if names is not None:
            handle.write("\t".join(names) + "\n")
        for r in rows:
            handle.write("\t".join(str(x) for x in r) + "\n")


def feature_kinds(features):
    """The categories (list) of a form row of data/form, or an empty list for a base form."""
    parts = features.split("+")
    if "Number=Plur" in parts:
        return ["plural"]
    if "Degree=Cmp" in parts:
        return ["comparative"]
    if "Degree=Sup" in parts:
        return ["superlative"]
    if "VerbForm=Ger" in parts:
        return ["gerund"]
    if "Person=3" in parts:
        return ["third"]
    if "Tense=Past" in parts and "VerbForm=Part" in parts:
        return ["past", "participle"]
    if "Tense=Past" in parts:
        return ["past"]
    if "VerbForm=Part" in parts:
        return ["participle"]
    return []


def read_form_table(directory):
    """Rows (spelling, entry id or 'lemma|pos', features, provenance) of the form table in a data folder."""
    rows = []
    folder = os.path.join(directory, "form")
    for name in sorted(os.listdir(folder)):
        with open(os.path.join(folder, name), encoding="utf-8", newline="") as handle:
            lines = handle.read().split("\n")
        for line in lines[1:]:
            line = line.rstrip("\r")
            if not line:
                continue
            c = line.split("\t") + [""] * 9
            spelling, entry_id, features, lemma, pos, prov = c[0], c[2], c[3], c[6], c[7], c[8]
            rows.append((spelling, entry_id if entry_id else f"{lemma}|{pos}", features, prov))
    return rows


def read_entries(directory):
    """entry id -> (lemma, pos letter)."""
    entries = {}
    folder = os.path.join(directory, "entry")
    for name in sorted(os.listdir(folder)):
        with open(os.path.join(folder, name), encoding="utf-8") as handle:
            lines = handle.read().split("\n")
        for line in lines[1:]:
            if line.strip():
                c = line.rstrip("\r").split("\t")
                entries[c[0]] = (c[1], c[2])
    return entries


def lemma_pos_of(key, entries):
    if "|" in key:
        lemma, pos = key.rsplit("|", 1)
        return lemma, pos
    return entries[key]


def batch_files(folder):
    return sorted(glob.glob(os.path.join(folder, "batch-*.json")))
