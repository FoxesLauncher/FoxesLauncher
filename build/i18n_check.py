#!/usr/bin/env python3
"""Validate the launcher language packs.

Checks performed:
  1. Every ``T("...")`` / ``ModLanguage.T("...")`` key in VB exists in zh_cn.json.
  2. Every ``{DynamicResource ...}`` key in XAML exists in zh_cn.json.
  3. ``{0}`` placeholders are contiguous and match the call's argument count.
  4. Keys do not contain the old ``.{8 hex}`` source-hash suffix.
  5. No empty values in the base language pack.
  6. No hard-coded Chinese in XAML display properties (allow-list below).
  7. Every extra language pack only contains known keys, has no empty
     values, keeps the source placeholders and newlines, and reaches the
     required coverage; every pack is registered in manifest.json.
  8. With ``--unused``, every key defined in zh_cn.json is referenced by
     the source (orphan keys are reported).

Exit code 1 on any problem, so it can be used in CI.
"""

from __future__ import annotations

import argparse
import html
import json
import os
import re
import sys
import tempfile
from pathlib import Path

CJK_RE = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]")
HASH_KEY_RE = re.compile(r"\.[0-9a-f]{8}$")
T_START_RE = re.compile(r"(?<![A-Za-z0-9_.])(?:ModLanguage\.)?T\(")
DR_RE = re.compile(r"\{DynamicResource\s+([^}]+?)\s*\}")
I18N_RE = re.compile(r"\{(?:local|my|fxl):I18n\s+(?:Text\s*=\s*)?'([^']*)'\s*\}")
SET_MENU_RE = re.compile(r'\bSetMenuHeader\([^,]+,\s*[^,]+,\s*"((?:[^"]|"")*)"\s*\)')
ATTR_RE = re.compile(r'(?P<name>[A-Za-z_][\w:.]*)\s*=\s*"(?P<value>[^"]*)"')

DISPLAY_ATTRS = {
    "Text", "Content", "ToolTip", "Title", "Header", "Description", "Info",
    "HintText", "Value", "TypeName", "TypeNameSpaced",
}

# External single-language resources are intentionally not translated.
SKIP_FILES = {
    "Resources/Custom.xaml",
    "Resources/Languages/zh_cn.json",
}


def read_text(path: Path) -> str:
    raw = path.read_bytes()
    return raw.decode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")


def strip_vb_comment(line: str) -> str:
    """Drop a trailing VB comment, respecting string literals."""
    in_string = False
    i = 0
    while i < len(line):
        ch = line[i]
        if in_string:
            if ch == '"':
                if i + 1 < len(line) and line[i + 1] == '"':
                    i += 1
                else:
                    in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "'":
                return line[:i]
        i += 1
    return line


def extract_t_call(text: str, start: int) -> tuple[str | None, int, str] | None:
    """Parse a T(...) call starting at *start*.

    Returns (key, arg_count, raw_first_arg) or None if the key is dynamic.
    """
    i = text.find("(", start)
    if i < 0:
        return None
    i += 1
    depth = 1
    in_string = False
    args: list[str] = []
    current: list[str] = []
    while i < len(text):
        ch = text[i]
        if in_string:
            current.append(ch)
            if ch == '"':
                if i + 1 < len(text) and text[i + 1] == '"':
                    current.append('"')
                    i += 2
                    continue
                in_string = False
            i += 1
            continue
        if ch == '"':
            in_string = True
            current.append(ch)
        elif ch == "(":
            depth += 1
            current.append(ch)
        elif ch == ")":
            depth -= 1
            if depth == 0:
                args.append("".join(current).strip())
                break
            current.append(ch)
        elif ch == "," and depth == 1:
            args.append("".join(current).strip())
            current = []
        else:
            current.append(ch)
        i += 1
    if not args:
        return None
    first = args[0]
    key_parts: list[str] = []
    # Walk a concatenation of literals and newline constants.
    pos = 0
    expect_operand = True
    ok = True
    while pos < len(first):
        while pos < len(first) and first[pos].isspace():
            pos += 1
        if pos >= len(first):
            break
        if not expect_operand:
            if first.startswith("&", pos):
                pos += 1
                expect_operand = True
                continue
            ok = False
            break
        if first[pos] == '"':
            pos += 1
            value: list[str] = []
            closed = False
            while pos < len(first):
                if first[pos] == '"':
                    if pos + 1 < len(first) and first[pos + 1] == '"':
                        value.append('"')
                        pos += 2
                        continue
                    pos += 1
                    closed = True
                    break
                value.append(first[pos])
                pos += 1
            if not closed:
                ok = False
                break
            key_parts.append("".join(value))
            expect_operand = False
            continue
        m = re.match(r"(?:vbCrLf|vbNewLine|Environment\.NewLine|ControlChars\.CrLf)\b", first[pos:])
        if m:
            key_parts.append("\n")
            pos += m.end()
            expect_operand = False
            continue
        ok = False
        break
    if not ok or expect_operand:
        return None
    key = "".join(key_parts)
    return key, len(args), first


