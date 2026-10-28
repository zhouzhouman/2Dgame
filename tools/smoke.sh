#!/usr/bin/env bash
# 一键本地门禁：编译 → 单测 → 内容校验 → Core 纯净性 → 三套引擎自检 → 存档自检
#
# 为什么要这个
#   每次交付要敲七八条命令，手敲容易漏一条，而漏掉的那条往往正是会红的。
#   这条脚本按顺序跑完，任何一步失败立刻停并打印失败项。
#
# 用法
#   bash tools/smoke.sh            # 全跑
#   bash tools/smoke.sh --fast     # 跳过引擎自检（只跑编译/单测/校验），约 10 秒
#
# 退出码：0 全绿；1 有失败（会打印是哪一项）

set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

FAST=0
[ "${1:-}" = "--fast" ] && FAST=1

PASS=0
FAIL=0
FAILED_STEPS=""

step() {
  local name="$1"; shift
  printf '\033[1m▶ %s\033[0m\n' "$name"
  if "$@"; then
    PASS=$((PASS + 1))
    printf '\033[32m  ✓ %s\033[0m\n\n' "$name"
  else
    FAIL=$((FAIL + 1))
    FAILED_STEPS="$FAILED_STEPS\n  - $name"
    printf '\033[31m  ✗ %s\033[0m\n\n' "$name"
    return 1
  fi
}

# ── 1. 编译 ──────────────────────────────────────────────
compile() {
  local out
  out="$(bash tools/dotnet.sh build Juhuo/Juhuo.sln --nologo 2>&1)"
  echo "$out" | tail -3
  # 0 警告 0 错误 是硬要求
  echo "$out" | grep -qE "0 个警告" && echo "$out" | grep -qE "0 个错误"
}
step "编译（要求 0 警告 0 错误）" compile || true

# ── 2. 单测 ──────────────────────────────────────────────
unit_tests() {
  local out
  out="$(bash tools/dotnet.sh test Juhuo/Tests/Core.Tests/Core.Tests.csproj --nologo -v q 2>&1)"
  echo "$out" | grep -E "已通过!|失败!" | tail -1
  echo "$out" | grep -q "已通过!" && ! echo "$out" | grep -q "失败!"
}
step "Core 单测（全绿）" unit_tests || true

# ── 3. 内容校验 ──────────────────────────────────────────
validate_selftest() {
  python tools/validate.py --selftest 2>&1 | tail -1
  python tools/validate.py --selftest >/dev/null 2>&1
}
step "校验器自测（五类错都要能报出）" validate_selftest || true

validate_data() {
  local out
  out="$(python tools/validate.py 2>&1)"
  echo "$out" | tail -1
  echo "$out" | grep -qE "错误 0 条"
}
step "内容校验（当前数据 0 错）" validate_data || true

# ── 4. Core 纯净性 ───────────────────────────────────────
purity() {
  if grep -rnE "using +Godot|Godot\.[A-Z]|GodotSharp" Juhuo/Src/Core \
       --include="*.cs" --include="*.csproj" 2>/dev/null; then
    echo "  ↑ Src/Core 里出现了 Godot 引用"
    return 1
  fi
  echo "  Src/Core 无 Godot 引用"
}
step "Core 纯净性" purity || true

if [ "$FAST" = "0" ]; then
  # ── 5. 引擎自检 ─────────────────────────────────────────
  # GODOT 可以用环境变量覆盖（便于 CI 或指定某个引擎）
  GODOT="${GODOT:-$(python tools/godot_path.py)}" || { echo "找不到 Godot"; exit 1; }
  echo "  引擎：$GODOT"

  audit() {
    local out
    out="$("$GODOT" --headless --path Juhuo -- --audit 2>&1)"
    echo "$out" | tail -1
    echo "$out" | grep -q "够不着 0 件"
  }
  step "关卡可达性（120 件全部够得着）" audit || true

  chain() {
    local out
    out="$("$GODOT" --headless --path Juhuo --quit -- --chain 2>&1)"
    echo "$out" | tail -1
    echo "$out" | grep -q "hub7"
  }
  step "七关串联" chain || true

  tutrun() {
    local out
    out="$("$GODOT" --headless --path Juhuo --quit -- --tutrun 2>&1)"
    echo "$out" | tail -1
    echo "$out" | grep -q "完成"
  }
  step "引导五步" tutrun || true

  savetest() {
    local out
    out="$("$GODOT" --headless --path Juhuo --quit -- --savetest 2>&1)"
    echo "$out" | tail -1
    echo "$out" | grep -q "一致"
  }
  step "存档写入→重读" savetest || true
fi

# ── 汇总 ────────────────────────────────────────────────
echo "────────────────────────────────────────"
if [ "$FAIL" -eq 0 ]; then
  printf '\033[32m全部通过：%d 项\033[0m\n' "$PASS"
  exit 0
else
  printf '\033[31m失败 %d 项，通过 %d 项\033[0m\n' "$FAIL" "$PASS"
  printf '失败项：%b\n' "$FAILED_STEPS"
  exit 1
fi
