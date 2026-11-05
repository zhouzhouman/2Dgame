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
    E12 trust.csv / routes.csv 表结构、id、三轴取值（−10..10 整数）、路线轴与阈值、三轴全 0
    E13 shops.csv 表结构、shop 取值、item 引用、价与存量、同物黑市价 ≤ 官市价
    E14 siege.csv / recruit.csv 表结构、四法齐全、刻数/伤亡/民怨/天命区间、
        needs 只认四种写法、官望门槛与 routes.csv 对齐、打赢门槛 troops_needed 正整数、
        招兵三档与 trust 引用
    E15 oaths.csv / realms.csv 表结构、id、誓约四件套非空、source 指得住一拍真选项、
        国号四个且不许撞真实朝代
    E16 travel.csv / intro.csv / portraits.csv：42 个有向组合齐全且来回差 ≤2、
        开场 6–10 句且 speaker/portrait 指得住 npcs.csv、30 行立绘参数在范围内且同 hub 不撞脸

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
    * E12 的三轴是「这一次动作对 官望 / 民望 / 疑心 的增减」，不是绝对值：
      0–100 是轴本身的范围（设计文档 §4.3），表里存的是单次变化量。
      故 guan/min/yi 限 −10..10 的整数——超过 10 的单次变化在序章里不存在，
      写大了多半是把轴值当变化量填了。
    * E12 不许三轴全 0：那一行等于没写，事件挂上去什么也不会发生。
      这是"数据必须有后果"的底线，CANON §2 第 12 条。
    * E12 的 routes.axis 只认 guan / min：疑心不是攻城路线，写进来就是设计错。
      阈值 1..100 与 0–100 的轴同量纲。
    * E13 的 price 单位是文（1 贯 = 1000 文），整数且为正；stock 是这一趟能买的份数，1..20。
      序章买的是"一升/一尺/一包"这种小份，与文档的"贯/石、贯/斤"不是同一分母，
      折算系数必须写在 note 里，否则这条价格无从复核。
    * E13 要求同一件物品的黑市价 ≤ 官市价（设计文档 §5.2：官市 ×1.2、黑市 ×0.75）。
      反过来写就是数据错，不是设计选择。
    * E13 还要求 official / black 各至少一行，且至少一件物品两边都卖——两边都卖的那一件
      是玩家能直接看见"官市贵、黑市贱"的唯一证据，没有它这一课就教不出来。
    * E14 的 siege.csv 是 §8.6 攻城四法那张表：民怨 +20/+8/0/+30、天命 −5/0/+3/−15 是
      原文直给；ke（长/短/中）与 casualty（低/高/极低/中）文档只有相对量，表里落成整数，
      故区间只卡合理性（刻 1..8、伤亡 0..100、民怨 0..40、天命 −20..10），不卡具体值。
    * E14 的 needs 方言只有四种：`trust:guan>=N` / `troops>=N` / `flag:name` / 空。
      写 "官望>60" 这种中文比较式一律红——引擎里没有这个解析器，写了就是永远不可选。
    * E14 的官望门槛必须与 routes.csv 的 inside 阈值是同一个数：两处打架时玩家会看到
      "官望 60 可选内应"和"内应要 70"同时成立，只有一处是真话。
    * E14 把两件事分开卡：`needs` 是"能不能选"，`troops_needed` 是"打不打得赢"。
      合在一起写就会重演 60 vs 72 的两处打架——攻那一行的 needs 因此故意留空，
      兵不够也能硬选，结果按 §16.5 撤退，这正是"攻"的代价。
      troops_needed 卡 1..200：引擎侧（SiegeMethod.MaxTroopsNeed=9999）只防打错零，
      这里是内容门，序章兵力就在几十的量级，四位数的门槛一定是写错了。
    * E14 的 recruit.option 对应 beats.csv b07 的 options 顺序（给粮|给承诺|抢兵器），
      三档各一行；trust 允许留空（承诺/抢夺的代价不落三轴），非空则必须在 trust.csv 里。
    * E15 的 oaths.source 是「这张卡由哪一拍的哪个选项产生」，写法 `b07:1`（拍 id : 选项
      序号，0 起）。序号必须落在该拍 options 的真实长度内：指向不存在的选项是硬错——那一拍的
      选项玩家点不到，这张卡在整局里永远不会出现，而表里看着像有。options 为空的拍同理。
    * E15 的誓约四件套是 §14.2 直给的结构：内容 / 受益者 / 期限 / 违约惩罚，缺一件这张卡
      就立不住（兑现时不知道该赔给谁、赔多少）。故四列都非空，text 一并算进去。
    * E15 不许用真实朝代名当国号（汉唐宋明清元），也不许用旧朝国号「晟」（世界观 §2.1：
      国号大晟，都中州）。架空王朝顶着别人的国号，史官评语就没法写了。
    * E16 的 travel.csv 是七个空间两两之间的刻数。42 组（7×6）必须齐：缺一组意味着玩家
      走到那个出口就卡死；ke 限 1..6，因为一个 hub 的探索预算只有 8–20 刻，一趟路要走掉
      一半预算就没人愿意出城了。来回允许不等（山道绕远、顺水下坡），但差超过 2 刻是硬错
      ——同一段路两个方向差一倍，只可能是表填错了，不是地形。
      每个 hub 至少一条进、一条出：孤岛比缺一组更难查。
    * E16 的 intro.csv 是开场 6–10 句。speaker 为空 = 旁白，旁白不许挂立绘；非空必须是
      npcs.csv 里的名字（否则这张脸没处取）。seq 必须从 1 连续——顺序是玩家读到的东西，
      跳号说明中间删过一句还没补。
    * E16 的 portraits.csv 必须覆盖 npcs.csv 的 30 处放置：沈砚与老鸦各出现两次，是同一
      个人，两行的七个参数因此必须一致（不一致就是两个人长了同一张脸，或一个人换了两张）。
      参数范围是美术规范里的枚举值，越界在引擎侧只会被夹到边界，看不出是内容写错了。
      同一 hub 里不许两个人七个参数全同：立绘是玩家唯一的辨识手段，撞脸等于没画。

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

# E12：两条信任轴 + 疑心，按地区各一条（序章只有县城一带，先按一条算）
TRUST_COLS = ["id", "label", "guan", "min", "yi", "note"]
ROUTE_COLS = ["id", "label", "axis", "threshold", "note"]
TRUST_AXES = ("guan", "min", "yi")
TRUST_DELTA_MIN, TRUST_DELTA_MAX = -10, 10        # 单次变化量的上下限
ROUTE_AXES = ("guan", "min")                      # 内应开门 / 民变开城，疑心不是路线
TRUST_ID = re.compile(r"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$")

