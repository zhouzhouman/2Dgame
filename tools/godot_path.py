#!/usr/bin/env python3
"""解析 Godot 可执行文件的绝对路径。

为什么需要这个
    Godot 装了但不一定在 PATH 里：长驻的 IDE / 终端可能拿着旧的环境变量，
    CI 里的路径又和本机不同。与其在文档里写死绝对路径，不如让脚本自己找。

用法
    GODOT="$(python tools/godot_path.py)"
    "$GODOT" --headless --path Juhuo --quit -- --audit

查找顺序
    1. 环境变量 GODOT_BIN
    2. PATH 里的 godot / godot_console
    3. 常见安装目录（Windows 与 Linux/macOS 各一组）

找不到就退出码 1，并把检查过的地方打印出来。
"""

from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

# 同一个目录里，console 版优先：Windows 上 godot.exe 是 GUI 子系统，
# 输出不挂控制台，headless 日志容易丢。
CONSOLE_NAMES = ("godot_console", "godot_console.exe", "Godot_v4*_mono_win64_console.exe")
PLAIN_NAMES = ("godot", "godot.exe", "Godot_v4*_mono_win64.exe")

WINDOWS_DIRS = [
    r"D:\Tools",
    r"C:\Tools",
    r"C:\Program Files\Godot",
    r"C:\Program Files (x86)\Godot",
    r"C:\Godot",
]
POSIX_DIRS = [
    "/usr/local/bin",
    "/usr/bin",
    "/opt",
    str(Path.home() / "Godot"),
    str(Path.home() / ".local/bin"),
    str(Path.home() / "Downloads"),
]


def _glob_dirs(base: str) -> list[Path]:
    p = Path(base)
    if not p.is_dir():
        return []
    if p.name.lower().startswith("godot"):
        return [p]
    try:
        return sorted(d for d in p.glob("Godot*") if d.is_dir())
    except OSError:
        return []


def _first_hit(dirs: list[Path], names: tuple[str, ...]) -> Path | None:
    for d in dirs:
        for name in names:
            if "*" in name:
                hits = sorted(d.glob(name))
                if hits:
                    return hits[0]
                continue
            hit = d / name
            if hit.is_file():
                return hit
    return None


def find_godot() -> Path | None:
    env = os.environ.get("GODOT_BIN", "").strip()
    if env and Path(env).is_file():
        return Path(env)

    for name in CONSOLE_NAMES + PLAIN_NAMES:
        if "*" in name:
            continue
        found = shutil.which(name)
        if found:
            return Path(found)

    if os.name == "nt":
        roots = [Path(d) for d in WINDOWS_DIRS]
        candidates: list[Path] = []
        for r in roots:
            candidates.extend(_glob_dirs(str(r)))
        hit = _first_hit(candidates, CONSOLE_NAMES) or _first_hit(candidates, PLAIN_NAMES)
        if hit:
            return hit
    else:
        candidates = [Path(d) for d in POSIX_DIRS if Path(d).is_dir()]
        hit = _first_hit(candidates, CONSOLE_NAMES + PLAIN_NAMES)
        if hit:
            return hit

    return None


def main() -> int:
    hit = find_godot()
    if hit is None:
        print("找不到 Godot。已检查：", file=sys.stderr)
        print("  1. 环境变量 GODOT_BIN", file=sys.stderr)
        print("  2. PATH 中的 godot / godot_console", file=sys.stderr)
        print("  3. " + ", ".join(WINDOWS_DIRS if os.name == "nt" else POSIX_DIRS), file=sys.stderr)
        print("装好后可以：setx GODOT_BIN \"<绝对路径>\"", file=sys.stderr)
        return 1

    print(hit)
    return 0


if __name__ == "__main__":
    sys.exit(main())