def find_named_element_span(text: str, name_attr: str) -> tuple[int, int] | None:
    """Return the [start, end) span of the element carrying *name_attr*."""
    marker = text.find(name_attr)
    if marker < 0:
        return None
    start = text.rfind("<", 0, marker)
    if start < 0:
        return None
    tag_match = re.match(r"<\s*([A-Za-z_][\w:.-]*)", text[start:])
    if tag_match is None:
        return None
    tag = tag_match.group(1)
    pattern = re.compile(r"</?" + re.escape(tag) + r"(?=[\s>/])")
    depth = 0
    for match in pattern.finditer(text, start):
        close = text.find(">", match.end())
        if close < 0:
            break
        is_close = text[match.start() + 1] == "/"
        is_self_close = text[close - 1] == "/"
        if is_close:
            depth -= 1
            if depth == 0:
                return start, close + 1
        elif not is_self_close:
            depth += 1
    return None


def collect_used(root: Path) -> tuple[dict[str, list[tuple[int, str]]], list[str]]:
    used: dict[str, list[tuple[int, str]]] = {}
    problems: list[str] = []
    for path in root.rglob("*"):
        if path.suffix.lower() not in {".vb", ".xaml"}:
            continue
        if any(part.lower() in {"bin", "obj"} for part in path.parts):
            continue
        rel = path.relative_to(root).as_posix()
        if rel in SKIP_FILES:
            continue
        text = read_text(path)
        if path.suffix.lower() == ".xaml":
            visible = re.sub(r"<!--.*?-->", "", text, flags=re.S)
            preset_span = find_named_element_span(visible, 'x:Name="PanCustomPreset"')
            for match in DR_RE.finditer(visible):
                key = match.group(1).strip()
                if not key:
                    continue
                # Only text-bearing attributes; brushes and other resources are
                # not language keys.
                line_start = visible.rfind("<", 0, match.start())
                line_end = visible.find(">", match.start())
                tag_text = visible[line_start:line_end] if line_start >= 0 and line_end >= 0 else ""
                tag_name_match = re.match(r"<\s*([A-Za-z_][\w:.]*)", tag_text)
                if tag_name_match is None or tag_name_match.group(1).split(":")[-1] == "Setter":
                    continue
                prefix = visible[line_start:match.start()]
                attr_match = re.search(r'([A-Za-z_][\w:.]*)\s*=\s*"?\s*$', prefix)
                if attr_match is None or attr_match.group(1).split(":")[-1] not in DISPLAY_ATTRS:
                    continue
                used.setdefault(key, []).append((1, rel))
            for match in I18N_RE.finditer(visible):
                key = html.unescape(match.group(1))
                if key:
                    used.setdefault(key, []).append((1, rel))
            # Hard-coded UI text check.
            for match in ATTR_RE.finditer(visible):
                name = match.group("name")
                value = match.group("value")
                if name not in DISPLAY_ATTRS or not CJK_RE.search(value):
                    continue
                if value.lstrip().startswith("{"):
                    continue
                line = visible.count("\n", 0, match.start()) + 1
                if preset_span is not None and preset_span[0] <= match.start() < preset_span[1]:
                    continue  # preset list: external single-language resource
                problems.append(f"hard-coded Chinese UI: {rel}:{line} {name}={value.strip()[:60]!r}")
        else:
            cleaned = "\n".join(strip_vb_comment(line) for line in text.splitlines())
            for pattern, description in [
                (r"Contains\(T\(", "translated text used in Contains"),
                (r"EndsWithF\(T\(", "translated text used in EndsWithF"),
                (r"RegexSeek\(T\(", "translated text used in RegexSeek"),
                (r"Telemetry\(T\(", "translated telemetry category"),
                (r"Pair\.Key\s*=\s*T\(", "translated value used as dictionary key"),
            ]:
                for m in re.finditer(pattern, cleaned):
                    line = cleaned.count("\n", 0, m.start()) + 1
                    problems.append(f"{description}: {rel}:{line}")
            for match in T_START_RE.finditer(cleaned):
                parsed = extract_t_call(cleaned, match.start())
                if parsed is None:
                    continue
                key, argc, raw = parsed
                if not key:
                    continue
                used.setdefault(key, []).append((argc, rel))
            # Keys passed through a small wrapper are invisible to the T()
            # scanner above, so collect the literal argument explicitly.
            for match in SET_MENU_RE.finditer(cleaned):
                key = match.group(1).replace('""', '"')
                if key:
                    used.setdefault(key, []).append((1, rel))
    return used, problems