# E13：官市 / 黑市货架（手维护表）
SHOP_COLS = ["shop", "item", "price", "stock", "note"]
SHOP_NAMES = ("official", "black")                 # 官市 / 黑市
STOCK_MIN, STOCK_MAX = 1, 20                      # 这一趟能买的份数
MARKET_TRUST_IDS = ("market_official", "market_black")   # §5.2 两条交易后果必须有行

# E14：攻城四法（§8.6）+ 招兵三选项（§18.5 场景7 / 关卡 §3.5）
SIEGE_COLS = ["id", "label", "ke", "casualty", "minyuan", "tianming", "needs", "troops_needed", "note"]
SIEGE_IDS = ("surround", "assault", "inside", "burn")   # 围 / 攻 / 间（内应）/ 水·火
SIEGE_ROWS = len(SIEGE_IDS)
KE_MIN, KE_MAX = 1, 8                      # 刻数：短→3、中→4–5、长→6
CASUALTY_MIN, CASUALTY_MAX = 0, 100        # 伤亡百分比
MINYUAN_MIN, MINYUAN_MAX = 0, 40           # 民怨增量
TIANMING_MIN, TIANMING_MAX = -20, 10       # 天命增量
TROOPS_NEEDED_MIN, TROOPS_NEEDED_MAX = 1, 200   # 打赢门槛；内容门比引擎的 9999 紧
# needs 只认这四种写法（空串 = 无条件）；中文比较式没有解析器，直接红
NEED_TRUST = re.compile(r"^trust:(?:guan|min)>=(\d{1,3})$")
NEED_TROOPS = re.compile(r"^troops>=(\d{1,3})$")
NEED_FLAG = re.compile(r"^flag:[a-z][a-z0-9]*(?:_[a-z0-9]+)*$")
INSIDE_ROUTE_ID = "inside"                 # 官望门槛要对齐的那一行

RECRUIT_COLS = ["option", "troops", "trust", "note"]
RECRUIT_OPTIONS = ("0", "1", "2")          # b07：给粮 / 给承诺 / 抢兵器
RECRUIT_TROOPS_MIN, RECRUIT_TROOPS_MAX = 10, 200

# E15：誓约卡（§14.2 内容/受益者/期限/违约惩罚）+ 转段国号（§16.6 第 2 步）
OATH_COLS = ["id", "text", "beneficiary", "deadline", "penalty", "source", "note"]
REALM_COLS = ["id", "label", "motto", "note"]
REALM_ROWS = 4                             # 转段时给玩家挑的国号备选数
OATH_SOURCE = re.compile(r"^(b\d{2}):(\d{1,2})$")     # 拍 id : 选项序号（0 起）
# 真实朝代名不许做国号；「晟」是旧朝国号（世界观 §2.1），同样不许
REALM_FORBIDDEN_LABELS = {"汉", "唐", "宋", "明", "清", "元", "晟"}

# E16：地图位移 / 开场介绍 / 立绘参数（三张手维护表）
TRAVEL_COLS = ["from", "to", "ke", "note"]
INTRO_COLS = ["seq", "speaker", "portrait", "text", "note"]
PORTRAIT_COLS = ["npc", "skin", "hair", "style", "beard", "hat", "robe", "mark", "note"]
TRAVEL_KE_MIN, TRAVEL_KE_MAX = 1, 6        # 一趟路最多 6 刻（hub 预算只有 8–20 刻）
TRAVEL_PAIR_SLACK = 2                      # 来回差：山路/顺水可以有差，差一倍就是表错了
INTRO_ROWS_MIN, INTRO_ROWS_MAX = 6, 10     # 开场 6–10 句
PORTRAIT_ROWS = 30                         # npcs.csv 是 30 处放置（28 个唯一名）
PORTRAIT_PARAMS = ("skin", "hair", "style", "beard", "hat", "robe", "mark")
PORTRAIT_RANGES = {"skin": (1, 4), "hair": (1, 6), "style": (1, 4), "beard": (0, 3),
                   "hat": (0, 4), "robe": (1, 6), "mark": (0, 4)}


def need_kind(needs):
    """返回 (合法与否, 写法的类别, 门槛数或 None)。空串算合法、类别为空。"""
    s = (needs or "").strip()
    if not s:
        return True, "", None
    for kind, rx in (("trust", NEED_TRUST), ("troops", NEED_TROOPS)):
        m = rx.match(s)
        if m:
            return True, kind, int(m.group(1))
    if NEED_FLAG.match(s):
        return True, "flag", None
    return False, "", None

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

        # E10（选项效果）：effects 是可缺省列；一旦填了，组数必须等于选项数。
        # 这条护栏防的是"改了选项忘了改效果"——错位的效果会安到错误的选项上，
        # 玩家点"杀马"却扣了"卖刀"的钱，而且很难在测试里看出来。
        opts_raw = (r.get("options") or "").strip()
        opts_n = len([o for o in opts_raw.split("|") if o.strip()]) if opts_raw else 0
        eff_raw = (r.get("effects") or "").strip() if "effects" in r else ""
        if eff_raw:
            eff_n = len(eff_raw.split("|"))
            if opts_n == 0:
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "填了 effects 却没有 options"))
            elif eff_n != opts_n:
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "options %d 条、effects %d 组，对不上" % (opts_n, eff_n)))
            for tok in [t.strip() for grp in eff_raw.split("|") for t in grp.split(";") if t.strip()]:
                if not re.fullmatch(r"(coin|item|guan|min|yi|troops|flag):[A-Za-z_]+(=[+-]?\d+|\s*[+-]\d+|\s*[A-Za-z_]+)|(coin|guan|min|yi|troops):[+-]\d+", tok):
                    errors.append(Err("E10", hub or "?", bid or where, "-",
                                      "effects 里有不认识的写法：%r" % tok))

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
            # 旧规则是"只有 choice 型才配选项"，太紧：b02 敲门的人 就是一次抉择
            # （老仆替你死，还是你把他推出去），b08 空仓 也是。触发方式决定"什么时候来"，
            # 选项决定"要不要做决定"，两件事不该互斥。这里只拦 1 项（多半是分隔符写错）。
            if len(opts) == 1:
                errors.append(Err("E10", hub or "?", bid or where, "-",
                                  "options 只有 1 项 %r（多项请用 | 分隔）" % opts[0]))
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


