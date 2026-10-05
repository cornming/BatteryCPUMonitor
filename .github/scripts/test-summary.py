#!/usr/bin/env python3
"""讀取 dotnet test 產生的 TRX 測試報告。

- 印出通過、失敗的數量，並把通過數量寫到 GITHUB_OUTPUT（passed=N）
- 每一項失敗的測試輸出成 GitHub Actions 的錯誤註記，在 Actions 頁面與 API 都看得到原因
- 有測試失敗，或一項測試都沒執行到，就以非零的結束碼結束

用法：test-summary.py <results.trx>
"""
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main() -> int:
    if len(sys.argv) != 2 or not os.path.exists(sys.argv[1]):
        print("::error::找不到測試報告，測試可能沒有執行")
        return 1

    root = ET.parse(sys.argv[1]).getroot()
    results = root.findall(".//t:UnitTestResult", NS)
    passed = sum(1 for r in results if r.get("outcome") == "Passed")
    failed = [r for r in results if r.get("outcome") == "Failed"]

    for result in failed:
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
        message = " ".join(message.split())[:400]
        print(f"::error title=測試失敗::{result.get('testName')}: {message}")

    print(f"通過 {passed} 項，失敗 {len(failed)} 項")

    output_path = os.environ.get("GITHUB_OUTPUT")
    if output_path:
        with open(output_path, "a", encoding="utf-8") as output:
            output.write(f"passed={passed}\n")

    if passed == 0 and not failed:
        print("::error::沒有執行到任何測試")
        return 1

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
