#!/usr/bin/env python3
"""Build the simplified-technical-English (STE) challenge sentences of the TARGETED part of the open corpus.

  build_ste_probe.py

The 32 sentences were written by the project: 30 probe sentences of imperative technical instructions (component names, part
numbers, units, warnings, negated imperatives) and two "Mount ... home" sentences that exercise the verb and adverb readings of
`mount` and `home`. Their parts of speech were annotated independently by two annotators with the 10-class WordSense tags, who
agreed on every token (the gold classes are embedded below). The corpus tags (UPOS, lemma, features, collapsed tag) follow
GUIDELINE.md version 4 and are checked against that gold: the collapsed class of every token must equal the gold class, except
the two conventions of the corpus guideline that the 10-class tags cannot express (the infinitive marker `to` is PART and `not`
is PART; the gold had ADP and ADV).

The script writes targeted/build/sentences.tsv and targeted/annotations/final.tsv and then calls split_data.py --part targeted, so
the data file is produced by the same code path as every other part. It also records the one source document in sources.tsv.
The split is the document hash rule: the source id `wordsense-ste-probe` is in bucket 95, so every sentence is in the test part
(the challenge set).
"""
import hashlib
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import COLLAPSE, ROOT, TARGETED, check_tokenization, read_tsv, sha256_text, write_tsv

SOURCE_ID = "wordsense-ste-probe"
SELECTION = "genre=technical-instructions"
QUERY = "written:ste-probe"
REVIEWED = "unanimous"  # the two independent annotators agreed on the part of speech of every token

TAGS = {"N": "NOUN", "V": "VERB", "J": "ADJ", "B": "ADV", "P": "ADP", "C": "CCONJ", "S": "SCONJ", "D": "DET", "R": "PRON",
        "A": "AUX", "T": "PART", "U": "NUM", "O": "PUNCT", "Y": "SYM"}
FEATS = {"": "_", "imp": "Mood=Imp|VerbForm=Fin", "inf": "VerbForm=Inf", "p3": "Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin",
         "sg": "Number=Sing", "pl": "Number=Plur", "art": "PronType=Art", "arts": "Number=Sing|PronType=Art",
         "it": "Number=Sing|Person=3|PronType=Prs"}

# Corpus tags that differ from the 10-class gold on purpose: (gold class, corpus collapsed class)
GUIDELINE_DIFFERENCES = {"not": ("B", "T"), "to": ("P", "T")}