def check_trust_routes(trust_path, routes_path):
    """E12：三轴增减表与攻城路线表。两张都是手维护表，与文档 CSV 无关。"""
    errors = []

    trust_rows = []
    if not os.path.isfile(trust_path):
        errors.append(Err("E12", "-", "trust.csv", "-",
                          "找不到 trust.csv（事件的三轴增减就没处查了）"))
    else:
        missing, trust_rows = read_table(trust_path, TRUST_COLS)
        if missing:
            errors.append(Err("E12", "-", "trust.csv", "-", "表头缺列：%s" % ", ".join(missing)))
            trust_rows = []
        elif not trust_rows:
            errors.append(Err("E12", "-", "trust.csv", "-", "一行都没有（序章的信任轴是空的）"))

    seen = Counter()
    for n, r in enumerate(trust_rows, start=2):      # 行号按文件行算（表头是第 1 行）
        tid = (r.get("id") or "").strip()
        where = "第 %d 行" % n
        seen[tid] += 1

        if not TRUST_ID.match(tid):
            errors.append(Err("E12", "trust.csv", tid or where, "-",
                              "id 应是小写英文+下划线（如 bribe / sell_grain_cheap），"
                              "实际 %r" % tid))

        deltas = {}
        for axis in TRUST_AXES:
            val, why = parse_coord(r.get(axis))
            deltas[axis] = val
            if why:
                errors.append(Err("E12", "trust.csv", tid or where, "-",
                                  "%s %s（三轴只收整数）" % (axis, why)))
            elif not TRUST_DELTA_MIN <= val <= TRUST_DELTA_MAX:
                errors.append(Err("E12", "trust.csv", tid or where, "-",
                                  "%s=%d 应在 %d..%d（这是单次变化量，不是轴值）"
                                  % (axis, val, TRUST_DELTA_MIN, TRUST_DELTA_MAX)))

        if None not in deltas.values() and not any(deltas.values()):
            errors.append(Err("E12", "trust.csv", tid or where, "-",
                              "guan/min/yi 三轴全 0，这一行等于没写"))

        if not (r.get("note") or "").strip():
            errors.append(Err("E12", "trust.csv", tid or where, "-",
                              "note 为空（每个数都要能指回文档哪一节）"))

    for tid, cnt in sorted(seen.items()):
        if tid and cnt > 1:
            errors.append(Err("E12", "trust.csv", tid, "-", "id 重复（共 %d 条）" % cnt))

    route_rows = []
    if not os.path.isfile(routes_path):
        errors.append(Err("E12", "-", "routes.csv", "-",
                          "找不到 routes.csv（攻城路线就没处查门槛了）"))
    else:
        missing, route_rows = read_table(routes_path, ROUTE_COLS)
        if missing:
            errors.append(Err("E12", "-", "routes.csv", "-", "表头缺列：%s" % ", ".join(missing)))
            route_rows = []

    seen = Counter()
    for n, r in enumerate(route_rows, start=2):
        rid = (r.get("id") or "").strip()
        axis = (r.get("axis") or "").strip()
        where = "第 %d 行" % n
        seen[rid] += 1

        if not rid:
            errors.append(Err("E12", "routes.csv", where, "-", "id 为空"))
        if axis not in ROUTE_AXES:
            errors.append(Err("E12", "routes.csv", rid or where, "-",
                              "axis=%r 只能是 %s（疑心不是攻城路线）"
                              % (axis, " / ".join(ROUTE_AXES))))

        th, why = parse_coord(r.get("threshold"))
        if why:
            errors.append(Err("E12", "routes.csv", rid or where, "-", "threshold %s" % why))
        elif not 1 <= th <= 100:
            errors.append(Err("E12", "routes.csv", rid or where, "-",
                              "threshold=%d 应在 1..100" % th))

    for rid, cnt in sorted(seen.items()):
        if rid and cnt > 1:
            errors.append(Err("E12", "routes.csv", rid, "-", "id 重复（共 %d 条）" % cnt))

    return errors


def check_shops(shops_path, items_path, trust_path):
    """E13：官市/黑市货架。价与量、引用、以及"黑市不许比官市贵"。"""
    errors = []
    if not os.path.isfile(shops_path):
        return [Err("E13", "-", "shops.csv", "-",
                    "找不到 shops.csv（官市/黑市就没货可卖）")]

    missing, rows = read_table(shops_path, SHOP_COLS)
    if missing:
        return [Err("E13", "-", "shops.csv", "-", "表头缺列：%s" % ", ".join(missing))]

    item_ids = set()
    if os.path.isfile(items_path):
        _, items = read_table(items_path, ITEM_COLS)
        item_ids = {(r.get("id") or "").strip() for r in items}
        item_ids.discard("")

    price_of = {}          # (shop, item) -> 文
    for n, r in enumerate(rows, start=2):
        shop = (r.get("shop") or "").strip()
        iid = (r.get("item") or "").strip()
        where = "第 %d 行" % n
        who = "%s:%s" % (shop or where, iid or "?")

        if shop not in SHOP_NAMES:
            errors.append(Err("E13", "-", who, "-",
                              "shop=%r 只能是 %s" % (shop, " / ".join(SHOP_NAMES))))
        if item_ids and iid not in item_ids:
            errors.append(Err("E13", "-", who, "-",
                              "item %r 不在 items.csv（货架上没有的东西卖不出去）" % iid))

        price, why = parse_coord(r.get("price"))
        if why:
            errors.append(Err("E13", "-", who, "-", "price %s（单位是文，整数）" % why))
        elif price <= 0:
            errors.append(Err("E13", "-", who, "-", "price=%d 应为正整数文" % price))
        else:
            price_of[(shop, iid)] = price

        stock, swhy = parse_coord(r.get("stock"))
        if swhy:
            errors.append(Err("E13", "-", who, "-", "stock %s（这一趟能买的份数，整数）" % swhy))
        elif not STOCK_MIN <= stock <= STOCK_MAX:
            errors.append(Err("E13", "-", who, "-",
                              "stock=%d 应在 %d..%d" % (stock, STOCK_MIN, STOCK_MAX)))

        if not (r.get("note") or "").strip():
            errors.append(Err("E13", "-", who, "-",
                              "note 为空（折算系数要写在这里，否则价格无从复核）"))

    official = {(s, i) for s, i in price_of if s == "official"}
    black = {(s, i) for s, i in price_of if s == "black"}
    if not official:
        errors.append(Err("E13", "-", "shops.csv", "-", "官市一行都没有"))
    if not black:
        errors.append(Err("E13", "-", "shops.csv", "-", "黑市一行都没有"))

    both = {i for _, i in official} & {i for _, i in black}
    if official and black and not both:
        errors.append(Err("E13", "-", "shops.csv", "-",
                          "官市与黑市没有共同卖的物品（玩家看不见两边的价差）"))

    for iid in sorted(both):
        po, pb = price_of[("official", iid)], price_of[("black", iid)]
        if pb > po:
            errors.append(Err("E13", "-", "%s:%s" % ("black", iid), "-",
                              "黑市价 %d 文高于官市价 %d 文（§5.2：官市 ×1.2、黑市 ×0.75，"
                              "黑市只许更贱）" % (pb, po)))

    if os.path.isfile(trust_path):
        _, trust = read_table(trust_path, TRUST_COLS)
        have = {(r.get("id") or "").strip() for r in trust}
        for tid in MARKET_TRUST_IDS:
            if tid not in have:
                errors.append(Err("E13", "-", "trust.csv", "-",
                                  "缺 %s 一行（§5.2 的交易后果没处挂）" % tid))

    return errors