def normalize_key(key: str) -> str:
    return key.replace("\r\n", "\n").replace("\r", "\n")


def find_potential_fragments(root: Path) -> list[str]:
    """Report concatenations where a T() literal is not a complete sentence."""
    t_literal = re.compile(r'(?<![A-Za-z0-9_.])(?:ModLanguage\.)?T\("((?:[^"]|"")*)"')
    concat = re.compile(r'T\("[^"]*"\)\s*&|&\s*T\("')
    rows: list[str] = []
    for path in root.rglob("*.vb"):
        if any(part.lower() in {"bin", "obj"} for part in path.parts):
            continue
        text = read_text(path)
        for line_no, line in enumerate(text.splitlines(), 1):
            if not concat.search(line):
                continue
            keys = [m.group(1).replace('""', '"') for m in t_literal.finditer(line)]
            if any(key and not re.search(r"[。！？：…\n]$", key) for key in keys):
                rows.append(f"{path.relative_to(root).as_posix()}:{line_no}")
    return rows


def load_language_packs(root: Path, base: dict[str, str], max_missing: int = 0) -> tuple[list[str], list[str]]:
    """Validate every non-base language pack against the base pack."""
    lang_dir = root / "Resources" / "Languages"
    reports: list[str] = []
    problems: list[str] = []
    packs: dict[str, dict] = {}
    for path in sorted(lang_dir.glob("*.json")):
        if path.name in {"zh_cn.json", "manifest.json"}:
            continue
        try:
            data = json.loads(read_text(path))
        except Exception as ex:  # noqa: BLE001
            problems.append(f"unreadable language pack {path.name}: {ex}")
            continue
        if not isinstance(data, dict):
            problems.append(f"language pack {path.name} is not a JSON object")
            continue
        packs[path.stem.lower()] = data
        missing = [key for key in base if key not in data]
        unknown = [key for key in data if key not in base]
        coverage = (len(base) - len(missing)) / len(base) if base else 1.0
        reports.append(f"{path.stem}: {len(base) - len(missing)}/{len(base)} keys ({coverage:.1%})")
        if len(missing) > max_missing:
            problems.append(
                f"{path.name} is missing {len(missing)} key(s) (limit {max_missing}); "
                f"first: {missing[0]!r}"
            )
        for key in unknown[:20]:
            problems.append(f"unknown key in {path.name}: {key!r}")
        if len(unknown) > 20:
            problems.append(f"... and {len(unknown) - 20} more unknown key(s) in {path.name}")
        for key, value in data.items():
            if not isinstance(value, str) or not value.strip():
                problems.append(f"empty value in {path.name}: {key!r}")
                continue
            if key not in base:
                continue
            expected = sorted(re.findall(r"\{(\d+)(?:[,:][^{}]*)?\}", base[key]))
            actual = sorted(re.findall(r"\{(\d+)(?:[,:][^{}]*)?\}", value))
            if expected != actual:
                problems.append(
                    f"placeholder mismatch in {path.name}: {key!r} has {actual}, expected {expected}"
                )
            if base[key].count("\n") != value.count("\n"):
                problems.append(f"newline mismatch in {path.name}: {key!r}")

    manifest_path = lang_dir / "manifest.json"
    try:
        manifest = json.loads(read_text(manifest_path))
        registered = {str(item.get("code", "")).lower() for item in manifest.get("languages", [])}
    except Exception as ex:  # noqa: BLE001
        problems.append(f"unreadable manifest.json: {ex}")
        registered = set()
    for code in packs:
        if code not in registered:
            problems.append(f"language pack {code} is not registered in manifest.json")
    for code in sorted(registered):
        if code and code != "zh_cn" and code not in packs:
            problems.append(f"manifest.json registers {code!r} but {code}.json is missing")

    return reports, problems


