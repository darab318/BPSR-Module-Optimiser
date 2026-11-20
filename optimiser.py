"""
BPSR Module Optimiser
---------------------
Finds the best 5-module loadouts that meet the stat minimums you choose.

Input: ModulesSaveData.json, the file ZDPS writes to its Data folder after it
has read your module inventory from game traffic (Npcap capture of the
SyncContainerData message). Point this tool at that file.

Usage examples:
  python optimiser.py ModulesSaveData.json --min 2104=16
  python optimiser.py ModulesSaveData.json --min 2104=16 --min 2204=8 --top 20
  python optimiser.py ModulesSaveData.json --min 2104=16 --names stat_names.json

Each --min is STAT_ID=MINIMUM. Stat IDs are the same numbers ZDPS uses.
A loadout is only listed if every stat you set reaches its minimum.
"""

import argparse
import itertools
import json
import sys
from collections import defaultdict

NUM_MODULES = 5          # loadout size in the game
MAX_PER_TYPE = 5         # keep at most this many identical modules (same as ZDPS)
BEAM_WIDTH = 20000       # candidates kept per depth; higher = slower but more thorough
STAT_CAP = 50            # totals are capped here, same as ZDPS


# ---------------------------------------------------------------- loading ----

def _get(d, *keys):
    """Read a key from a dict, accepting either case (ZDPS JSON uses C# names)."""
    for k in keys:
        if k in d:
            return d[k]
        for dk in d:
            if dk.lower() == k.lower():
                return d[dk]
    raise KeyError(keys[0])


def load_modules(path):
    """Return a list of modules: {'id', 'config_id', 'stats': {stat_id: value}}."""
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)

    pkg = _get(raw, "ModulesPackage")
    items = _get(pkg, "Items")
    mod = _get(raw, "Mod")
    infos = _get(mod, "ModInfos")

    modules = []
    for key, item in items.items():
        attr = _get(item, "ModNewAttr")
        parts = _get(attr, "ModParts")
        info = infos.get(key) or infos.get(str(key))
        if info is None:
            continue
        values = _get(info, "InitLinkNums")

        stats = {}
        for stat_id, value in zip(parts, values):
            stats[int(stat_id)] = int(value)

        modules.append({
            "id": key,
            "config_id": next((item[k] for k in item if k.lower() == "configid"), None),
            "stats": stats,
        })
    return modules


def load_names(path):
    if not path:
        return {}
    with open(path, encoding="utf-8") as f:
        return {int(k): v for k, v in json.load(f).items()}


# --------------------------------------------------------------- filtering ----

def limit_duplicates(modules):
    """Keep at most MAX_PER_TYPE modules with identical stat lines."""
    groups = defaultdict(list)
    for m in modules:
        sig = tuple(sorted(m["stats"].items()))
        groups[sig].append(m)
    kept = []
    for group in groups.values():
        kept.extend(group[:MAX_PER_TYPE])
    return kept


def relevant_modules(modules, minimums):
    """Drop modules that contribute nothing to the stats you chose."""
    wanted = set(minimums)
    return [m for m in modules if wanted & set(m["stats"])]


# ----------------------------------------------------------------- scoring ----

def loadout_totals(loadout):
    totals = defaultdict(int)
    for m in loadout:
        for sid, val in m["stats"].items():
            totals[sid] += val
    return totals


def meets(totals, minimums):
    return all(min(totals.get(sid, 0), STAT_CAP) >= need for sid, need in minimums.items())


def progress(totals, minimums):
    """Average percentage of each minimum reached, capped at 100 per stat."""
    if not minimums:
        return 100.0
    pct = [min(totals.get(sid, 0), need) / need * 100 for sid, need in minimums.items()]
    return sum(pct) / len(pct)


def score(totals, minimums):
    """Ranking score: stats you asked for count double, everything else counts once."""
    s = 0
    for sid, val in totals.items():
        weight = 2 if sid in minimums else 1
        s += min(val, STAT_CAP) * weight
    return s


# ------------------------------------------------------------------ search ----

def beam_search(modules, minimums):
    """Build loadouts one module at a time, keeping the most promising ones."""
    beam = [()]  # tuples of indices into `modules`
    for _ in range(NUM_MODULES):
        candidates = {}
        for combo in beam:
            used = set(combo)
            base_totals = loadout_totals([modules[i] for i in combo])
            for i in range(len(modules)):
                if i in used:
                    continue
                new = tuple(sorted(combo + (i,)))
                if new in candidates:
                    continue
                totals = dict(base_totals)
                for sid, val in modules[i]["stats"].items():
                    totals[sid] = totals.get(sid, 0) + val
                candidates[new] = (progress(totals, minimums), score(totals, minimums))
        ranked = sorted(candidates.items(), key=lambda kv: (kv[1][0], kv[1][1]), reverse=True)
        beam = [combo for combo, _ in ranked[:BEAM_WIDTH]]
        if not beam:
            break
    return beam


def find_loadouts(modules, minimums, top):
    combos = beam_search(modules, minimums)
    results = []
    seen = set()
    for combo in combos:
        loadout = [modules[i] for i in combo]
        totals = loadout_totals(loadout)
        if not meets(totals, minimums):
            continue
        sig = tuple(sorted(m["id"] for m in loadout))
        if sig in seen:
            continue
        seen.add(sig)
        results.append({
            "modules": [m["id"] for m in loadout],
            "totals": {int(k): v for k, v in sorted(totals.items())},
            "score": score(totals, minimums),
        })
    results.sort(key=lambda r: r["score"], reverse=True)
    return results[:top]


# --------------------------------------------------------------------- CLI ----

def parse_mins(items):
    minimums = {}
    for item in items:
        if "=" not in item:
            raise SystemExit(f"--min must look like STAT_ID=MINIMUM, got: {item}")
        sid, val = item.split("=", 1)
        minimums[int(sid)] = max(0, min(int(val), STAT_CAP))
    return minimums


def main():
    p = argparse.ArgumentParser(description="BPSR module optimiser")
    p.add_argument("modules_file", help="ModulesSaveData.json written by ZDPS")
    p.add_argument("--min", action="append", default=[], metavar="STAT=VALUE",
                   help="minimum total for a stat, e.g. --min 2104=16 (repeat for more)")
    p.add_argument("--top", type=int, default=10, help="how many loadouts to show")
    p.add_argument("--names", help="optional JSON file mapping stat IDs to names")
    p.add_argument("--out", default="results.json", help="where to save the results")
    args = p.parse_args()

    minimums = parse_mins(args.min)
    if not minimums:
        print("Set at least one --min, for example: --min 2104=16", file=sys.stderr)
        return 1

    names = load_names(args.names)
    modules = limit_duplicates(relevant_modules(load_modules(args.modules_file), minimums))
    print(f"Modules considered: {len(modules)}")

    results = find_loadouts(modules, minimums, args.top)
    if not results:
        print("No loadout reaches all of those minimums with your modules.")
        return 0

    def label(sid):
        return names.get(sid, str(sid))

    print("Minimums:", ", ".join(f"{label(s)} >= {v}" for s, v in minimums.items()))
    for n, r in enumerate(results, 1):
        print(f"\n#{n}  score {r['score']}")
        print("  modules:", ", ".join(r["modules"]))
        print("  totals: ", ", ".join(f"{label(s)} {v}" for s, v in r["totals"].items()))

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)
    print(f"\nSaved to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