def check_siege_recruit(siege_path, recruit_path, routes_path, trust_path):
    """E14：攻城四法 + 招兵三档。两张都是手维护表，与文档 CSV 无关。"""
    errors = []

    # —— routes.csv 的 inside 阈值：needs 里的官望门槛要与它同一个数 ——
    inside_th = None
    if os.path.isfile(routes_path):
        _, route_rows = read_table(routes_path, ROUTE_COLS)
        for r in route_rows:
            if (r.get("id") or "").strip() == INSIDE_ROUTE_ID:
                val, why = parse_coord(r.get("threshold"))
                if not why:
                    inside_th = val

    if not os.path.isfile(siege_path):
        errors.append(Err("E14", "-", "siege.csv", "-", "找不到 siege.csv（攻城四法没处查代价）"))
    else:
        missing, rows = read_table(siege_path, SIEGE_COLS)
        if missing:
            errors.append(Err("E14", "-", "siege.csv", "-", "表头缺列：%s" % ", ".join(missing)))
        else:
            if len(rows) != SIEGE_ROWS:
                errors.append(Err("E14", "-", "siege.csv", "-",
                                  "行数应为 %d（%s），实际 %d"
                                  % (SIEGE_ROWS, " / ".join(SIEGE_IDS), len(rows))))

            seen = Counter()
            for n, r in enumerate(rows, start=2):
                sid = (r.get("id") or "").strip()
                where = "第 %d 行" % n
                seen[sid] += 1
                who = "siege:" + (sid or where)

                if sid not in SIEGE_IDS:
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "id=%r 不在 %s" % (sid, " / ".join(SIEGE_IDS))))
                if not (r.get("label") or "").strip():
                    errors.append(Err("E14", "siege.csv", who, "-", "label 为空（选择项没有名字）"))

                for col, lo, hi in (("ke", KE_MIN, KE_MAX),
                                    ("casualty", CASUALTY_MIN, CASUALTY_MAX),
                                    ("minyuan", MINYUAN_MIN, MINYUAN_MAX),
                                    ("tianming", TIANMING_MIN, TIANMING_MAX)):
                    val, why = parse_coord(r.get(col))
                    if why:
                        errors.append(Err("E14", "siege.csv", who, "-", "%s %s（只收整数）" % (col, why)))
                    elif not lo <= val <= hi:
                        errors.append(Err("E14", "siege.csv", who, "-",
                                          "%s=%d 应在 %d..%d" % (col, val, lo, hi)))

                tn, tnwhy = parse_coord(r.get("troops_needed"))
                if tnwhy:
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "troops_needed %s（打赢门槛要一个整数）" % tnwhy))
                elif not TROOPS_NEEDED_MIN <= tn <= TROOPS_NEEDED_MAX:
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "troops_needed=%d 应在 %d..%d（打赢门槛）"
                                      % (tn, TROOPS_NEEDED_MIN, TROOPS_NEEDED_MAX)))

                needs = (r.get("needs") or "").strip()
                ok, kind, th = need_kind(needs)
                if not ok:
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "needs=%r 不是四种写法之一（trust:guan>=60 / troops>=60 / "
                                      "flag:name / 空）——引擎没有别的解析器，写了就永远不可选" % needs))
                elif kind == "trust" and inside_th is not None:
                    if th != inside_th:
                        errors.append(Err("E14", "siege.csv", who, "-",
                                          "needs 的官望门槛 %d 与 routes.csv 的 %s 阈值 %d 不一致"
                                          "（两处打架，玩家看到两套门槛）"
                                          % (th, INSIDE_ROUTE_ID, inside_th)))
                elif kind == "trust" and inside_th is None:
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "needs 写了 trust 门槛，但 routes.csv 里没有可对齐的 %s 一行"
                                      % INSIDE_ROUTE_ID))

                if not (r.get("note") or "").strip():
                    errors.append(Err("E14", "siege.csv", who, "-",
                                      "note 为空（每个数要能指回文档哪一节，折算的要说清）"))

            for sid in SIEGE_IDS:
                if not seen.get(sid):
                    errors.append(Err("E14", "siege.csv", "siege:" + sid, "-", "四法缺这一行"))
            for sid, cnt in sorted(seen.items()):
                if sid and cnt > 1:
                    errors.append(Err("E14", "siege.csv", "siege:" + sid, "-",
                                      "id 重复（共 %d 条）" % cnt))

    # —— recruit.csv ——
    trust_ids = set()
    if os.path.isfile(trust_path):
        _, trust_rows = read_table(trust_path, TRUST_COLS)
        trust_ids = {(r.get("id") or "").strip() for r in trust_rows}
        trust_ids.discard("")

    if not os.path.isfile(recruit_path):
        errors.append(Err("E14", "-", "recruit.csv", "-",
                          "找不到 recruit.csv（招兵三档没处查收益）"))
        return errors

    missing, rows = read_table(recruit_path, RECRUIT_COLS)
    if missing:
        errors.append(Err("E14", "-", "recruit.csv", "-", "表头缺列：%s" % ", ".join(missing)))
        return errors

    if len(rows) != len(RECRUIT_OPTIONS):
        errors.append(Err("E14", "-", "recruit.csv", "-",
                          "行数应为 %d（给粮/给承诺/抢兵器），实际 %d"
                          % (len(RECRUIT_OPTIONS), len(rows))))

    seen = Counter()
    for n, r in enumerate(rows, start=2):
        opt = (r.get("option") or "").strip()
        where = "第 %d 行" % n
        seen[opt] += 1
        who = "recruit:" + (opt or where)

        if opt not in RECRUIT_OPTIONS:
            errors.append(Err("E14", "recruit.csv", who, "-",
                              "option=%r 只能是 0（给粮）/ 1（给承诺）/ 2（抢兵器）" % opt))

        troops, why = parse_coord(r.get("troops"))
        if why:
            errors.append(Err("E14", "recruit.csv", who, "-", "troops %s（只收整数）" % why))
        elif not RECRUIT_TROOPS_MIN <= troops <= RECRUIT_TROOPS_MAX:
            errors.append(Err("E14", "recruit.csv", who, "-",
                              "troops=%d 应在 %d..%d（招到多少人）"
                              % (troops, RECRUIT_TROOPS_MIN, RECRUIT_TROOPS_MAX)))

        tid = (r.get("trust") or "").strip()
        if tid and trust_ids and tid not in trust_ids:
            errors.append(Err("E14", "recruit.csv", who, "-",
                              "trust=%r 不在 trust.csv（这一档的三轴增减没处查）" % tid))

        if not (r.get("note") or "").strip():
            errors.append(Err("E14", "recruit.csv", who, "-",
                              "note 为空（出处与折算都要写在这）"))

    for opt in RECRUIT_OPTIONS:
        if not seen.get(opt):
            errors.append(Err("E14", "recruit.csv", "recruit:" + opt, "-", "三档缺这一行"))
    for opt, cnt in sorted(seen.items()):
        if opt and cnt > 1:
            errors.append(Err("E14", "recruit.csv", "recruit:" + opt, "-",
                              "option 重复（共 %d 条）" % cnt))

    return errors