def validate(root: Path) -> tuple[list[str], dict[str, list[tuple[int, str]]], dict[str, str]]:
    lang_path = root / "Resources" / "Languages" / "zh_cn.json"
    raw_language = json.loads(read_text(lang_path))
    language = {normalize_key(key): value for key, value in raw_language.items()}

    problems: list[str] = []
    used, scan_problems = collect_used(root)
    problems.extend(scan_problems)

    for key, sites in sorted(used.items()):
        if key not in language:
            problems.append(f"missing key: {key!r} (used in {sites[0][1]})")
            continue
        value = language[key]
        placeholders = sorted({int(n) for n in re.findall(r"\{(\d+)(?:[,:][^{}]*)?\}", value)})
        if placeholders and placeholders != list(range(len(placeholders))):
            problems.append(f"non-contiguous placeholders: {key!r} -> {placeholders}")
        for argc, rel in sites:
            if placeholders and placeholders[-1] + 1 != argc - 1:
                problems.append(f"placeholder/argument mismatch: {key!r} has {len(placeholders)} placeholder(s), call in {rel} has {argc - 1} argument(s)")

    for key, value in language.items():
        if not isinstance(value, str) or value == "":
            problems.append(f"empty value: {key!r}")
        if HASH_KEY_RE.search(key):
            problems.append(f"legacy hash key: {key!r}")
        if key.startswith("UI."):
            problems.append(f"legacy file/property key: {key!r}")
        if key.startswith("$"):
            problems.append(f"legacy brief-prefix key: {key!r}")
        if key.startswith(("{0}Help", "title ", "echo ")) or '"},' in key or (
            key.startswith("{0}") and any(part in key for part in ("\\", ".exe", ".json", ".xaml", ".bat"))
        ):
            problems.append(f"data template used as language key: {key!r}")
        # Leading/trailing whitespace is the fingerprint of a sentence split.
        if key != key.strip():
            problems.append(f"edge-whitespace key: {key!r}")

    return problems, used, language


def find_unused(used: dict[str, list[tuple[int, str]]], language: dict[str, str]) -> list[str]:
    """Return keys defined in the base pack but never referenced by the source."""
    return sorted(key for key in language if key not in used)


