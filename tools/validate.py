"""《举火》内容校验器（D3 · S0-03）

用途
    把 Content 里的表与地图对一遍，五类错一次全报出来，有错就红。

运行
    python tools/validate.py              # 校验 Juhuo/Content
    python tools/validate.py --notes       # 额外列出"无法验证、已跳过"的逐条明细
    python tools/validate.py --selftest    # 在临时目录造 5 个错样本，验证五类都能报出

错误码
    E01 坐标越界（含坐标为空 / 非整数，此时无法定到格子）
    E02 落在不可通行格（通行字符以 Src/Game/Art/TerrainMap.cs 的 IsWalkable 为准）
    E03 同一 hub 同一格重叠（点与点、人与点、人与人）
    E04 文本为空（points.text_once / text_repeat；npcs.first）
    E05 引用缺失（knowledge 缺 requires；requires 与 set_flag 同旗标；NPC 重名/无名）
    E06 重复 id
    E07 hub 值不在 hub1..hub7
    E08 表头缺列

判定口径
    * 通行格 = 地面字符落在 '.', ',', ':', '+', '^'（与 TerrainMap.IsWalkable 一致）。
    * 地图里的标记字符（'@' '>' 'X' 及任何非地形字符）按 TerrainMap.Resolve 的
      "邻域投票"还原成地形后再判，避免把标记当成墙。
    * npcs.csv 里 (0,0) 是巡逻衙役的占位（World.cs:BuildPatrol 会覆盖坐标），跳过坐标校验。
    * npcs.again 允许为空：World.cs:529 明确回退到 first，"没有再谈台词就说初见"。
      这一条不是错，只在跳过项里计数。

退出码 0 无错 / 1 有错 / 2 跑不起来（文件缺失等）。
"""

from __future__ import annotations

import argparse
import csv
import os
import shutil
import sys
import tempfile
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_MAPS = os.path.join(ROOT, "Juhuo", "Content", "maps")
DEFAULT_DATA = os.path.join(ROOT, "Juhuo", "Content", "data")

HUBS = ["hub%d" % i for i in range(1, 8)]

# 与 TerrainMap.cs 保持一致
TERRAIN = set(".,:#T~^=+W")
WALKABLE = set(".,:+^")
NEIGHBORS = ((0, -1, 3), (0, 1, 3), (-1, 0, 3), (1, 0, 3),
             (-1, -1, 1), (1, -1, 1), (-1, 1, 1), (1, 1, 1))

POINT_COLS = ["hub", "id", "mark", "x", "y", "layer", "name", "requires",
              "text_once", "text_repeat", "loot", "set_flag", "watch_npc", "cost"]
NPC_COLS = ["hub", "name", "x", "y", "first", "again"]

PATROL_SENTINEL = (0, 0)

NOTE_REASONS = {
    "requires": "requires 旗标无任何点置位（可能由对话置位，无法验证）",
    "watch": "watch_npc 是字母代号，npcs.csv 无对应列（无法验证）",
    "again": "npcs again 为空（引擎回退 first，World.cs:529，不算错）",
    "sentinel": "npcs 坐标为 (0,0) 的巡逻占位（坐标由代码给定）",
    "ragged": "地图行宽不齐（按 TerrainMap 口径，短行按墙处理）",
}


class Err:
    __slots__ = ("code", "hub", "who", "coord", "msg")

    def __init__(self, code, hub, who, coord, msg):
        self.code = code
        self.hub = hub
        self.who = who
        self.coord = coord
        self.msg = msg

    def line(self):
        return "[%s] %s %s (%s) %s" % (self.code, self.hub, self.who, self.coord, self.msg)

    def sort_key(self):
        return (self.code, self.hub, self.who)


class Map:
    """地图的只读视图：保留原始字符，按 TerrainMap.Resolve 还原地形。"""

    def __init__(self, text):
        rows = [ln for ln in text.replace("\r", "").split("\n") if ln]
        self.rows = rows
        self.h = len(rows)
        self.w = len(rows[0]) if rows else 0
        self.ragged = any(len(r) != self.w for r in rows)

    def raw(self, x, y):
        if 1 <= y <= self.h and 1 <= x <= len(self.rows[y - 1]):
            return self.rows[y - 1][x - 1]
        return "#"

    def ground(self, x, y):
        c = self.raw(x, y)
        if c in TERRAIN:
            return c
        score = {}
        for dx, dy, w in NEIGHBORS:
            n = self.raw(x + dx, y + dy)
            if n in TERRAIN:
                score[n] = score.get(n, 0) + w
        if not score:
            return ","
        best = max(sorted(score), key=lambda k: score[k])   # 平票取字符码小的，同 C#
        return best

    def walkable(self, x, y):
        return self.ground(x, y) in WALKABLE


