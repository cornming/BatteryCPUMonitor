#!/usr/bin/env bash
# 產生 GitHub Release 的說明文字（Markdown）。
# 用法：release-notes.sh <版本號，不含 v>
#
# 內容取自「上一個版本標籤之後」的提交：標題依類型分組，
# 提交內文裡以 "- " 開頭的行會列為該項目的子項目。
set -euo pipefail

version=${1:?用法：release-notes.sh <版本號>}

last=$(git describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*' 2>/dev/null || true)
range=HEAD
[ -n "$last" ] && range="$last..HEAD"

features=""; fixes=""; others=""

while read -r hash; do
  [ -z "$hash" ] && continue
  subject=$(git log -1 --format=%s "$hash")
  short=$(git log -1 --format=%h "$hash")
  details=$(git log -1 --format=%b "$hash" | grep -E '^- ' | sed 's/^/  /' || true)

  kind=$(sed -nE 's/^([a-z]+)(\([^)]*\))?!?: .*/\1/p' <<<"$subject")
  title=$(sed -E 's/^[a-z]+(\([^)]*\))?!?: //' <<<"$subject")

  entry="- $title ($short)"
  [ -n "$details" ] && entry+=$'\n'"$details"
  entry+=$'\n'

  case "$kind" in
    feat) features+="$entry" ;;
    fix)  fixes+="$entry" ;;
    *)    others+="$entry" ;;
  esac
done < <(git log --no-merges --reverse --format=%H "$range")

cat <<NOTES
## 下載哪一個？

| 檔案 | 說明 |
| --- | --- |
| \`BatteryCPUMonitor-v$version-standalone.exe\` | 免安裝完整版，下載後直接執行，檔案較大 |
| \`BatteryCPUMonitor-v$version.exe\` | 精簡版，電腦需先安裝 [.NET 10 桌面執行階段](https://dotnet.microsoft.com/download/dotnet/10.0) |

兩個檔案功能相同，都不需要系統管理員權限。

## 更新內容
NOTES

[ -n "$features" ] && printf '\n### 新功能\n\n%s' "$features"
[ -n "$fixes" ]    && printf '\n### 修正\n\n%s' "$fixes"
[ -n "$others" ]   && printf '\n### 其他調整\n\n%s' "$others"

if [ -n "$last" ]; then
  printf '\n完整差異：https://github.com/%s/compare/%s...v%s\n' "${GITHUB_REPOSITORY:-cornming/BatteryCPUMonitor}" "$last" "$version"
fi