def check_oaths_realms(oaths_path, realms_path, beats_path):
    """E15：誓约卡来源必须指得住一拍真选项；国号不许撞真实朝代。"""
    errors = []

    # —— 各拍的 options 长度：source 里的序号要落在这条区间里 ——
    opt_count = {}
    beats_ok = os.path.isfile(beats_path)
    if beats_ok:
        _, beat_rows = read_table(beats_path, BEATS_COLS)
        for r in beat_rows:
            bid = (r.get("id") or "").strip()
            raw = (r.get("options") or "").strip()
            opt_count[bid] = len([x for x in raw.split("|") if x.strip()])

    if not os.path.isfile(oaths_path):
        errors.append(Err("E15", "-", "oaths.csv", "-",
                          "找不到 oaths.csv（誓约卡没处查）"))
    else:
        missing, rows = read_table(oaths_path, OATH_COLS)
        if missing:
            errors.append(Err("E15", "-", "oaths.csv", "-", "表头缺列：%s" % ", ".join(missing)))
        else:
            if not rows:
                errors.append(Err("E15", "-", "oaths.csv", "-",
                                  "一行都没有（序章一句承诺都没留下，转段时沙盒里是空的）"))
            seen = Counter()
            for n, r in enumerate(rows, start=2):
                oid = (r.get("id") or "").strip()
                where = "第 %d 行" % n
                seen[oid] += 1
                who = "oath:" + (oid or where)

                if not TRUST_ID.match(oid):
                    errors.append(Err("E15", "oaths.csv", who, "-",
                                      "id 应是小写英文+下划线（如 oath_pioneer），实际 %r" % oid))

                for col, label in (("text", "内容"), ("beneficiary", "受益者"),
                                   ("deadline", "期限"), ("penalty", "违约惩罚")):
                    if not (r.get(col) or "").strip():
                        errors.append(Err("E15", "oaths.csv", who, "-",
                                          "%s（%s 列）为空——§14.2 的四件套缺一件这张卡就立不住"
                                          % (label, col)))

                src = (r.get("source") or "").strip()
                m = OATH_SOURCE.match(src)
                if not m:
                    errors.append(Err("E15", "oaths.csv", who, "-",
                                      "source=%r 应形如 b07:1（拍 id : 选项序号，0 起）" % src))
                elif not beats_ok:
                    errors.append(Err("E15", "oaths.csv", who, "-",
                                      "找不到 beats.csv，source=%r 无从核对" % src))
                else:
                    bid, k = m.group(1), int(m.group(2))
                    if bid not in opt_count:
                        errors.append(Err("E15", "oaths.csv", who, "-",
                                          "source 指向的拍 %s 不在 beats.csv" % bid))
                    elif opt_count[bid] == 0:
                        errors.append(Err("E15", "oaths.csv", who, "-",
                                          "source 指向的 %s 没有 options（这一拍发不出承诺，"
                                          "玩家永远拿不到这张卡）" % bid))
                    elif k >= opt_count[bid]:
                        errors.append(Err("E15", "oaths.csv", who, "-",
                                          "source=%s 指向不存在的选项：%s 只有 %d 个选项（0..%d）"
                                          % (src, bid, opt_count[bid], opt_count[bid] - 1)))

            for oid, cnt in sorted(seen.items()):
                if oid and cnt > 1:
                    errors.append(Err("E15", "oaths.csv", "oath:" + oid, "-",
                                      "id 重复（共 %d 条）" % cnt))

    # —— realms.csv：转段时玩家挑的那几个国号 ——
    if not os.path.isfile(realms_path):
        errors.append(Err("E15", "-", "realms.csv", "-",
                          "找不到 realms.csv（转段没国号可选）"))
        return errors

    missing, rows = read_table(realms_path, REALM_COLS)
    if missing:
        errors.append(Err("E15", "-", "realms.csv", "-", "表头缺列：%s" % ", ".join(missing)))
        return errors

    if len(rows) != REALM_ROWS:
        errors.append(Err("E15", "-", "realms.csv", "-",
                          "行数应为 %d 个国号备选，实际 %d" % (REALM_ROWS, len(rows))))

    seen = Counter()
    for n, r in enumerate(rows, start=2):
        rid = (r.get("id") or "").strip()
        label = (r.get("label") or "").strip()
        where = "第 %d 行" % n
        seen[rid] += 1
        who = "realm:" + (rid or where)

        if not TRUST_ID.match(rid):
            errors.append(Err("E15", "realms.csv", who, "-",
                              "id 应是小写英文+下划线，实际 %r" % rid))
        if not label:
            errors.append(Err("E15", "realms.csv", who, "-", "label（国号）为空"))
        elif label in REALM_FORBIDDEN_LABELS:
            errors.append(Err("E15", "realms.csv", who, "-",
                              "国号 %r 与真实朝代（或旧朝「晟」）重名，"
                              "架空王朝顶着别人的国号，士绅与史官都不认" % label))
        if not (r.get("motto") or "").strip():
            errors.append(Err("E15", "realms.csv", who, "-", "motto（年号/口号）为空"))

    for rid, cnt in sorted(seen.items()):
        if rid and cnt > 1:
            errors.append(Err("E15", "realms.csv", "realm:" + rid, "-",
                              "id 重复（共 %d 条）" % cnt))
    return errors