def read_table(path, required):
    with open(path, encoding="utf-8-sig", newline="") as f:
        rd = csv.DictReader(f)
        cols = rd.fieldnames or []
        missing = [c for c in required if c not in cols]
        rows = []
        for r in rd:
            rows.append({(k or ""): (v if v is not None else "") for k, v in r.items()})
    return missing, rows


def require_file(path):
    if not os.path.isfile(path):
        raise FileNotFoundError(path)


def parse_coord(raw):
    """返回 (int 或 None, 失败原因 或 None)。"""
    s = (raw or "").strip()
    if not s:
        return None, "空值"
    body = s[1:] if s[0] in "+-" else s
    if body.isdigit():
        return int(s), None
    return None, "非整数 %r" % s


def validate(maps_dir, data_dir):
    """返回 (errors, notes, checked, note_detail)。不打印，不做退出码。"""
    errors = []
    notes = Counter()
    note_detail = []

    maps = {}
    for hub in HUBS:
        path = os.path.join(maps_dir, hub + ".txt")
        require_file(path)
        with open(path, encoding="utf-8-sig") as f:
            m = Map(f.read())
        maps[hub] = m
        if m.ragged:
            notes["ragged"] += 1
            note_detail.append(("ragged", "%s 行宽不齐（宽 %d，共 %d 行）" % (hub, m.w, m.h)))

    p_path = os.path.join(data_dir, "points.csv")
    n_path = os.path.join(data_dir, "npcs.csv")
    require_file(p_path)
    require_file(n_path)
    p_missing, points = read_table(p_path, POINT_COLS)
    n_missing, npcs = read_table(n_path, NPC_COLS)
    if p_missing:
        errors.append(Err("E08", "points.csv", "-", "-", "表头缺列：%s" % ", ".join(p_missing)))
    if n_missing:
        errors.append(Err("E08", "npcs.csv", "-", "-", "表头缺列：%s" % ", ".join(n_missing)))

    checked = len(points) + len(npcs)

    # —— 旗标全集：任何点置位过的旗标算"可验证存在" ——
    flags = {(r.get("set_flag") or "").strip() for r in points}
    flags.discard("")

    cells = {}      # (hub, x, y) -> [(who, 说明)]
    ids = Counter((r.get("id") or "").strip() for r in points)
    id_where = {}
    for r in points:
        pid = (r.get("id") or "").strip()
        id_where.setdefault(pid, ((r.get("hub") or "?").strip(),
                                  "%s,%s" % ((r.get("x") or "?").strip(),
                                             (r.get("y") or "?").strip())))

    for r in points:
        hub = (r.get("hub") or "").strip()
        pid = (r.get("id") or "").strip()
        xs, ys = r.get("x"), r.get("y")
        coord = "%s,%s" % ((xs or "?").strip(), (ys or "?").strip())

        if hub not in HUBS:
            errors.append(Err("E07", hub or "?", pid or "?", coord,
                              "hub 值不在 %s..%s" % (HUBS[0], HUBS[-1])))
            continue

        # —— E01 越界 / 坐标非法 ——
        x, ex = parse_coord(xs)
        y, ey = parse_coord(ys)
        if ex or ey:
            errors.append(Err("E01", hub, pid, coord,
                              "坐标%s" % "/".join(v for v in (ex, ey) if v)))
            continue
        m = maps[hub]
        if not (1 <= x <= m.w and 1 <= y <= m.h):
            where = []
            if not 1 <= x <= m.w:
                where.append("x 应在 1..%d" % m.w)
            if not 1 <= y <= m.h:
                where.append("y 应在 1..%d" % m.h)
            errors.append(Err("E01", hub, pid, "%d,%d" % (x, y),
                              "坐标越界（地图 %d×%d，%s）" % (m.w, m.h, "，".join(where))))
            continue

        # —— E02 不可通行 ——
        if not m.walkable(x, y):
            errors.append(Err("E02", hub, pid, "%d,%d" % (x, y),
                              "落在不可通行格（地面 %r，原始 %r）" % (m.ground(x, y), m.raw(x, y))))

        cells.setdefault((hub, x, y), []).append(("point", pid or "?"))

        # —— E04 文本为空 ——
        for col in ("text_once", "text_repeat"):
            if not (r.get(col) or "").strip():
                errors.append(Err("E04", hub, pid, "%d,%d" % (x, y), "%s 为空" % col))

        # —— E05 引用 ——
        layer = (r.get("layer") or "").strip()
        requires = (r.get("requires") or "").strip()
        set_flag = (r.get("set_flag") or "").strip()
        if layer == "knowledge" and not requires:
            errors.append(Err("E05", hub, pid, "%d,%d" % (x, y),
                              "knowledge 缺 requires（未满足前置就永远不可交互）"))
        if requires and requires == set_flag:
            errors.append(Err("E05", hub, pid, "%d,%d" % (x, y),
                              "requires 与 set_flag 同为 %r，前置永远不会先成立" % requires))
        if requires and requires not in flags:
            notes["requires"] += 1
            note_detail.append(("requires", "%s %s requires=%s" % (hub, pid, requires)))
        if (r.get("watch_npc") or "").strip():
            notes["watch"] += 1
            note_detail.append(("watch", "%s %s watch_npc=%s"
                                % (hub, pid, (r.get("watch_npc") or "").strip())))

    for pid, n in sorted(ids.items()):
        if n > 1:
            hub, coord = id_where.get(pid, ("?", "-"))
            errors.append(Err("E06", hub, pid, coord, "id 重复（共 %d 条）" % n))

    npc_names = Counter()
    for r in npcs:
        hub = (r.get("hub") or "").strip()
        name = (r.get("name") or "").strip()
        xs, ys = r.get("x"), r.get("y")
        coord = "%s,%s" % ((xs or "?").strip(), (ys or "?").strip())
        who = "npc:" + (name or "?")

        if hub not in HUBS:
            errors.append(Err("E07", hub or "?", who, coord,
                              "hub 值不在 %s..%s" % (HUBS[0], HUBS[-1])))
            continue
        if not name:
            errors.append(Err("E05", hub, who, coord, "name 为空，按名查台词会落空"))
        else:
            npc_names[(hub, name)] += 1

        # —— E04 文本为空（again 允许空，见文件头）——
        if not (r.get("first") or "").strip():
            errors.append(Err("E04", hub, who, coord, "first 为空"))
        if not (r.get("again") or "").strip():
            notes["again"] += 1
            note_detail.append(("again", "%s %s" % (hub, name or "?")))

        # —— E01 / E02 / E03 ——
        x, ex = parse_coord(xs)
        y, ey = parse_coord(ys)
        if ex or ey:
            errors.append(Err("E01", hub, who, coord,
                              "坐标%s" % ("/".join(v for v in (ex, ey) if v))))
            continue
        if (x, y) == PATROL_SENTINEL:
            notes["sentinel"] += 1
            note_detail.append(("sentinel", "%s %s" % (hub, name or "?")))
            continue
        m = maps[hub]
        if not (1 <= x <= m.w and 1 <= y <= m.h):
            errors.append(Err("E01", hub, who, "%d,%d" % (x, y),
                              "坐标越界（地图 %d×%d）" % (m.w, m.h)))
            continue
        if not m.walkable(x, y):
            errors.append(Err("E02", hub, who, "%d,%d" % (x, y),
                              "站在不可通行格（地面 %r）" % m.ground(x, y)))
        cells.setdefault((hub, x, y), []).append(("npc", name or "?"))

    for (hub, name), n in npc_names.items():
        if n > 1:
            errors.append(Err("E05", hub, "npc:" + name, "-",
                              "同一 hub 内重名 %d 次，按名查台词会取错人" % n))

    # —— E03 同格重叠 ——
    for (hub, x, y), who in sorted(cells.items()):
        if len(who) > 1:
            labels = [(v if kind == "point" else "npc:" + v) for kind, v in who]
            errors.append(Err("E03", hub, ",".join(labels), "%d,%d" % (x, y),
                              "同一格重叠：%s" % " / ".join(labels)))

    errors.sort(key=lambda e: e.sort_key())
    return errors, notes, checked, note_detail