# text; the tokens as form|TAG[|features[|lemma]] (default lemma = the lower-case form); the gold classes of the two annotators
SENTENCES = [
    ("Mount the Theta Arm to the Gantry Robot.",
     "Mount|V|imp the|D|art Theta|N|sg Arm|N|sg to|P the|D|art Gantry|N|sg Robot|N|sg .|O", "VDNNPDNNO"),
    ("Install the seal (P/N 4471-20) in the Hydraulic Pump Assembly.",
     "Install|V|imp the|D|art seal|N|sg (|O P|N /|O N|N 4471-20|U )|O in|P the|D|art Hydraulic|J Pump|N|sg Assembly|N|sg .|O",
     "VDNONONUOPDJNNO"),
    ("Remove the Main Control Panel from the frame.",
     "Remove|V|imp the|D|art Main|J Control|N|sg Panel|N|sg from|P the|D|art frame|N|sg .|O", "VDJNNPDNO"),
    ("Check the oil level.", "Check|V|imp the|D|art oil|N|sg level|N|sg .|O", "VDNNO"),
    ("Torque the bolts to 25 N\u00b7m.",
     "Torque|V|imp the|D|art bolts|N|pl|bolt to|P 25|U N|N \u00b7|Y m|N .|O", "VDNPUNONO"),
    ("Clean the surface with a dry cloth.",
     "Clean|V|imp the|D|art surface|N|sg with|P a|D|arts dry|J cloth|N|sg .|O", "VDNPDJNO"),
    ("Lift the Z-Axis Carriage with a hoist.",
     "Lift|V|imp the|D|art Z-Axis|N|sg Carriage|N|sg with|P a|D|arts hoist|N|sg .|O", "VDNNPDNO"),
    ("Turn the knob clockwise until it stops.",
     "Turn|V|imp the|D|art knob|N|sg clockwise|B until|S it|R|it stops|V|p3|stop .|O", "VDNBCRVO"),
    ("Set the pressure to 3000 psi.", "Set|V|imp the|D|art pressure|N|sg to|P 3000|U psi|N .|O", "VDNPUNO"),
    ("Lock the Theta Arm in position.",
     "Lock|V|imp the|D|art Theta|N|sg Arm|N|sg in|P position|N|sg .|O", "VDNNPNO"),
    ("Support the load with a jack.", "Support|V|imp the|D|art load|N|sg with|P a|D|arts jack|N|sg .|O", "VDNPDNO"),
    ("Position the bearing in the housing.",
     "Position|V|imp the|D|art bearing|N|sg in|P the|D|art housing|N|sg .|O", "VDNPDNO"),
    ("Attach the cable to the Gantry Robot.",
     "Attach|V|imp the|D|art cable|N|sg to|P the|D|art Gantry|N|sg Robot|N|sg .|O", "VDNPDNNO"),
    ("Record the readings in the log.",
     "Record|V|imp the|D|art readings|N|pl|reading in|P the|D|art log|N|sg .|O", "VDNPDNO"),
    ("Do not touch the hot surface.",
     "Do|A|imp|do not|T||not touch|V|inf the|D|art hot|J surface|N|sg .|O", "ABVDJNO"),
    ("WARNING: Do not operate the motor without the guard.",
     "WARNING|N|sg :|O Do|A|imp|do not|T||not operate|V|inf the|D|art motor|N|sg without|P the|D|art guard|N|sg .|O",
     "NOABVDNPDNO"),
    ("Make sure that the valve is closed.",
     "Make|V|imp sure|J that|S the|D|art valve|N|sg is|V|p3|be closed|J .|O", "VJCDNVJO"),
    ("Make sure that the arm does not touch the frame.",
     "Make|V|imp sure|J that|S the|D|art arm|N|sg does|A|p3|do not|T||not touch|V|inf the|D|art frame|N|sg .|O",
     "VJCDNABVDNO"),
    ("If the indicator light comes on, stop the motor.",
     "If|S the|D|art indicator|N|sg light|N|sg comes|V|p3|come on|B ,|O stop|V|imp the|D|art motor|N|sg .|O",
     "CDNNVBOVDNO"),
    ("Flush the system with clean fluid.",
     "Flush|V|imp the|D|art system|N|sg with|P clean|J fluid|N|sg .|O", "VDNPJNO"),
    ("Bleed the air from the brake line.",
     "Bleed|V|imp the|D|art air|N|sg from|P the|D|art brake|N|sg line|N|sg .|O", "VDNPDNNO"),
    ("Tap the housing lightly to seat the bearing.",
     "Tap|V|imp the|D|art housing|N|sg lightly|B to|T seat|V|inf the|D|art bearing|N|sg .|O", "VDNBPVDNO"),
    ("Run the pump for 5 minutes.", "Run|V|imp the|D|art pump|N|sg for|P 5|U minutes|N|pl|minute .|O", "VDNPUNO"),
    ("Connect the lead to the terminal.",
     "Connect|V|imp the|D|art lead|N|sg to|P the|D|art terminal|N|sg .|O", "VDNPDNO"),
    ("Do not use a damaged seal.",
     "Do|A|imp|do not|T||not use|V|inf a|D|arts damaged|J seal|N|sg .|O", "ABVDJNO"),
    ("Tighten the nut by hand, then torque it to 12 N\u00b7m.",
     "Tighten|V|imp the|D|art nut|N|sg by|P hand|N|sg ,|O then|B torque|V|imp it|R|it to|P 12|U N|N \u00b7|Y m|N .|O",
     "VDNPNOBVRPUNONO"),
    ("Apply grease to the mount.", "Apply|V|imp grease|N|sg to|P the|D|art mount|N|sg .|O", "VNPDNO"),
    ("Remove the mount from the Theta Arm.",
     "Remove|V|imp the|D|art mount|N|sg from|P the|D|art Theta|N|sg Arm|N|sg .|O", "VDNPDNNO"),
    ("Check that the seal is not cracked.",
     "Check|V|imp that|S the|D|art seal|N|sg is|V|p3|be not|T||not cracked|J .|O", "VCDNVBJO"),
    ("Set the Hydraulic Pump Assembly to the OFF position.",
     "Set|V|imp the|D|art Hydraulic|J Pump|N|sg Assembly|N|sg to|P the|D|art OFF|J position|N|sg .|O", "VDJNNPDJNO"),
    ("Mount the robot arm and slide it home.",
     "Mount|V|imp the|D|art robot|N|sg arm|N|sg and|C slide|V|imp it|R|it home|B .|O", "VDNNCVRBO"),
    ("Mount the wild horse and ride it home.",
     "Mount|V|imp the|D|art wild|J horse|N|sg and|C ride|V|imp it|R|it home|B .|O", "VDJNCVRBO"),
]