def check_travel(path):
    """E16：42 个有向组合齐全、hub 合法、ke 1..6、来回差 ≤2、每个 hub 有进有出。"""
    errors = []
    if not os.path.isfile(path):
        return [Err("E16", "-", "travel.csv", "-",
                    "找不到 travel.csv（七个空间之间就没有赶路的刻度）")]

    missing, rows = read_table(path, TRAVEL_COLS)
    if missing:
        return [Err("E16", "-", "travel.csv", "-", "表头缺列：%s" % ", ".join(missing))]

    want = {(a, b) for a in HUBS for b in HUBS if a != b}
    if len(rows) != len(want):
        errors.append(Err("E16", "-", "travel.csv", "-",
                          "行数应为 %d（7 个空间两两有向），实际 %d" % (len(want), len(rows))))

    ke_of = {}
    for n, r in enumerate(rows, start=2):
        a = (r.get("from") or "").strip()
        b = (r.get("to") or "").strip()
        where = "第 %d 行" % n
        who = "%s→%s" % (a or where, b or "?")

        if a not in HUBS or b not in HUBS:
            errors.append(Err("E16", "-", who, "-",
                              "from/to 应在 %s..%s（实际 from=%r to=%r）"
                              % (HUBS[0], HUBS[-1], a, b)))
            continue
        if a == b:
            errors.append(Err("E16", a, who, "-", "from 与 to 相同（原地不必走）"))
            continue
        if (a, b) in ke_of:
            errors.append(Err("E16", a, who, "-", "同一对重复（前面已经写过一次）"))
            continue

        ke, why = parse_coord(r.get("ke"))
        if why:
            errors.append(Err("E16", a, who, "-", "ke %s（要整数刻数）" % why))
        elif not TRAVEL_KE_MIN <= ke <= TRAVEL_KE_MAX:
            errors.append(Err("E16", a, who, "-",
                              "ke=%d 应在 %d..%d（一趟路要走掉半个 hub 的预算就没人出城了）"
                              % (ke, TRAVEL_KE_MIN, TRAVEL_KE_MAX)))
        else:
            ke_of[(a, b)] = ke

        if not (r.get("note") or "").strip():
            errors.append(Err("E16", a, who, "-",
                              "note 为空（远近与绕路的依据要写在这，尤其是来回不等的）"))

    for a, b in sorted(want - set(ke_of)):
        errors.append(Err("E16", a, "%s→%s" % (a, b), "-",
                          "缺这一组（7×6 要齐，缺一组这个出口就走不通）"))

    for hub in HUBS:
        if not any(a == hub for a, _ in ke_of):
            errors.append(Err("E16", hub, "travel.csv", "-", "没有任何一条从这里出发（走不出去）"))
        if not any(b == hub for _, b in ke_of):
            errors.append(Err("E16", hub, "travel.csv", "-", "没有任何一条走进这里（进不来）"))

    for a in HUBS:
        for b in HUBS:
            if a >= b:
                continue
            ab, ba = ke_of.get((a, b)), ke_of.get((b, a))
            if ab is not None and ba is not None and abs(ab - ba) > TRAVEL_PAIR_SLACK:
                errors.append(Err("E16", a, "%s↔%s" % (a, b), "-",
                                  "来回差 %d 刻（去 %d / 回 %d），超过 %d 刻说明表写错了"
                                  "（山路可以有差，但不会差一倍）"
                                  % (abs(ab - ba), ab, ba, TRAVEL_PAIR_SLACK)))
    return errors


def check_intro(path, npc_names):
    """E16：seq 从 1 连续、行数 6..10、speaker/portrait 指得住 npcs.csv、旁白不挂立绘。"""
    errors = []
    if not os.path.isfile(path):
        return [Err("E16", "-", "intro.csv", "-", "找不到 intro.csv（开场就没词了）")]

    missing, rows = read_table(path, INTRO_COLS)
    if missing:
        return [Err("E16", "-", "intro.csv", "-", "表头缺列：%s" % ", ".join(missing))]

    if not INTRO_ROWS_MIN <= len(rows) <= INTRO_ROWS_MAX:
        errors.append(Err("E16", "-", "intro.csv", "-",
                          "行数应在 %d..%d，实际 %d" % (INTRO_ROWS_MIN, INTRO_ROWS_MAX, len(rows))))

    for i, r in enumerate(rows, start=1):
        seq = (r.get("seq") or "").strip()
        speaker = (r.get("speaker") or "").strip()
        portrait = (r.get("portrait") or "").strip()
        who = "seq:%s" % (seq or "第 %d 行" % (i + 1))

        got, why = parse_coord(seq)
        if why or got != i:
            errors.append(Err("E16", "-", "intro.csv", who,
                              "seq 应连续从 1 起（第 %d 行应为 %d，实际 %r）" % (i, i, seq)))

        if speaker and speaker not in npc_names:
            errors.append(Err("E16", "-", who, "-",
                              "speaker %r 不在 npcs.csv（这句话没有人能说）" % speaker))
        if portrait and portrait not in npc_names:
            errors.append(Err("E16", "-", who, "-",
                              "portrait %r 不在 npcs.csv（这张立绘没处取）" % portrait))
        if not speaker and portrait:
            errors.append(Err("E16", "-", who, "-",
                              "旁白（speaker 为空）不该挂立绘 %r" % portrait))
        if not (r.get("text") or "").strip():
            errors.append(Err("E16", "-", who, "-", "text 为空"))
        if not (r.get("note") or "").strip():
            errors.append(Err("E16", "-", who, "-",
                              "note 为空（出处要写在这：文档节号或 npcs.csv 行）"))
    return errors


def check_portraits(path, npcs):
    """E16：30 行、名字与 npcs.csv 一一对上、参数在范围内、同 hub 不许两人七参数全同。"""
    errors = []
    if not os.path.isfile(path):
        return [Err("E16", "-", "portraits.csv", "-",
                    "找不到 portraits.csv（所有 NPC 只能共用一张脸）")]

    missing, rows = read_table(path, PORTRAIT_COLS)
    if missing:
        return [Err("E16", "-", "portraits.csv", "-", "表头缺列：%s" % ", ".join(missing))]

    if len(rows) != PORTRAIT_ROWS:
        errors.append(Err("E16", "-", "portraits.csv", "-",
                          "行数应为 %d（npcs.csv 的 30 处放置），实际 %d"
                          % (PORTRAIT_ROWS, len(rows))))

    placed = Counter((r.get("name") or "").strip() for r in npcs)
    placed.pop("", None)
    hubs_of = {}
    for r in npcs:
        name = (r.get("name") or "").strip()
        hub = (r.get("hub") or "").strip()
        if name and hub in HUBS:
            hubs_of.setdefault(name, set()).add(hub)

    params_of = {}          # 名字 -> 七元组（同一人两处放置只留第一次）
    seen = Counter()
    for n, r in enumerate(rows, start=2):
        name = (r.get("npc") or "").strip()
        where = "第 %d 行" % n
        seen[name] += 1

        if name not in placed:
            errors.append(Err("E16", "-", "portraits:" + (name or where), "-",
                              "npc %r 不在 npcs.csv" % name))
            continue

        vals = []
        for col in PORTRAIT_PARAMS:
            lo, hi = PORTRAIT_RANGES[col]
            val, why = parse_coord(r.get(col))
            if why:
                errors.append(Err("E16", "-", name, "-", "%s %s（只收整数）" % (col, why)))
                vals.append(None)
            elif not lo <= val <= hi:
                errors.append(Err("E16", "-", name, "-",
                                  "%s=%d 应在 %d..%d" % (col, val, lo, hi)))
                vals.append(None)
            else:
                vals.append(val)

        if None not in vals:
            seven = tuple(vals)
            if name in params_of and params_of[name] != seven:
                errors.append(Err("E16", "-", name, "-",
                                  "同一个人在 %s 与别处被放置，两次的立绘参数不一致"
                                  "（%r / %r）——要么是同两个人，要么是同一个人换了两张脸"
                                  % ("、".join(sorted(hubs_of.get(name, {"?"}))),
                                     params_of[name], seven)))
            params_of.setdefault(name, seven)

        if not (r.get("note") or "").strip():
            errors.append(Err("E16", "-", name or where, "-",
                              "note 为空（依据要写在这：台词或身份）"))

    for name in sorted(placed):
        if not seen.get(name):
            errors.append(Err("E16", "-", "portraits:" + name, "-",
                              "npcs.csv 里有这个名字，这里没有立绘参数"))
        elif seen[name] != placed[name]:
            errors.append(Err("E16", "-", "portraits:" + name, "-",
                              "npcs.csv 里放置 %d 次，这里 %d 行（同一人多处放置要各写一行）"
                              % (placed[name], seen[name])))

    # 同 hub 不许两个人七个参数全同：立绘是玩家唯一的辨识手段，撞脸等于没画
    by_hub = {}
    for name, seven in params_of.items():
        for hub in hubs_of.get(name, ()):
            by_hub.setdefault(hub, []).append((name, seven))
    for hub in HUBS:
        people = by_hub.get(hub, [])
        for i in range(len(people)):
            for j in range(i + 1, len(people)):
                a, ta = people[i]
                b, tb = people[j]
                if a != b and ta == tb:
                    errors.append(Err("E16", hub, "%s/%s" % (a, b), "-",
                                      "七个参数完全相同 %r，同一 hub 里分不清谁是谁" % (ta,)))
    return errors


