"""《举火》内容校验器（D3 · S0-03）

用途
    把 Content 里的表与地图对一遍，错一次全报出来，有错就红。
    另外三件事：两份 points CSV 是否同源（E09）、十二节拍表是否符合 schema（E10）、
    物品表/赠礼表是否覆盖并引用得住（E11）。

运行
    python tools/validate.py              # 校验 Juhuo/Content
    python tools/validate.py --notes       # 额外列出"无法验证、已跳过"的逐条明细
    python tools/validate.py --selftest    # 在临时目录造错样本，验证各类都能报出

错误码
    E01 坐标越界（含坐标为空 / 非整数，此时无法定到格子）
    E02 落在不可通行格（通行字符以 Src/Game/Art/TerrainMap.cs 的 IsWalkable 为准）
    E03 同一 hub 同一格重叠（点与点、人与点、人与人）
    E04 文本为空（points.text_once / text_repeat；npcs.first）
    E05 引用缺失（knowledge 缺 requires；requires 与 set_flag 同旗标；NPC 重名/无名）
    E06 重复 id
    E07 hub 值不在 hub1..hub7
    E08 表头缺列
    E09 两份 points CSV 不一致（生成器产出的文档 CSV vs 游戏读的 Content/data/points.csv）
    E10 beats.csv 结构错（行数 / id / day / hub / requires / sets 旗标名 / trigger）
    E11 items.csv / gifts.csv 表结构、loot 覆盖、引用与旗标名

判定口径
    * 通行格 = 地面字符落在 '.', ',', ':', '+', '^'（与 TerrainMap.IsWalkable 一致）。
    * 地图里的标记字符（'@' '>' 'X' 及任何非地形字符）按 TerrainMap.Resolve 的
      "邻域投票"还原成地形后再判，避免把标记当成墙。
    * npcs.csv 里 (0,0) 是巡逻衙役的占位（World.cs:BuildPatrol 会覆盖坐标），跳过坐标校验。
    * npcs.again 允许为空：World.cs:529 明确回退到 first，"没有再谈台词就说初见"。
      这一条不是错，只在跳过项里计数。
    * E09 比的是 id/hub/x/y/name 五列（任务书口径）。id 集合或顺序不同也报，因为
      顺序不同多半说明有一份是旧的。
    * E10 的 requires 按 FlagExpression 的文法判，方言只有一种（S1-06b 裁定）：
      裸旗标名 + '&' '|' '!' + 括号。`has(x)` 是非法写法——name 在 '(' 处截断，
      整条解析失败、那一拍永远 Locked，所以在校验里直接红。
    * E10 的 sets 按 FlagStore.IsValidName 判：点号式 3–4 段、下划线式 2–4 段全小写
      ASCII、或 12 个冻结旧名，≤40 字节。名字不合法时 FlagStore.Set 是静默忽略的，
      节拍会永远完不成还没人报错，所以必须在表里就拦住。
    * E11 的 loot 覆盖是硬错：points.loot 里出现的 id 在 items.csv 查不到，游戏就只
      显示裸 id，没有名字、没有能不能叠。反过来 items.csv 里没被任何 loot 引用到的行
      不报错（以后还会有别的来源），只进跳过项。
    * E11 的赠礼表按"给他看"读：npc 必须是 npcs.csv 里有的名字（该表没有 id 列，
      名字是唯一的键）、item 必须在 items.csv 里、requires 按同一套旗标文法判、
      sets 为空允许（有些反应不留痕）。

退出码 0 无错 / 1 有错 / 2 跑不起来（文件缺失等）。
"""

from __future__ import annotations

import argparse
import csv
import os
import re
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
BEATS_COLS = ["id", "name", "hub", "day", "requires", "sets", "trigger", "options", "note"]
ITEM_COLS = ["id", "name", "kind", "stack", "note"]
GIFT_COLS = ["npc", "item", "requires", "sets", "note"]
ITEM_KINDS = ("food", "tool", "token", "paper")     # 食物 / 器物 / 凭证 / 纸
STACK_VALUES = ("0", "1")                          # 0 不可叠 / 1 可叠

# 文档/校对用的那一份（生成器写；与游戏读的那份同源）
DOC_CSV = os.path.join(ROOT, "举火_序章数据_全关卡_v1.1.csv")
MIRROR_COLS = ["hub", "x", "y", "name"]
BEATS_ROWS = 12
TRIGGERS = {"choice", "knock", "search", "talk"}

