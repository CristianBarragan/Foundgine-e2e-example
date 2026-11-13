#!/usr/bin/env python3
"""Fail if any test was skipped or no results exist. Understands .NET .trx and Maven surefire/failsafe XML.
DB-backed tests skip themselves when their connection variable is unset; in CI that must be a failure, not a silent pass.
Usage: assert-no-skipped.py DIR [DIR...]   (searches recursively)"""
import pathlib, sys
import xml.etree.ElementTree as ET

def local(tag): return tag.rsplit('}', 1)[-1]

def scan(path):
    """Returns (executed_count, [skipped test names])."""
    total, skipped = 0, []
    root = ET.parse(path).getroot()
    if path.suffix == '.trx':
        for el in root.iter():
            if local(el.tag) == 'UnitTestResult':
                total += 1
                if el.get('outcome') in ('NotExecuted', 'Skipped'): skipped.append(el.get('testName'))
    else:  # surefire: <testcase> with a <skipped/> child
        for tc in root.iter('testcase'):
            total += 1
            if tc.find('skipped') is not None: skipped.append(f"{tc.get('classname')}.{tc.get('name')}")
    return total, skipped

def main(dirs):
    files = [f for d in dirs for f in pathlib.Path(d).rglob('*') if f.suffix == '.trx' or (f.suffix == '.xml' and f.name.startswith('TEST-'))]
    if not files:
        print(f"no test result files under {dirs}"); return 1
    grand, bad = 0, []
    for f in files:
        n, s = scan(f); grand += n; bad += s
    print(f"{grand} tests in {len(files)} result files; {len(bad)} skipped")
    for name in bad: print("  SKIPPED:", name)
    return 1 if bad or grand == 0 else 0

if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]) if len(sys.argv) > 1 else 2)
