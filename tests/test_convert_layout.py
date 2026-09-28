"""Tests for skill/hebrew-keyboard-layout-fix/scripts/convert_layout.py (same cases as test-converter.ps1)."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "skill" / "hebrew-keyboard-layout-fix" / "scripts"))
from convert_layout import convert  # noqa: E402

CASES = [
    ("tbh rumv ahvhv rauo cgcrh", "אני רוצה שיהיה רשום בעברי"),
    ("אני רוצה שיהיה רשום בעברי", "tbh rumv ahvhv rauo cgcrh"),
    ("akuo", "שלום"),
    ("שלום", "akuo"),
    ("Akuo", "שלום"),
    ("יקךךם", "hello"),
    ("hello", "יקךךם"),
    ("dhfh,", "גיכית"),
    ("ן", "i"),
    ("123 !?", "123 !?"),
    ("ין 2026", "hi 2026"),
    ("akuo עולם", "שלום guko"),
    ("שלום hello", "akuo יקךךם"),
    ("tbh rumv to go to עברית", "אני רוצה אם עם אם gcrh,"),
    ("akuo, guko.", "שלוםת עולםץ"),
    ("akuo ,", "שלום ת"),
    ("ab שמ", "שנ an"),
    ("akuo\nguko", "שלום\nעולם"),
]


def main():
    failed = 0
    for src, expected in CASES:
        got = convert(src)
        if got != expected:
            failed += 1
            print(f"FAIL {src!r} -> {got!r} (expected {expected!r})")
    forced = convert("Tbh rumv", "he")
    if forced != "אני רוצה":
        failed += 1
        print(f"FAIL forced he -> {forced!r}")
    print(f"{len(CASES) + 1 - failed}/{len(CASES) + 1} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