PATROL_SENTINEL = (0, 0)

NOTE_REASONS = {
    "requires": "requires 旗标无任何点置位（可能由对话置位，无法验证）",
    "watch": "watch_npc 是字母代号，npcs.csv 无对应列（无法验证）",
    "again": "npcs again 为空（引擎回退 first，World.cs:529，不算错）",
    "sentinel": "npcs 坐标为 (0,0) 的巡逻占位（坐标由代码给定）",
    "ragged": "地图行宽不齐（按 TerrainMap 口径，短行按墙处理）",
    "orphan_item": "items.csv 里有、但没有任何 loot 引用（以后会有别的来源，不算错）",
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


# ————————————————————————— 旗标表达式（照抄 FlagExpression.cs 的文法） —————————————————————————
# expr := or ；or := and ('|' and)* ；and := unary ('&' unary)* ；
# unary := '!' unary | primary ；primary := '(' expr ')' | name
# name  := 除空白与 ! & | ( ) 外的连续字符（含 '.' 与汉字）
# 空串 = 无条件成立。语法错误在 C# 里返回 false（门关死），这里只判"能不能解析"。
# 方言只有这一种（S1-06b 裁定）：没有 has(...) 这种壳。写了 has(b01.done)，
# name 在 '(' 处截断只剩 "has"，剩下的 "(b01.done)" 没消费完 → 整条解析失败 → 永远不触发。

MAX_DEPTH = 32
HAS_LIKE = re.compile(r"(?<![^\s!&|()])has\s*\(")

# 旗标名的命名口径，照抄 FlagStore.IsValidName：
#   * 点号式 3–4 段，每段 \p{L}\p{N}_（Python 的 \w 覆盖字母/数字/下划线）
#   * 下划线式 2–4 段全小写 ASCII（首段以字母开头）
#   * 另加 12 个冻结旧名
#   * 总长 ≤40 字节（UTF-8，汉字算 3 字节）
MAX_NAME_BYTES = 40
FROZEN_FLAGS = {
    "has_shoe", "knows_shoe_inward", "knows_lu_wenzhao", "knows_11_banner",
    "intel_seal_order", "has_letter", "has_deed", "has_medicine", "has_ash",
    "took_temple_coin", "knows_herb_path", "path_backhill_open",
}
NEW_STYLE_NAME = re.compile(r"^[a-z][a-z0-9]*(?:_[a-z0-9]+){1,3}$")
DOT_STYLE_NAME = re.compile(r"^\w+(?:\.\w+){2,3}$")


class _Expr:
    def __init__(self, text):
        self.s = text
        self.i = 0

    def skip(self):
        while self.i < len(self.s) and self.s[self.i].isspace():
            self.i += 1

    def at_end(self):
        return self.i >= len(self.s)

    def peek_is(self, ch):
        return not self.at_end() and self.s[self.i] == ch

    def parse_or(self, depth):
        if depth > MAX_DEPTH or not self.parse_and(depth):
            return False
        while True:
            self.skip()
            if not self.peek_is("|"):
                return True
            self.i += 1
            if not self.parse_and(depth):
                return False

    def parse_and(self, depth):
        if depth > MAX_DEPTH or not self.parse_unary(depth):
            return False
        while True:
            self.skip()
            if not self.peek_is("&"):
                return True
            self.i += 1
            if not self.parse_unary(depth):
                return False

    def parse_unary(self, depth):
        if depth > MAX_DEPTH:
            return False
        self.skip()
        if self.at_end():
            return False
        if self.s[self.i] == "!":
            self.i += 1
            return self.parse_unary(depth + 1)
        return self.parse_primary(depth)

    def parse_primary(self, depth):
        if depth > MAX_DEPTH:
            return False
        self.skip()
        if self.at_end():
            return False
        if self.s[self.i] == "(":
            self.i += 1
            if not self.parse_or(depth + 1):
                return False
            self.skip()
            if not self.peek_is(")"):
                return False
            self.i += 1
            return True
        start = self.i
        while self.i < len(self.s) and _is_name_char(self.s[self.i]):
            self.i += 1
        return self.i > start


def _is_name_char(c):
    return not c.isspace() and c not in "!&|()"


def flag_expr_ok(expr):
    """能不能被 FlagExpression 解析。空串算能（无条件成立）。"""
    s = (expr or "").strip()
    if not s:
        return True
    p = _Expr(s)
    if not p.parse_or(0):
        return False
    p.skip()
    return p.at_end()


def flag_name_ok(name):
    """旗标名是否过 FlagStore.IsValidName（sets 列用）。"""
    if not name:
        return False
    if len(name.encode("utf-8")) > MAX_NAME_BYTES:
        return False
    if name in FROZEN_FLAGS:
        return True
    return bool(NEW_STYLE_NAME.match(name) or DOT_STYLE_NAME.match(name))


# ————————————————————————— E09 两份 CSV 同源 —————————————————————————

def check_mirror(doc_path, points):
    """比对文档 CSV 与游戏 CSV 的 id/hub/x/y/name。points 是游戏那份的行。"""
    errors = []
    if not os.path.isfile(doc_path):
        return [Err("E09", "-", os.path.basename(doc_path), "-",
                    "文档 CSV 不存在，先跑 python tools/gen_prologue_data.py")]

    _missing, doc_rows = read_table(doc_path, POINT_COLS)
    doc_order = [(r.get("id") or "").strip() for r in doc_rows]
    game_order = [(r.get("id") or "").strip() for r in points]

    if len(doc_rows) != len(points):
        errors.append(Err("E09", "-", "points.csv", "-",
                          "条数不一致：文档 %d 条，游戏 %d 条" % (len(doc_rows), len(points))))

    doc_by_id = {i: r for i, r in zip(doc_order, doc_rows) if i}
    game_by_id = {i: r for i, r in zip(game_order, points) if i}
    for pid in sorted(set(doc_by_id) - set(game_by_id)):
        errors.append(Err("E09", "-", pid, "-", "只有文档 CSV 有，游戏 CSV 缺这条"))
    for pid in sorted(set(game_by_id) - set(doc_by_id)):
        errors.append(Err("E09", "-", pid, "-", "只有游戏 CSV 有，文档 CSV 缺这条"))

    for pid in sorted(set(doc_by_id) & set(game_by_id)):
        d, g = doc_by_id[pid], game_by_id[pid]
        diffs = []
        for col in MIRROR_COLS:
            dv = (d.get(col) or "").strip()
            gv = (g.get(col) or "").strip()
            if dv != gv:
                diffs.append("%s 文档=%r 游戏=%r" % (col, dv, gv))
        if diffs:
            errors.append(Err("E09", (g.get("hub") or "?").strip(), pid,
                              "%s,%s" % ((g.get("x") or "?").strip(),
                                         (g.get("y") or "?").strip()),
                              "两份 CSV 不一致：" + "；".join(diffs)))

    if not errors and doc_order != game_order:
        first = next(i for i, (a, b) in enumerate(zip(doc_order, game_order)) if a != b)
        errors.append(Err("E09", "-", game_order[first] or "?", "-",
                          "顺序不一致（第 %d 行起：文档 %r，游戏 %r）"
                          % (first + 1, doc_order[first], game_order[first])))
    return errors


# ————————————————————————— E10 十二节拍表 —————————————————————————

def check_beats(path):
    """E10：行数、id、day、hub、requires、sets、trigger。"""
    errors = []
    if not os.path.isfile(path):
        return [Err("E10", "-", "beats.csv", "-", "找不到 beats.csv")]

    missing, rows = read_table(path, BEATS_COLS)
    if missing:
        errors.append(Err("E10", "-", "beats.csv", "-", "表头缺列：%s" % ", ".join(missing)))
        return errors

    if len(rows) != BEATS_ROWS:
        errors.append(Err("E10", "-", "beats.csv", "-",
                          "行数应为 %d，实际 %d" % (BEATS_ROWS, len(rows))))

    seen = Counter()
    prev_day = None
    for n, r in enumerate(rows, start=2):        # 行号按文件行算（表头是第 1 行）
        bid = (r.get("id") or "").strip()
        hub = (r.get("hub") or "").strip()
        where = "第 %d 行" % n
        seen[bid] += 1

        if not re.fullmatch(r"b\d{2}", bid):
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "id 应形如 b01（两位数字），实际 %r" % bid))

        if hub not in HUBS:
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "hub 不在 %s..%s" % (HUBS[0], HUBS[-1])))

        day, dwhy = parse_coord(r.get("day"))
        if dwhy:
            errors.append(Err("E10", hub or "?", bid or where, "-", "day %s" % dwhy))
        elif not 1 <= day <= 30:
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "day=%d 应在 1..30" % day))
        else:
            # 允许同一天两拍（b01/b02 都在第 1 天），但绝不允许多日倒流。
            if prev_day is not None and day < prev_day:
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "day 不许回退（上一行 %d，本行 %d）" % (prev_day, day)))
            prev_day = day

        req = (r.get("requires") or "").strip()
        if not flag_expr_ok(req):
            hint = "；has(...) 不是合法写法，直接写裸旗标名" if HAS_LIKE.search(req) else ""
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "requires 无法解析：%r（只认裸旗标名 + & | ! + 括号%s）" % (req, hint)))

        raw_sets = (r.get("sets") or "").strip()
        if not raw_sets:
            errors.append(Err("E10", hub or "?", bid or where, "-", "sets 为空（这一拍做完不留痕）"))
        for flag in [x.strip() for x in raw_sets.split(",") if x.strip()]:
            if not flag_name_ok(flag):
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "sets 里的旗标名 %r 不合法（点号式 3–4 段、"
                                  "下划线式 2–4 段全小写，或冻结旧名，≤40 字节）" % flag))

        trig = (r.get("trigger") or "").strip()
        raw_opts = (r.get("options") or "").strip()
        opts = [x.strip() for x in raw_opts.split("|") if x.strip()]
        if trig == "choice":
            # 0 项合法（文案还没写，表现层退化成"继续"）；1 项没意义，多半是把分隔符写错了。
            if len(opts) == 1:
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "options 只有 1 项 %r（多项请用 | 分隔；没有文案就留空）" % opts[0]))
        elif opts:
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "%s 型节拍不该有 options（实际 %r）" % (trig, raw_opts)))
        if trig not in TRIGGERS:
            errors.append(Err("E10", hub or "?", bid or where, "-",
                              "trigger=%r 不在 %s" % (trig, "/".join(sorted(TRIGGERS)))))

    for bid, cnt in sorted(seen.items()):
        if bid and cnt > 1:
            errors.append(Err("E10", "-", bid, "-", "id 重复（共 %d 条）" % cnt))
    return errors


