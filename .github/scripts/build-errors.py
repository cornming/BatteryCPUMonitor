#!/usr/bin/env python3
"""把 dotnet build 的錯誤整理成 GitHub Actions 的錯誤註記。

這樣建置失敗時，不必打開完整記錄，在 Actions 頁面與 API 上就能直接看到是哪個檔案、哪一行、什麼錯誤。
- 標準的編譯錯誤（檔案(行,欄): error 代碼: 訊息）會標在對應的檔案與行數上
- 其他格式的錯誤（專案設定、還原套件等）原樣列出
- 最後附上記錄的最後幾行，方便看出失敗前在做什麼

用法：build-errors.py <建置輸出記錄檔>
"""
import re
import sys

COMPILER = re.compile(r"^(?P<file>.+?)\((?P<line>\d+),(?P<col>\d+)\): error (?P<code>\w+): (?P<message>.*?)(?: \[.*\])?$")
ANY_ERROR = re.compile(r"\berror\b\s*[A-Za-z]*\d*\s*:", re.IGNORECASE)
LIMIT = 20
TAIL_LINES = 12


def escape(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "").replace("\n", " ")


def main() -> int:
    if len(sys.argv) != 2:
        return 0

    with open(sys.argv[1], encoding="utf-8", errors="replace") as log:
        lines = [line.rstrip("\r\n") for line in log]

    seen = set()
    for raw in lines:
        text = raw.strip()
        if len(seen) >= LIMIT:
            break

        match = COMPILER.match(text)
        if match:
            key = (match["file"], match["line"], match["code"])
            if key not in seen:
                seen.add(key)
                print(f"::error file={match['file']},line={match['line']},col={match['col']},title={match['code']}::{escape(match['message'])}")
        elif ANY_ERROR.search(text) and text not in seen:
            seen.add(text)
            print(f"::error title=建置錯誤::{escape(text[:600])}")

    tail = " ⏎ ".join(line.strip() for line in lines[-TAIL_LINES:] if line.strip())
    print(f"::warning title=建置記錄的最後幾行::{escape(tail[:1500])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
