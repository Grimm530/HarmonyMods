#!/usr/bin/env python3
"""
Merge Economics sources into C:\\!DataPersistence\\harmony\\Economics.

Strategy (safe for live File-mode data):
  1) Shared Players JSON is authoritative for any steamId already present.
  2) SQLite then legacy Balances only ADD missing accounts (never raise existing balances).
  3) Newer LastSeen from SQLite may update metadata on existing accounts.
"""
import json
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

LEGACY = Path(r"c:\svr1\oxide\data\Economics.json")
SHARED = Path(r"C:\!DataPersistence\oxide\data\Economics\Economics.json")
RP_SRC = Path(r"C:\!DataPersistence\oxide\data\Economics\Economics_RPTracking.json")
DB_PATH = Path(r"C:\!DataPersistence\economics_balances.db")
DEST_DIR = Path(r"C:\!DataPersistence\harmony\Economics")


def fmt_ts(ts):
    try:
        ts = float(ts or 0)
        if ts <= 0:
            return "Never"
        return datetime.fromtimestamp(ts, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    except Exception:
        return "Never"


def main():
    DEST_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")

    players = {}
    stats = {
        "from_shared": 0,
        "from_sqlite": 0,
        "from_legacy": 0,
        "added_sqlite": 0,
        "added_legacy": 0,
        "lastseen_upd_sqlite": 0,
        "skipped_existing_sqlite": 0,
        "skipped_existing_legacy": 0,
    }

    with SHARED.open("r", encoding="utf-8") as f:
        shared_data = json.load(f)
    for pid, pd in (shared_data.get("Players") or {}).items():
        if not pid:
            continue
        players[pid] = {
            "Balance": float(pd.get("Balance") or 0),
            "LastSeen": float(pd.get("LastSeen") or 0),
            "LastSeenFormatted": pd.get("LastSeenFormatted") or fmt_ts(pd.get("LastSeen") or 0),
        }
        stats["from_shared"] += 1

    sqlite_rows = 0
    if DB_PATH.exists():
        uri = DB_PATH.resolve().as_uri() + "?mode=ro"
        con = sqlite3.connect(uri, uri=True, timeout=5)
        try:
            con.execute("PRAGMA busy_timeout=5000")
            cur = con.execute(
                "SELECT steam_id, balance, last_seen, last_seen_formatted FROM economics_balances"
            )
            for pid, bal, ls, lsf in cur.fetchall():
                if not pid:
                    continue
                sqlite_rows += 1
                bal = float(bal or 0)
                ls = float(ls or 0)
                lsf = lsf or fmt_ts(ls)
                stats["from_sqlite"] += 1
                if pid not in players:
                    players[pid] = {
                        "Balance": bal,
                        "LastSeen": ls,
                        "LastSeenFormatted": lsf,
                    }
                    stats["added_sqlite"] += 1
                else:
                    stats["skipped_existing_sqlite"] += 1
                    if ls > players[pid]["LastSeen"]:
                        players[pid]["LastSeen"] = ls
                        players[pid]["LastSeenFormatted"] = lsf or fmt_ts(ls)
                        stats["lastseen_upd_sqlite"] += 1
        finally:
            con.close()

    with LEGACY.open("r", encoding="utf-8") as f:
        legacy_data = json.load(f)
    ls_map = legacy_data.get("LastSeen") or {}
    for pid, bal in (legacy_data.get("Balances") or {}).items():
        if not pid:
            continue
        bal = float(bal or 0)
        ls = float(ls_map.get(pid) or 0) if isinstance(ls_map, dict) else 0.0
        stats["from_legacy"] += 1
        if pid not in players:
            players[pid] = {
                "Balance": bal,
                "LastSeen": ls,
                "LastSeenFormatted": fmt_ts(ls),
            }
            stats["added_legacy"] += 1
        else:
            stats["skipped_existing_legacy"] += 1

    dest_json = DEST_DIR / "Economics.json"
    if dest_json.exists():
        shutil.copy2(dest_json, DEST_DIR / f"Economics.json.premerge-{stamp}")
    with dest_json.open("w", encoding="utf-8") as f:
        json.dump({"Players": players}, f, indent=2)
        f.write("\n")

    dest_rp = DEST_DIR / "Economics_RPTracking.json"
    if RP_SRC.exists():
        if dest_rp.exists():
            shutil.copy2(dest_rp, DEST_DIR / f"Economics_RPTracking.json.premerge-{stamp}")
        shutil.copy2(RP_SRC, dest_rp)

    for name in (
        "economics_balances.db",
        "economics_balances.db-wal",
        "economics_balances.db-shm",
    ):
        src = Path(r"C:\!DataPersistence") / name
        if src.exists():
            shutil.copy2(src, DEST_DIR / name)

    manifest = {
        "stamp": stamp,
        "strategy": (
            "shared Players authoritative for existing steamIds; "
            "sqlite then legacy only add missing accounts; "
            "sqlite may refresh LastSeen metadata"
        ),
        "sources": {
            "legacy_balances": str(LEGACY),
            "shared_players": str(SHARED),
            "sqlite": str(DB_PATH),
            "rp_tracking": str(RP_SRC),
        },
        "stats": stats,
        "sqlite_rows": sqlite_rows,
        "merged_players": len(players),
        "dest": str(DEST_DIR),
    }
    with (DEST_DIR / f"merge-manifest-{stamp}.json").open("w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print(json.dumps(manifest, indent=2))
    print(f"Wrote {dest_json} ({dest_json.stat().st_size} bytes)")
    if dest_rp.exists():
        print(f"Wrote {dest_rp} ({dest_rp.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