# ————————————————————————— E11 物品表 / 赠礼表 —————————————————————————

def loot_refs(points):
    """points.loot -> [(物品 id, hub, 点位 id, 坐标)]，按出现顺序，空项跳过。"""
    refs = []
    for r in points:
        hub = (r.get("hub") or "?").strip()
        pid = (r.get("id") or "?").strip()
        coord = "%s,%s" % ((r.get("x") or "?").strip(), (r.get("y") or "?").strip())
        for part in (r.get("loot") or "").split("|"):
            part = part.strip()
            if part:
                refs.append((part.split(":")[0].strip(), hub, pid, coord))
    return refs


def check_items_gifts(items_path, gifts_path, points, npcs, notes, note_detail):
    """E11：items.csv 覆盖 loot 且字段合法；gifts.csv 的 npc/item/requires/sets 都站得住。

    返回值与别处一致，是 Err 列表；跳过项写进传进来的 notes / note_detail。
    """
    errors = []
    items = []
    items_ok = True

    if not os.path.isfile(items_path):
        errors.append(Err("E11", "-", "items.csv", "-",
                          "找不到 items.csv（loot 里的 id 就没处查名字了）"))
        items_ok = False
    else:
        missing, items = read_table(items_path, ITEM_COLS)
        if missing:
            errors.append(Err("E11", "-", "items.csv", "-", "表头缺列：%s" % ", ".join(missing)))
            return errors
        seen = Counter()
        for n, r in enumerate(items, start=2):
            iid = (r.get("id") or "").strip()
            name = (r.get("name") or "").strip()
            kind = (r.get("kind") or "").strip()
            stack = (r.get("stack") or "").strip()
            where = "第 %d 行" % n
            seen[iid] += 1
            if not iid:
                errors.append(Err("E11", "items.csv", where, "-", "id 为空"))
            if not name:
                errors.append(Err("E11", "items.csv", iid or where, "-",
                                  "name 为空（游戏里会显示裸 id）"))
            if kind not in ITEM_KINDS:
                errors.append(Err("E11", "items.csv", iid or where, "-",
                                  "kind=%r 不在 %s" % (kind, "/".join(ITEM_KINDS))))
            if stack not in STACK_VALUES:
                errors.append(Err("E11", "items.csv", iid or where, "-",
                                  "stack=%r 只能是 0（不可叠）或 1（可叠）" % stack))
        for iid, cnt in sorted(seen.items()):
            if iid and cnt > 1:
                errors.append(Err("E11", "items.csv", iid, "-", "id 重复（共 %d 条）" % cnt))

    item_ids = {(r.get("id") or "").strip() for r in items}
    item_ids.discard("")

    refs = loot_refs(points)
    for loot_id, hub, pid, coord in refs:
        if items_ok and loot_id not in item_ids:
            errors.append(Err("E11", hub, pid, coord,
                              "loot 引用了 items.csv 里没有的物品 %r" % loot_id))

    if items_ok:
        for iid in sorted(item_ids - {r[0] for r in refs}):
            notes["orphan_item"] += 1
            note_detail.append(("orphan_item", "items.csv %s 没有任何 loot 引用" % iid))

    if not os.path.isfile(gifts_path):
        errors.append(Err("E11", "-", "gifts.csv", "-", "找不到 gifts.csv"))
        return errors
    gmissing, gifts = read_table(gifts_path, GIFT_COLS)
    if gmissing:
        errors.append(Err("E11", "-", "gifts.csv", "-", "表头缺列：%s" % ", ".join(gmissing)))
        return errors

    npc_names = {(r.get("name") or "").strip() for r in npcs}
    npc_names.discard("")

    for n, r in enumerate(gifts, start=2):
        npc = (r.get("npc") or "").strip()
        iid = (r.get("item") or "").strip()
        req = (r.get("requires") or "").strip()
        where = "第 %d 行" % n
        who = "gift:%s→%s" % (npc or where, iid or "?")
        if npc not in npc_names:
            errors.append(Err("E11", "-", who, "-",
                              "npc %r 不在 npcs.csv（该表没有 id 列，按 name 查）" % npc))
        if items_ok and iid not in item_ids:
            errors.append(Err("E11", "-", who, "-", "item %r 不在 items.csv" % iid))
        if not flag_expr_ok(req):
            hint = "；has(...) 不是合法写法，直接写裸旗标名" if HAS_LIKE.search(req) else ""
            errors.append(Err("E11", "-", who, "-",
                              "requires 无法解析：%r（只认裸旗标名 + & | ! + 括号%s）" % (req, hint)))
        for flag in [x.strip() for x in (r.get("sets") or "").split(",") if x.strip()]:
            if not flag_name_ok(flag):
                errors.append(Err("E11", "-", who, "-",
                                  "sets 里的旗标名 %r 不合法（点号式 3–4 段、"
                                  "下划线式 2–4 段全小写，或冻结旧名，≤40 字节）" % flag))
    return errors