def validate(maps_dir, data_dir, doc_csv=None, beats_path=None):
    """返回 (errors, notes, checked, note_detail)。不打印，不做退出码。

    doc_csv / beats_path 传 None = 这次不查（selftest 用的是临时数据，比不了真实的文档 CSV）。
    items.csv / gifts.csv / trust.csv / routes.csv 始终查：它们是手维护表，与文档 CSV 无关。
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

    # —— E12 信任轴 / 攻城路线 ——
    errors.extend(check_trust_routes(os.path.join(data_dir, "trust.csv"),
                                     os.path.join(data_dir, "routes.csv")))

    # —— E13 官市 / 黑市货架 ——
    errors.extend(check_shops(os.path.join(data_dir, "shops.csv"),
                              os.path.join(data_dir, "items.csv"),
                              os.path.join(data_dir, "trust.csv")))

    # —— E14 攻城四法 / 招兵三档 ——
    errors.extend(check_siege_recruit(os.path.join(data_dir, "siege.csv"),
                                      os.path.join(data_dir, "recruit.csv"),
                                      os.path.join(data_dir, "routes.csv"),
                                      os.path.join(data_dir, "trust.csv")))

    # —— E15 誓约卡 / 国号 ——
    errors.extend(check_oaths_realms(os.path.join(data_dir, "oaths.csv"),
                                     os.path.join(data_dir, "realms.csv"),
                                     os.path.join(data_dir, "beats.csv")))

    # —— E16 地图位移 / 开场介绍 / 立绘参数 ——
    errors.extend(check_travel(os.path.join(data_dir, "travel.csv")))
    errors.extend(check_intro(os.path.join(data_dir, "intro.csv"),
                              {name for _hub, name in npc_names}))
    errors.extend(check_portraits(os.path.join(data_dir, "portraits.csv"), npcs))

    errors.sort(key=lambda e: e.sort_key())
    return errors, notes, checked, note_detail


def report(errors, notes, checked, note_detail, show_notes):
    print("《举火》内容校验 · points + npcs + maps(hub1..hub7) + beats.csv + items/gifts + "
          "信任轴/路线 + 官市/黑市 + 攻城四法/招兵 + 誓约/国号 + 位移/开场/立绘 + 两份 points 同源")
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


def _selftest_e12(tmp):
    """E12 自测：routes.axis 写成 yi + trust 里一行三轴全 0，两处都必须报出。"""
    _, trust = read_table(os.path.join(DEFAULT_DATA, "trust.csv"), TRUST_COLS)
    _, routes = read_table(os.path.join(DEFAULT_DATA, "routes.csv"), ROUTE_COLS)

    bad_trust = [dict(r) for r in trust]
    bad_trust.append({"id": "ghost_act", "label": "坏样本",
                      "guan": "0", "min": "0", "yi": "0", "note": "坏样本：三轴全 0"})
    bad_routes = [dict(r) for r in routes]
    bad_routes.append({"id": "ghost_route", "label": "坏样本", "axis": "yi",
                       "threshold": "60", "note": "坏样本：axis 写了疑心"})

    d = os.path.join(tmp, "e12")
    os.makedirs(d)
    _write_csv(os.path.join(d, "trust.csv"), TRUST_COLS, bad_trust)
    _write_csv(os.path.join(d, "routes.csv"), ROUTE_COLS, bad_routes)
    msgs = [e.msg for e in check_trust_routes(os.path.join(d, "trust.csv"),
                                             os.path.join(d, "routes.csv"))]
    zero_hit = any("三轴全 0" in m for m in msgs)
    axis_hit = any("axis=" in m for m in msgs)
    return zero_hit and axis_hit


def _selftest_e13(tmp):
    """E13 自测：黑市比官市贵 + 卖 items.csv 里没有的东西，两处都必须报出。"""
    items_path = os.path.join(DEFAULT_DATA, "items.csv")
    trust_path = os.path.join(DEFAULT_DATA, "trust.csv")
    _, shops = read_table(os.path.join(DEFAULT_DATA, "shops.csv"), SHOP_COLS)

    bad = [dict(r) for r in shops]
    for r in bad:                      # 同一件物品：黑市改到比官市还贵
        if r["shop"] == "official" and r["item"] == "grain":
            r["price"] = "6"
        if r["shop"] == "black" and r["item"] == "grain":
            r["price"] = "9"
    bad.append({"shop": "black", "item": "ghost_item", "price": "10",
                "stock": "3", "note": "坏样本：items.csv 里没有这件东西"})

    d = os.path.join(tmp, "e13")
    os.makedirs(d)
    _write_csv(os.path.join(d, "shops.csv"), SHOP_COLS, bad)
    msgs = [e.msg for e in check_shops(os.path.join(d, "shops.csv"), items_path, trust_path)]
    dearer_hit = any("高于官市价" in m for m in msgs)
    ghost_hit = any("不在 items.csv" in m for m in msgs)
    return dearer_hit and ghost_hit


def _selftest_e14(tmp):
    """E14 自测：needs 写成中文比较式 + recruit 的 trust 指向不存在的动作，两处都要报出。"""
    _, siege = read_table(os.path.join(DEFAULT_DATA, "siege.csv"), SIEGE_COLS)
    _, recruit = read_table(os.path.join(DEFAULT_DATA, "recruit.csv"), RECRUIT_COLS)
    routes_path = os.path.join(DEFAULT_DATA, "routes.csv")
    trust_path = os.path.join(DEFAULT_DATA, "trust.csv")

    bad_siege = [dict(r) for r in siege]
    for r in bad_siege:
        if r["id"] == "inside":
            r["needs"] = "官望>60"          # 中文比较式：没有解析器
        if r["id"] == "burn":
            r["troops_needed"] = "0"        # 打赢门槛不能是 0，也不能空
    bad_recruit = [dict(r) for r in recruit]
    for r in bad_recruit:
        if r["option"] == "0":
            r["trust"] = "ghost_act"        # trust.csv 里没有这条

    d = os.path.join(tmp, "e14")
    os.makedirs(d)
    _write_csv(os.path.join(d, "siege.csv"), SIEGE_COLS, bad_siege)
    _write_csv(os.path.join(d, "recruit.csv"), RECRUIT_COLS, bad_recruit)
    msgs = [e.msg for e in check_siege_recruit(os.path.join(d, "siege.csv"),
                                              os.path.join(d, "recruit.csv"),
                                              routes_path, trust_path)]
    need_hit = any("不是四种写法之一" in m for m in msgs)
    trust_hit = any("不在 trust.csv" in m for m in msgs)
    troops_hit = any("troops_needed" in m for m in msgs)
    return need_hit and trust_hit and troops_hit


def _selftest_e15(tmp):
    """E15 自测：source 指向不存在的选项 + 国号与真实朝代重名，两处都要报出。"""
    _, oaths = read_table(os.path.join(DEFAULT_DATA, "oaths.csv"), OATH_COLS)
    _, realms = read_table(os.path.join(DEFAULT_DATA, "realms.csv"), REALM_COLS)
    beats_path = os.path.join(DEFAULT_DATA, "beats.csv")

    bad_oaths = [dict(r) for r in oaths]
    bad_oaths[0]["source"] = "b07:9"        # b07 只有 3 个选项（给粮/给承诺/抢兵器）
    bad_realms = [dict(r) for r in realms]
    bad_realms[0]["label"] = "唐"           # 真实朝代名，不许当国号

    d = os.path.join(tmp, "e15")
    os.makedirs(d)
    _write_csv(os.path.join(d, "oaths.csv"), OATH_COLS, bad_oaths)
    _write_csv(os.path.join(d, "realms.csv"), REALM_COLS, bad_realms)
    msgs = [e.msg for e in check_oaths_realms(os.path.join(d, "oaths.csv"),
                                             os.path.join(d, "realms.csv"), beats_path)]
    src_hit = any("不存在的选项" in m for m in msgs)
    name_hit = any("重名" in m for m in msgs)
    return src_hit and name_hit


def _selftest_e16(tmp):
    """E16 自测：travel 少一组 + intro 的 speaker 不存在 + portraits 参数越界。"""
    _, npcs = read_table(os.path.join(DEFAULT_DATA, "npcs.csv"), NPC_COLS)
    names = {(r.get("name") or "").strip() for r in npcs}
    names.discard("")

    _, travel = read_table(os.path.join(DEFAULT_DATA, "travel.csv"), TRAVEL_COLS)
    _, intro = read_table(os.path.join(DEFAULT_DATA, "intro.csv"), INTRO_COLS)
    _, portraits = read_table(os.path.join(DEFAULT_DATA, "portraits.csv"), PORTRAIT_COLS)

    bad_travel = [dict(r) for r in travel][:-1]      # 少一组有向组合
    bad_intro = [dict(r) for r in intro]
    bad_intro[0]["speaker"] = "没有这个人"            # 没有人能说这句话
    bad_portraits = [dict(r) for r in portraits]
    bad_portraits[0]["skin"] = "9"                   # 肤色只有 1..4

    d = os.path.join(tmp, "e16")
    os.makedirs(d)
    _write_csv(os.path.join(d, "travel.csv"), TRAVEL_COLS, bad_travel)
    _write_csv(os.path.join(d, "intro.csv"), INTRO_COLS, bad_intro)
    _write_csv(os.path.join(d, "portraits.csv"), PORTRAIT_COLS, bad_portraits)

    travel_msgs = [e.msg for e in check_travel(os.path.join(d, "travel.csv"))]
    intro_msgs = [e.msg for e in check_intro(os.path.join(d, "intro.csv"), names)]
    portrait_msgs = [e.msg for e in check_portraits(os.path.join(d, "portraits.csv"), npcs)]

    pair_hit = any("缺这一组" in m for m in travel_msgs)
    speaker_hit = any("不在 npcs.csv" in m for m in intro_msgs)
    range_hit = any("skin=" in m for m in portrait_msgs)
    return pair_hit and speaker_hit and range_hit


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
        # E11–E15 的表照抄真表：这一轮只验 E01–E05，别让别的红点混进来
        for name, cols in (("items.csv", ITEM_COLS), ("gifts.csv", GIFT_COLS),
                           ("trust.csv", TRUST_COLS), ("routes.csv", ROUTE_COLS),
                           ("shops.csv", SHOP_COLS), ("siege.csv", SIEGE_COLS),
                           ("recruit.csv", RECRUIT_COLS), ("oaths.csv", OATH_COLS),
                           ("realms.csv", REALM_COLS), ("beats.csv", BEATS_COLS),
                           ("travel.csv", TRAVEL_COLS), ("intro.csv", INTRO_COLS),
                           ("portraits.csv", PORTRAIT_COLS)):
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
        if not _selftest_e12(tmp):
            print("[selftest] 信任轴/路线 E12 未报出")
            return 1
        print("[selftest] 信任轴/路线 E12 1/1")
        if not _selftest_e13(tmp):
            print("[selftest] 官市/黑市 E13 未报出")
            return 1
        print("[selftest] 官市/黑市 E13 1/1")
        if not _selftest_e14(tmp):
            print("[selftest] 攻城四法/招兵 E14 未报出")
            return 1
        print("[selftest] 攻城四法/招兵 E14 1/1")
        if not _selftest_e15(tmp):
            print("[selftest] 誓约/国号 E15 未报出")
            return 1
        print("[selftest] 誓约/国号 E15 1/1")
        if not _selftest_e16(tmp):
            print("[selftest] 位移/开场/立绘 E16 未报出")
            return 1
        print("[selftest] 位移/开场/立绘 E16 1/1")
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
    ap.add_argument("--selftest", action="store_true",
                    help="造错样本：E01–E05 五个 + E09..E16 各一处")
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
