"""从《举火_序章NPC对话_v1.0.md》提取台词，输出引擎可读的 CSV。

用法：
    python tools/extract_npc_dialogue.py

输出：
    Juhuo/Content/data/npcs.csv
    列：hub, name, x, y, first, again, interrupt

只取三段——首次 / 再谈 / 打断。分支和赠礼暂时不进表：
前者要旗标系统配合，后者要背包，都属于下一层。先用三段把"能说话"跑通。
"""
import csv
import re
from pathlib import Path

SRC = Path("举火_序章NPC对话_v1.0.md")
DST = Path("Juhuo/Content/data/npcs.csv")

# ## Hub 2 ｜ 逃亡路·废驿
RE_HUB = re.compile(r"^##\s*Hub\s*(\d+)")
# ### A ／ 沈砚 ｜ 火堆边 (12,4)
RE_NPC = re.compile(r"^###\s*([A-Z])\s*[／/]\s*(.+?)\s*(?:[｜|][^(]*)?\((\d+)\s*,\s*(\d+)\)")
# **首次**  /  **再谈**  /  **打断**：「……」
RE_SECTION = re.compile(r"^\*\*(首次|再谈|打断|默认)(?:（[^）]*）)?\*\*[：:]?\s*(.*)$")
# 「若你回头看了他一眼（他还活着）」这种也算首次
RE_IF = re.compile(r"^\*\*若.*?\*\*\s*$")
RE_QUOTE = re.compile(r"^>\s?(.*)$")
RE_BOLD = re.compile(r"^\*\*")


def clean(text: str) -> str:
    text = text.strip()
    text = text.replace("**", "")
    # 去掉「」外的 markdown 强调，但保留对话本身的引号
    return text


def main():
    lines = SRC.read_text(encoding="utf-8").split("\n")

    rows = []
    hub = None
    npc = None
    section = None
    buf = {"首次": [], "再谈": [], "打断": []}

    def flush():
        if npc is None:
            return
        first = " ".join(x for x in buf["首次"] if x)
        again = " ".join(x for x in buf["再谈"] if x)
        stop = " ".join(x for x in buf["打断"] if x)
        if first or again:
            rows.append({
                "hub": f"hub{hub}",
                "name": npc["name"],
                "x": npc["x"],
                "y": npc["y"],
                "first": first,
                "again": again,
                "interrupt": stop,
            })

    for raw in lines:
        line = raw.rstrip()

        m = RE_HUB.match(line)
        if m:
            flush()
            npc = None
            section = None
            hub = int(m.group(1))
            buf = {"首次": [], "再谈": [], "打断": []}
            continue

        m = RE_NPC.match(line)
        if m:
            flush()
            npc = {"name": clean(m.group(2)), "x": int(m.group(3)), "y": int(m.group(4))}
            section = None
            buf = {"首次": [], "再谈": [], "打断": []}
            continue

        m = RE_SECTION.match(line)
        if m and npc is not None:
            raw_sec = m.group(1)
            # 「默认」在营地里就是首次见面的台词
            section = "首次" if raw_sec == "默认" else raw_sec
            rest = clean(m.group(2))
            if rest:
                buf[section].append(rest)
            continue

        if RE_IF.match(line) and npc is not None:
            section = "首次"
            continue

        # 遇到新的加粗小节就结束当前引用块
        if RE_BOLD.match(line) and not RE_SECTION.match(line):
            section = None
            continue

        if line.strip() == "---":
            flush()
            npc = None
            section = None
            buf = {"首次": [], "再谈": [], "打断": []}
            continue

        m = RE_QUOTE.match(line)
        if m and section and npc is not None:
            buf[section].append(clean(m.group(1)))

    flush()

    DST.parent.mkdir(parents=True, exist_ok=True)
    with DST.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["hub", "name", "x", "y", "first", "again", "interrupt"])
        w.writeheader()
        w.writerows(rows)

    print(f"写出 {DST}：{len(rows)} 位 NPC")
    by_hub = {}
    for r in rows:
        by_hub.setdefault(r["hub"], []).append(r)
    for h in sorted(by_hub):
        print(f"  {h}: {len(by_hub[h])} 位")
        for r in by_hub[h][:3]:
            print(f"    ({r['x']:>2},{r['y']:>2}) {r['name']:8} {r['first'][:26]}")
    empty = [r["name"] for r in rows if not r["first"]]
    if empty:
        print("  ⚠ 无首次台词:", empty)


if __name__ == "__main__":
    main()