def validate(maps_dir, data_dir, doc_csv=None, beats_path=None):
    """返回 (errors, notes, checked, note_detail)。不打印，不做退出码。

    doc_csv / beats_path 传 None = 这次不查（selftest 用的是临时数据，比不了真实的文档 CSV）。
    items.csv / gifts.csv 始终查：它们是手维护表，与文档 CSV 无关。
    """
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

    # —— E09 两份 points CSV 同源 ——
    if doc_csv:
        errors.extend(check_mirror(doc_csv, points))

    # —— E10 十二节拍表 ——
    if beats_path:
        errors.extend(check_beats(beats_path))

    # —— E11 物品表 / 赠礼表 ——
    errors.extend(check_items_gifts(os.path.join(data_dir, "items.csv"),
                                    os.path.join(data_dir, "gifts.csv"),
                                    points, npcs, notes, note_detail))

    errors.sort(key=lambda e: e.sort_key())
    return errors, notes, checked, note_detail


def report(errors, notes, checked, note_detail, show_notes):
    print("《举火》内容校验 · points + npcs + maps(hub1..hub7) + beats.csv + items/gifts + 两份 points 同源")
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


def _selftest_extra(tmp):
    """E09 / E10 自测：临时目录里改坏一处，两类都必须报出。Content 只读。"""
    hit = 0

    # E09：文档侧 h1_01 的 x 挪一格，游戏侧不动 → 必须报"两份 CSV 不一致"
    _, points = read_table(os.path.join(DEFAULT_DATA, "points.csv"), POINT_COLS)
    doc = os.path.join(tmp, "doc.csv")
    broke = [dict(r) for r in points]
    for r in broke:
        if r["id"] == "h1_01":
            r["x"] = "16"
    _write_csv(doc, POINT_COLS, broke)
    if any(e.code == "E09" for e in check_mirror(doc, points)):
        hit += 1

    # E10：b03 trigger 不在白名单、b04 day 回退、b05 写了 has(...) 壳、
    #      b06 的 sets 是两段名（FlagStore 点号式要 3–4 段）——四类都要报
    _, rows = read_table(os.path.join(DEFAULT_DATA, "beats.csv"), BEATS_COLS)
    beats = os.path.join(tmp, "beats.csv")
    bad = [dict(r) for r in rows]
    bad[2]["trigger"] = "boom"
    bad[3]["day"] = "1"
    bad[4]["requires"] = "has(beat.b04.done)"
    bad[5]["sets"] = "b06.done"
    _write_csv(beats, BEATS_COLS, bad)
    if any(e.code == "E10" for e in check_beats(beats)):
        hit += 1

    return hit


