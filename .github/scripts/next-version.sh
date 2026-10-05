#!/usr/bin/env bash
# 依「上一個版本標籤之後的提交訊息」算出下一個版本號（輸出不含 v 前綴）。
#
#   提交標題是 "類型!: ..." 或內文含 "BREAKING CHANGE" → 主版號 +1
#   提交標題是 "feat: ..."                             → 次版號 +1
#   其他（fix、refactor、ci ...）                      → 修訂號 +1
set -euo pipefail

last=$(git describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*' 2>/dev/null || true)
if [ -z "$last" ]; then
  echo "1.0.0"
  exit 0
fi

IFS=. read -r major minor patch <<<"${last#v}"
log=$(git log --format='%s%n%b' "$last..HEAD")

if grep -Eq '^[a-z]+(\([^)]*\))?!:|BREAKING CHANGE' <<<"$log"; then
  major=$((major + 1)); minor=0; patch=0
elif grep -Eq '^feat(\([^)]*\))?:' <<<"$log"; then
  minor=$((minor + 1)); patch=0
else
  patch=$((patch + 1))
fi

echo "$major.$minor.$patch"
