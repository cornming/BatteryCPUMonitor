#!/usr/bin/env python3
"""把 dotnet build 的編譯錯誤整理成 GitHub Actions 的錯誤註記。

這樣建置失敗時，不必打開完整記錄，在 Actions 頁面與 API 上就能直接看到是哪個檔案、哪一行、什麼錯誤。

用法：build-errors.py <建置輸出記錄檔>
"""
import re
import sys

# 例：C:\a\repo\File.cs(12,34): error CS0121: 訊息 [C:\a\repo\Project.csproj]
PATTERN = re.compile(r"^(?P<file>.+?)\((?P<line>\d+),(?P<col>\d+)\): error (?P<code>\w+): (?P<message>.*?)(?: \[.*\])?$")


def main() -> int:
    if len(sys.argv) != 2:
        return 0

    seen = set()
    with open(sys.argv[1], encoding="utf-8", errors="replace") as log:
        for raw in log:
            match = PATTERN.match(raw.strip())
            if not match:
                continue
            key = (match["file"], match["line"], match["code"])
            if key in seen or len(seen) >= 20:
                continue
            seen.add(key)
            message = match["message"].replace("%", "%25").replace("\r", "").replace("\n", " ")
            print(f"::error file={match['file']},line={match['line']},col={match['col']},title={match['code']}::{message}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