def _selftest_e11(tmp):
    """E11 自测：loot 指向不存在的物品 + 赠礼表的三处坏样本，都必须报出。"""
    _, points = read_table(os.path.join(DEFAULT_DATA, "points.csv"), POINT_COLS)
    _, npcs = read_table(os.path.join(DEFAULT_DATA, "npcs.csv"), NPC_COLS)
    _, items = read_table(os.path.join(DEFAULT_DATA, "items.csv"), ITEM_COLS)
    _, gifts = read_table(os.path.join(DEFAULT_DATA, "gifts.csv"), GIFT_COLS)

    broke = [dict(r) for r in points]
    broke[0]["loot"] = ((broke[0].get("loot") or "").strip() + "|ghost_loot:1").lstrip("|")

    bad_items = [dict(r) for r in items]
    bad_items.append({"id": "ghost_item", "name": "", "kind": "weapon", "stack": "2", "note": ""})

    bad_gifts = [dict(r) for r in gifts]
    bad_gifts.append({"npc": "没有这个人", "item": "ghost_item",
                      "requires": "has(x)", "sets": "bad", "note": ""})

    d = os.path.join(tmp, "e11")
    os.makedirs(d)
    _write_csv(os.path.join(d, "items.csv"), ITEM_COLS, bad_items)
    _write_csv(os.path.join(d, "gifts.csv"), GIFT_COLS, bad_gifts)
    errs = check_items_gifts(os.path.join(d, "items.csv"), os.path.join(d, "gifts.csv"),
                             broke, npcs, Counter(), [])
    msgs = [e.msg for e in errs]

    loot_hit = any("ghost_loot" in m and "loot" in m for m in msgs)
    item_hit = any("kind=" in m for m in msgs) and any("stack=" in m for m in msgs)
    gift_hit = (any("不在 npcs.csv" in m for m in msgs)
                and any("无法解析" in m for m in msgs)
                and any("旗标名" in m for m in msgs))
    return loot_hit and item_hit and gift_hit


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
        # E11 的两张表照抄真表：这一轮只验 E01–E05，别让物品/赠礼的红点混进来
        for name, cols in (("items.csv", ITEM_COLS), ("gifts.csv", GIFT_COLS)):
            _, rows_ = read_table(os.path.join(DEFAULT_DATA, name), cols)
            _write_csv(os.path.join(data_dir, name), cols, rows_)

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
        extra = _selftest_extra(tmp)
        if extra < 2:
            print("[selftest] 一致性/节拍检查未报出（%d/2）" % extra)
            return 1
        print("[selftest] 一致性 E09 + 节拍 E10 %d/2" % extra)
        if not _selftest_e11(tmp):
            print("[selftest] 物品/赠礼 E11 未报出")
            return 1
        print("[selftest] 物品/赠礼 E11 1/1")
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
        errors, notes, checked, detail = validate(
            args.maps, args.data,
            doc_csv=None if args.data != DEFAULT_DATA else DOC_CSV,
            beats_path=os.path.join(args.data, "beats.csv"))
    except FileNotFoundError as exc:
        print("找不到文件：%s" % exc)
        return 2
    return report(errors, notes, checked, detail, args.notes)


if __name__ == "__main__":
    sys.exit(main())
