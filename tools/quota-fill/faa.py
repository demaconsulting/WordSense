"""Extracts the text of the FAA documents that the owner accepted (17 U.S.C. 105, US federal government work) into the pool.

  python faa.py extract      text/faa-*.txt in tools/real-text/cache/quota/ and faa-sources.tsv (read by pool.py)

Included: the Aviation Maintenance Technician Handbooks (General, Airframe, Powerplant), the Pilot's Handbook of
Aeronautical Knowledge, the Airplane, Helicopter, Glider and Balloon Flying Handbooks, Plane Sense, AC 43.13-1B and
AC 43.13-2B. Excluded entirely: the Parachute Rigger Handbook, the Aviation Weather Handbook, Aerodynamics for Naval
Aviators and the MC-4 manual. Text only: the first pages (front matter and acknowledgments) are skipped, and every page that
carries a credit marker (courtesy of, used with permission, copyright, a copyright sign, reproduced, photo credits) is
dropped whole, so no passage credited to a third party remains; figure and table captions are removed by the sentence
filter of the pool.
"""
import collections
import multiprocessing
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qf

SRC = r"C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\faa\cache"
BASIS = ("US federal government work, 17 U.S.C. \u00a7105 (public domain in the United States); text only, passages "
         "credited to a third party excluded; ")
BASE = "https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/"
DOCS = [
    ("faa-h-8083-30b", ["004_amtg_handbook.pdf"], "Aviation Maintenance Technician Handbook - General (FAA-H-8083-30B, 2023)",
     BASE + "amtg_handbook.pdf", "2023"),
    ("faa-h-8083-31b", ["005_FAA-H-8083-31B_Aviation_Maintenance_Technician_Handbook.pdf"],
     "Aviation Maintenance Technician Handbook - Airframe (FAA-H-8083-31B, 2023)",
     BASE + "FAA-H-8083-31B_Aviation_Maintenance_Technician_Handbook.pdf", "2023"),
    ("faa-h-8083-32b", ["006_amt_powerplant_handbook.pdf"], "Aviation Maintenance Technician Handbook - Powerplant (FAA-H-8083-32B, 2023)",
     BASE + "amt_powerplant_handbook.pdf", "2023"),
    ("faa-h-8083-25c", ["039_faa-h-8083-25c.pdf"], "Pilot's Handbook of Aeronautical Knowledge (FAA-H-8083-25C, 2023)",
     BASE + "faa-h-8083-25c.pdf", "2023"),
    ("faa-h-8083-3c", ["full_000_00_afh_full.pdf"], "Airplane Flying Handbook (FAA-H-8083-3C, 2021)",
     BASE + "airplane_handbook/00_afh_full.pdf", "2021"),
    ("faa-h-8083-21b", ["full_003_helicopter_flying_handbook.pdf"], "Helicopter Flying Handbook (FAA-H-8083-21B, 2019)",
     BASE + "helicopter_flying_handbook/helicopter_flying_handbook.pdf", "2019"),
    ("faa-h-8083-13b", ["full_002_Glider-Flying-Handbook.pdf"], "Glider Flying Handbook (FAA-H-8083-13B, 2024)",
     "https://www.faa.gov/sites/faa.gov/files/Glider-Flying-Handbook.pdf", "2024"),
    ("faa-h-8083-11b", [f"balloon_{i:02d}_bfh_chapter_{i + 1}.pdf" for i in range(11)],
     "Balloon Flying Handbook (FAA-H-8083-11B, 2024)", BASE + "Balloon_Flying_Handbook", "2024"),
    ("faa-h-8083-19a", ["019_faa-h-8083-19A.pdf"], "Plane Sense - General Aviation Information (FAA-H-8083-19A, 2008)",
     "https://www.faa.gov/sites/faa.gov/files/regulations_policies/handbooks_manuals/aviation/faa-h-8083-19A.pdf",
     "2008"),
    ("faa-ac-43-13-1b", ["047_AC_43.13-1B_w-chg1.pdf"],
     "Advisory Circular 43.13-1B, Acceptable Methods, Techniques, and Practices - Aircraft Inspection and Repair (Change 1, 1998)",
     "https://www.faa.gov/documentLibrary/media/Advisory_Circular/AC_43.13-1B_w-chg1.pdf", "1998"),
    ("faa-ac-43-13-2b", ["048_AC_43.13-2B.pdf"],
     "Advisory Circular 43.13-2B, Acceptable Methods, Techniques, and Practices - Aircraft Alterations (2008)",
     "https://www.faa.gov/documentLibrary/media/Advisory_Circular/AC%2043.13-2B.pdf", "2008"),
]
MARKERS = re.compile(r"courtesy|permission|copyright|\u00a9|\(c\)|reproduced|photo by|photograph by|image by|used by|"
                     r"all rights reserved|reprinted", re.I)
SKIP_PAGES = 15


def page_texts(path):
    from pypdf import PdfReader
    reader = PdfReader(path)
    out = []
    for page in reader.pages:
        try:
            out.append(page.extract_text() or "")
        except Exception:
            out.append("")
    return out


def extract_one(item):
    doc_id, files, title, url, year = item
    pages = []
    for name in files:
        pages.extend(page_texts(os.path.join(SRC, name)))
    counts = collections.Counter()
    for t in pages:
        for line in set(l.strip() for l in t.split("\n")):
            counts[re.sub(r"\d+", "#", line)] += 1
    frequent = {k for k, v in counts.items() if v >= max(5, 0.08 * len(pages)) and len(k) < 120}
    skip_front = SKIP_PAGES if len(files) == 1 else 0
    kept, dropped = [], 0
    for number, t in enumerate(pages):
        if number < skip_front:
            continue
        if MARKERS.search(t):
            dropped += 1
            continue
        lines = [l.strip() for l in t.split("\n")]
        lines = [l for l in lines if l and re.sub(r"\d+", "#", l) not in frequent and not re.fullmatch(r"[\d\s.,-]+", l)]
        text = " ".join(lines)
        text = re.sub(r"(\w)- (\w)", r"\1\2", text)
        text = text.replace("\u00ad", "")
        if len(text) > 200:
            kept.append(text)
    path = os.path.join(qf.QDIR, "text", doc_id + ".txt")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n\n".join(kept))
    return doc_id, len(pages), len(kept), dropped


def main():
    os.makedirs(os.path.join(qf.QDIR, "text"), exist_ok=True)
    with multiprocessing.Pool(6) as pool_:
        results = pool_.map(extract_one, DOCS)
    for r in results:
        print(r)
    import hashlib
    path = os.path.join(qf.QDIR, "sources.tsv")
    with open(path, encoding="utf-8", newline="") as handle:
        lines = [l.rstrip("\n") for l in handle if l.strip()]
    lines = [l for l in lines if l.split("\t")[1] != "faa"]
    for doc_id, files, title, url, year in DOCS:
        with open(os.path.join(qf.QDIR, "text", doc_id + ".txt"), "rb") as handle:
            digest = hashlib.sha256(handle.read()).hexdigest()
        lines.append("\t".join([doc_id, "faa", title, url, "Federal Aviation Administration", year,
                                BASIS + "document " + title + "; " + url, "https://www.faa.gov/about/website/copyright",
                                "2026-10-20", "", digest, ""]))
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