def self_test() -> int:
    """A deliberately broken fixture must fail, or the checker is empty."""
    import tempfile

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        lang_dir = root / "Resources" / "Languages"
        lang_dir.mkdir(parents=True)
        (lang_dir / "zh_cn.json").write_text('{"确定": "确定", " 半句": " 半句"}', encoding="utf-8")
        (lang_dir / "zh_tw.json").write_text('{"确定": "確定 {0}", "多余": "多餘"}', encoding="utf-8")
        (lang_dir / "manifest.json").write_text(
            '{"default": "zh_cn", "languages": [{"code": "zh_tw", "name": "繁體中文（中國臺灣）"}, '
            '{"code": "en_us", "name": "English (US)"}]}',
            encoding="utf-8",
        )
        (root / "Bad.xaml").write_text(
            '<TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" '
            'xmlns:local="clr-namespace:FXL" '
            'Text="{DynamicResource 不存在的XAML键}" '
            'ToolTip="{local:I18n \'不存在的I18n键\'}" />',
            encoding="utf-8",
        )
        (root / "Bad.vb").write_text(
            'Module M\nSub X()\nHint(T("不存在的VB键"))\nEnd Sub\nEnd Module\n',
            encoding="utf-8",
        )
        problems, used, language = validate(root)
        missing = {key for key in used if any(key in p and p.startswith("missing key:") for p in problems)}
        expected = {"不存在的XAML键", "不存在的I18n键", "不存在的VB键"}
        if not expected.issubset(missing):
            print("self-test failed: checker did not see all intentionally broken keys")
            print("used:", used)
            print("problems:", problems)
            return 1
        if not any(p.startswith("edge-whitespace key:") for p in problems):
            print("self-test failed: edge-whitespace fragment was not detected")
            return 1
        unused = find_unused(used, language)
        if "确定" not in unused:
            print("self-test failed: unused key was not detected")
            print("unused:", unused)
            return 1
        reports, pack_problems = load_language_packs(root, language)
        if not any("placeholder mismatch" in p for p in pack_problems):
            print("self-test failed: pack placeholder mismatch was not detected")
            print("reports:", reports)
            print("pack problems:", pack_problems)
            return 1
        if not any("unknown key" in p for p in pack_problems):
            print("self-test failed: pack unknown key was not detected")
            return 1
        if not any("missing 1 key" in p for p in pack_problems):
            print("self-test failed: pack missing key was not detected")
            return 1
        if not any("en_us.json is missing" in p for p in pack_problems):
            print("self-test failed: manifest entry without pack was not detected")
            return 1
        print(
            "self-test OK: XAML, I18n, VB missing keys, edge-whitespace fragments, "
            "unused keys and language pack coverage/placeholders are detected"
        )
        return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=str(Path(__file__).resolve().parent.parent / "Foxes Launcher"))
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--report-fragments", action="store_true")
    parser.add_argument("--max-fragments", type=int, default=None)
    parser.add_argument("--max-missing", type=int, default=0, help="allowed missing keys per language pack")
    parser.add_argument("--unused", action="store_true", help="report keys defined but not used by the source")
    parser.add_argument("--max-unused", type=int, default=0, help="allowed unused keys")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    root = Path(args.root).resolve()
    if args.report_fragments:
        rows = find_potential_fragments(root)
        print(f"{len(rows)} potential fragment line(s):")
        for row in rows:
            print("  " + row)
        if args.max_fragments is not None and len(rows) > args.max_fragments:
            print(f"fragment baseline exceeded: {len(rows)} > {args.max_fragments}")
            return 1
        return 0

    if args.unused:
        _, used, language = validate(root)
        unused = find_unused(used, language)
        if unused:
            print(f"{len(unused)} unused key(s):")
            for key in unused:
                print("  " + repr(key))
        if len(unused) > args.max_unused:
            print(f"unused baseline exceeded: {len(unused)} > {args.max_unused}")
            return 1
        print(f"OK: {len(unused)} unused key(s) (limit {args.max_unused})")
        return 0

    problems, used, language = validate(root)
    reports, pack_problems = load_language_packs(root, language, args.max_missing)
    problems.extend(pack_problems)

    if reports:
        print("Language packs:")
        for report in reports:
            print("  " + report)

    if problems:
        print(f"{len(problems)} problem(s):")
        for problem in problems:
            print("  " + problem)
        return 1
    print(f"OK: {len(used)} keys used, {len(language)} keys defined")
    return 0


if __name__ == "__main__":
    sys.exit(main())
