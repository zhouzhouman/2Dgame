#!/usr/bin/env bash
# 统一入口跑 dotnet：补齐 NuGet 初始化需要的环境变量。
#
# 为什么需要它
#   某些受限环境（容器 / 沙箱 / 精简的 CI 镜像）里 ProgramFiles、ProgramFiles(x86)、
#   LOCALAPPDATA、NUGET_PACKAGES 这些变量是空的。NuGet 的静态初始化会因此崩在
#   "Value cannot be null. (Parameter 'path1')"（NuGetEnvironment.GetFolderPath 里
#   对空值做 Path.Combine），表现是：
#     - dotnet build / dotnet test 报 NuGet.targets(789,5) 错误
#     - dotnet nuget list source 直接抛类型初始化异常
#     - Godot.NET.Sdk 解析不出来（"SDK 解析程序 NuGetSdkResolver 失败"）
#   补齐这些变量，NuGet 立刻恢复正常。普通 Windows 终端本来就有，
#   所以这里只在缺失时才补，不覆盖已有值。
#
# 跨平台
#   Windows（MSYS/Git Bash/Cygwin）：补 ProgramFiles、ProgramFiles(x86)、ProgramW6432、
#     LOCALAPPDATA、APPDATA、DOTNET_CLI_HOME、NUGET_PACKAGES。
#   Linux / macOS：只补 DOTNET_CLI_HOME 与 NUGET_PACKAGES（用 $HOME），
#     绝不导出 Windows 专有变量——那会把 /c/... 这种不存在的路径塞给 Linux runner。
#
# 用法
#   bash tools/dotnet.sh build Juhuo/Juhuo.sln
#   bash tools/dotnet.sh test Juhuo/Tests/Core.Tests/Core.Tests.csproj
#   bash tools/dotnet.sh nuget list source
#
# 查看**生效值**（不是继承值；这两者可能不同，见 --print-env）
#   bash tools/dotnet.sh --print-env

set -euo pipefail

# ── 平台判定 ────────────────────────────────────────────────
OS_NAME="$(uname -s 2>/dev/null || echo unknown)"
case "$OS_NAME" in
  MINGW*|MSYS*|CYGWIN*|Windows_NT) IS_WINDOWS=1 ;;
  *) IS_WINDOWS=0 ;;
esac

# ── 算出生效值（只在继承值为空时才接管）────────────────────────
if [ "$IS_WINDOWS" = "1" ]; then
  UP="${USERPROFILE:-${HOME:-}}"
  UP="${UP:-$(cd ~ 2>/dev/null && pwd)}"
else
  UP="${HOME:-$(cd ~ 2>/dev/null && pwd)}"
fi

# 继承值为空字符串时视同未设置
nonempty() { [ -n "${1:-}" ]; }

if [ "$IS_WINDOWS" = "1" ]; then
  EF_PROGRAMFILES="${ProgramFiles:-C:\\Program Files}"
  EF_PROGRAMFILES_X86="${ProgramFilesX86:-C:\\Program Files (x86)}"
  EF_PROGRAMW6432="${ProgramW6432:-C:\\Program Files}"
  EF_LOCALAPPDATA="${LOCALAPPDATA:-${UP}\\AppData\\Local}"
  EF_APPDATA="${APPDATA:-${UP}\\AppData\\Roaming}"
else
  EF_PROGRAMFILES=""
  EF_PROGRAMFILES_X86=""
  EF_PROGRAMW6432=""
  EF_LOCALAPPDATA=""
  EF_APPDATA=""
fi

EF_DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${UP}}"
EF_NUGET_PACKAGES="${NUGET_PACKAGES:-${UP}/.nuget/packages}"

if [ "${1:-}" = "--print-env" ]; then
  if [ "$IS_WINDOWS" = "1" ]; then
    echo "平台           = ${OS_NAME}（Windows 模式）"
  else
    echo "平台           = ${OS_NAME}（POSIX 模式）"
  fi
  echo "HOME/UP        = ${UP}"
  echo "DOTNET_CLI_HOME= ${EF_DOTNET_CLI_HOME}"
  echo "NUGET_PACKAGES = ${EF_NUGET_PACKAGES}"
  if [ "$IS_WINDOWS" = "1" ]; then
    echo "ProgramFiles  = ${EF_PROGRAMFILES}"
    echo "ProgramFiles(x86)= ${EF_PROGRAMFILES_X86}"
    echo "ProgramW6432  = ${EF_PROGRAMW6432}"
    echo "LOCALAPPDATA  = ${EF_LOCALAPPDATA}"
    echo "APPDATA       = ${EF_APPDATA}"
  else
    echo "（Linux/macOS 不导出 Windows 专有变量）"
  fi
  exit 0
fi

# ── 找 dotnet ───────────────────────────────────────────────
DOTNET_BIN="${DOTNET_BIN:-}"
if [ -z "$DOTNET_BIN" ]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="dotnet"
  elif [ -x "/c/Program Files/dotnet/dotnet.exe" ]; then
    DOTNET_BIN="/c/Program Files/dotnet/dotnet.exe"
  elif [ -x "/usr/share/dotnet/dotnet" ]; then
    DOTNET_BIN="/usr/share/dotnet/dotnet"
  else
    echo "找不到 dotnet：请设置 DOTNET_BIN=<绝对路径>" >&2
    exit 1
  fi
fi

# ── 按平台组装 env ──────────────────────────────────────────
if [ "$IS_WINDOWS" = "1" ]; then
  exec env \
    "ProgramFiles(x86)=${EF_PROGRAMFILES_X86}" \
    "ProgramFiles=${EF_PROGRAMFILES}" \
    "ProgramW6432=${EF_PROGRAMW6432}" \
    "LOCALAPPDATA=${EF_LOCALAPPDATA}" \
    "APPDATA=${EF_APPDATA}" \
    "DOTNET_CLI_HOME=${EF_DOTNET_CLI_HOME}" \
    "NUGET_PACKAGES=${EF_NUGET_PACKAGES}" \
    "$DOTNET_BIN" "$@"
else
  exec env \
    "DOTNET_CLI_HOME=${EF_DOTNET_CLI_HOME}" \
    "NUGET_PACKAGES=${EF_NUGET_PACKAGES}" \
    "$DOTNET_BIN" "$@"
fi