def parse(spec):
    """Turn 'form|TAG[|features[|lemma]]' items into (form, UPOS, lemma, features) rows."""
    tokens = []
    for item in spec.split(" "):
        parts = item.split("|")
        form, tag = parts[0], parts[1]
        feats = FEATS[parts[2]] if len(parts) > 2 else "_"
        lemma = parts[3] if len(parts) > 3 and parts[3] else form.lower()
        tokens.append((form, TAGS[tag], lemma, feats))
    return tokens


def main():
    rows, tokens_out, source_text = [], [], []
    for number, (text, spec, gold) in enumerate(SENTENCES, 1):
        tokens = parse(spec)
        forms = [t[0] for t in tokens]
        if not check_tokenization(text, forms):
            sys.exit(f"sentence {number}: the tokens do not concatenate to the text")
        if len(tokens) != len(gold):
            sys.exit(f"sentence {number}: {len(tokens)} tokens but {len(gold)} gold classes")
        uid = f"T{number:03d}"
        for index, ((form, upos, lemma, feats), letter) in enumerate(zip(tokens, gold)):
            collapsed = COLLAPSE[upos]
            allowed = GUIDELINE_DIFFERENCES.get(form.lower())
            if collapsed != letter and (allowed is None or (letter, collapsed) != allowed):
                sys.exit(f"sentence {number}: token {form}: collapsed class {collapsed} differs from the gold class {letter}")
            tokens_out.append({"uid": uid, "idx": index, "form": form, "lemma": lemma, "upos": upos, "collapsed": collapsed,
                               "feats": feats, "status": REVIEWED, "primary_upos": upos, "second_upos": upos})
        rows.append({"uid": uid, "sent_id": f"{SOURCE_ID}-s{number}", "source_id": SOURCE_ID, "family": "project-written",
                     "group": "technical-instructions", "selection": SELECTION, "query": QUERY, "ntokens": len(tokens),
                     "text": text, "tokens": " ".join(forms)})
        source_text.append(text)
    build = os.path.join(TARGETED, "build")
    annotations = os.path.join(TARGETED, "annotations")
    write_tsv(os.path.join(build, "sentences.tsv"), rows,
              ["uid", "sent_id", "source_id", "family", "group", "selection", "query", "ntokens", "text", "tokens"])
    write_tsv(os.path.join(annotations, "final.tsv"), tokens_out,
              ["uid", "idx", "form", "lemma", "upos", "collapsed", "feats", "status", "primary_upos", "second_upos"])

    # The one source document of this set
    sources_path = os.path.join(ROOT, "sources.tsv")
    sources = read_tsv(sources_path)
    columns = list(sources[0].keys())
    sources = [r for r in sources if r["source_id"] != SOURCE_ID]
    sources.append({
        "source_id": SOURCE_ID, "family": "project-written", "title": "WordSense simplified-technical-English probe sentences",
        "url": "", "author": "WordSense project (written by the project, annotated by LLM annotators)", "year": "2026",
        "license_basis": "Written by the project; the project owner dedicates the sentences to the public domain under CC0 1.0",
        "terms_url": "https://creativecommons.org/publicdomain/zero/1.0/", "retrieved": "2026-10-21", "raw_sha256": "",
        "text_sha256": sha256_text("\n".join(source_text)), "sentences": str(len(rows))})
    write_tsv(sources_path, sources, columns)

    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "split_data.py"), "--part", "targeted",
                    "--sentences", os.path.join(build, "sentences.tsv"), "--workdir", annotations], check=True)
    print(len(rows), "sentences", len(tokens_out), "tokens; document bucket",
          int(hashlib.sha256(SOURCE_ID.encode("utf-8")).hexdigest()[:8], 16) % 100)


if __name__ == "__main__":
    main()