def report(errors, notes, checked, note_detail, show_notes):
    print("《举火》内容校验 · points + npcs + maps(hub1..hub7)")
    for e in errors:
        print(e.line())

    if notes:
        total = sum(notes.values())
        print("跳过（无法验证/不算错，不计入错误）：%d 条" % total)
        for key, n in notes.most_common():
            print("  - %s：%d 条" % (NOTE_REASONS.get(key, key), n))
        if show_notes:
            for key, text in note_detail:
                print("    · [%s] %s" % (key, text))

    print("共检查 %d 条，错误 %d 条" % (checked, len(errors)))
    return 1 if errors else 0


# ————————————————————————— selftest —————————————————————————

def _write_csv(path, cols, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        wr = csv.writer(f, quoting=csv.QUOTE_ALL)
        wr.writerow(cols)
        for r in rows:
            wr.writerow([r.get(c, "") for c in cols])


def selftest():
    """在临时目录复制真实数据并注入 5 个错，五类必须全部报出。Content 只读不写。"""
    tmp = tempfile.mkdtemp(prefix="juhuo_selftest_")
    try:
        maps_dir = os.path.join(tmp, "maps")
        data_dir = os.path.join(tmp, "data")
        os.makedirs(maps_dir)
        os.makedirs(data_dir)

        for hub in HUBS:
            shutil.copyfile(os.path.join(DEFAULT_MAPS, hub + ".txt"),
                            os.path.join(maps_dir, hub + ".txt"))

        _, points = read_table(os.path.join(DEFAULT_DATA, "points.csv"), POINT_COLS)
        _, npcs = read_table(os.path.join(DEFAULT_DATA, "npcs.csv"), NPC_COLS)
        by_id = {r["id"]: r for r in points}

        # E01 越界：把 h1_01 扔到地图外
        by_id["h1_01"]["x"] = "999"
        # E02 不可通行：h1_02 移到 (1,1)，所有地图外圈都是 '#'
        by_id["h1_02"]["x"], by_id["h1_02"]["y"] = "1", "1"
        # E03 同格重叠：h1_03 挪到 h1_04 头上
        by_id["h1_03"]["x"] = by_id["h1_04"]["x"]
        by_id["h1_03"]["y"] = by_id["h1_04"]["y"]
        # E04 文本为空：抹掉 h1_05 的再谈文本
        by_id["h1_05"]["text_repeat"] = ""
        # E05 引用缺失：知识门不许没有前置
        by_id["bf_0k"]["requires"] = ""

        _write_csv(os.path.join(data_dir, "points.csv"), POINT_COLS, points)
        _write_csv(os.path.join(data_dir, "npcs.csv"), NPC_COLS, npcs)

        errors, _notes, checked, _detail = validate(maps_dir, data_dir)
        got = {e.code for e in errors}
        need = ["E01", "E02", "E03", "E04", "E05"]
        hit = [c for c in need if c in got]

        for e in errors:
            print("  " + e.line())
        missing = [c for c in need if c not in got]
        if missing:
            print("[selftest] 未报出：%s（共检查 %d 条）" % (", ".join(missing), checked))
            return 1
        print("[selftest] %d/5" % len(hit))
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main(argv=None):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    ap = argparse.ArgumentParser(description="《举火》内容校验器")
    ap.add_argument("--selftest", action="store_true", help="造 5 个错样本，验证五类都能报出")
    ap.add_argument("--notes", action="store_true", help="列出被跳过的引用明细")
    ap.add_argument("--maps", default=DEFAULT_MAPS, help="地图目录")
    ap.add_argument("--data", default=DEFAULT_DATA, help="数据目录")
    args = ap.parse_args(argv)

    if args.selftest:
        return selftest()

    try:
        errors, notes, checked, detail = validate(args.maps, args.data)
    except FileNotFoundError as exc:
        print("找不到文件：%s" % exc)
        return 2
    return report(errors, notes, checked, detail, args.notes)


if __name__ == "__main__":
    sys.exit(main())
