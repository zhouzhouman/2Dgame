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
# 用法
#   bash tools/dotnet.sh build Juhuo/Juhuo.sln
#   bash tools/dotnet.sh test Juhuo/Tests/Core.Tests/Core.Tests.csproj
#   bash tools/dotnet.sh nuget list source
#
# 查看补了哪些变量
#   bash tools/dotnet.sh --print-env

set -euo pipefail

case "${1:-}" in
  --print-env)
    echo "ProgramFiles=${ProgramFiles:-C:\\Program Files}"
    echo "ProgramFiles(x86)=${ProgramFilesX86:-C:\\Program Files (x86)}"
    echo "ProgramW6432=${ProgramW6432:-C:\\Program Files}"
    echo "LOCALAPPDATA=${LOCALAPPDATA:-<空>}"
    echo "APPDATA=${APPDATA:-<空>}"
    echo "DOTNET_CLI_HOME=${DOTNET_CLI_HOME:-<空>}"
    echo "NUGET_PACKAGES=${NUGET_PACKAGES:-<空>}"
    exit 0
    ;;
esac

# 用户目录（Windows 下的默认值）
if [ -n "${USERPROFILE:-}" ]; then
  UP="$USERPROFILE"
else
  UP="/c/Users/qwert"
fi

PF="${ProgramFiles:-C:\\Program Files}"
PF86="${ProgramFilesX86:-C:\\Program Files (x86)}"
LAD="${LOCALAPPDATA:-${UP}\\AppData\\Local}"
ARD="${APPDATA:-${UP}\\AppData\\Roaming}"
DCH="${DOTNET_CLI_HOME:-${UP}}"
NPK="${NUGET_PACKAGES:-${UP}\\.nuget\\packages}"

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

exec env \
  "ProgramFiles(x86)=${PF86}" \
  "ProgramFiles=${PF}" \
  "ProgramW6432=${PF}" \
  "LOCALAPPDATA=${LAD}" \
  "APPDATA=${ARD}" \
  "DOTNET_CLI_HOME=${DCH}" \
  "NUGET_PACKAGES=${NPK}" \
  "$DOTNET_BIN" "$@"
