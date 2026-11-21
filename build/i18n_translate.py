#!/usr/bin/env python3
"""Generate the Simplified Chinese based language packs with a local Ollama model.

This script never calls an online translation service.  It talks to a local
Ollama instance (http://127.0.0.1:11434 by default), so the machine must have
the requested model pulled.

Pipeline:
  * zh_tw / zh_hk: the source text is first converted with OpenCC (s2twp /
    s2hk), then a local model revises the draft into natural regional
    Traditional Chinese.
  * en_us: a local model translates the source directly into English.

Every batch is validated (ids, placeholders, escapes, newlines, CJK leakage)
and split/retried on failure.  Results are cached per source string so an
interrupted run resumes where it stopped.

Usage:
  python build/i18n_translate.py --lang all
  python build/i18n_translate.py --lang en_us --model gemma4:12b
  python build/i18n_translate.py --lang zh_tw --limit 40
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

try:
    from opencc import OpenCC
except ImportError:  # pragma: no cover - dependency hint
    print("opencc is required for the Chinese packs: pip install opencc-python-reimplemented", file=sys.stderr)
    raise

ROOT = Path(__file__).resolve().parent.parent
LANG_DIR = ROOT / "Foxes Launcher" / "Resources" / "Languages"
DEFAULT_CACHE = Path(__file__).resolve().parent / ".i18n-cache"

OLLAMA_URL = "http://127.0.0.1:11434/api/chat"

CJK_RE = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]")
PLACEHOLDER_RE = re.compile(r"\{(\d+)(?:[,:][^{}]*)?\}")
VAR_RE = re.compile(r"\$\{[^}]+\}")

# Control characters used to carry formatting through the model untouched.
MARKERS = {
    "\n": "[[LF]]",
    "\\n": "[[BN]]",
    "\\h": "[[BH]]",
    "\\e": "[[BE]]",
    "\t": "[[TB]]",
}

SIMPLIFIED_ONLY = set("门这说时国学对开无关东车马鸟龙见贝页风飞网络软数据设载缓储认证为题实")


def encode(text: str) -> str:
    for raw, token in MARKERS.items():
        if raw == "\n":
            continue
        text = text.replace(raw, token)
    text = text.replace("\n", MARKERS["\n"])
    return text.replace("\\", "[[BS]]")


def decode(text: str) -> str:
    for raw, token in MARKERS.items():
        text = text.replace(token, raw)
    return text.replace("[[BS]]", "\\")


def signature(text: str) -> tuple:
    """Values that must be preserved exactly by a translation."""
    return (
        tuple(sorted(PLACEHOLDER_RE.findall(text))),
        tuple(sorted(VAR_RE.findall(text))),
        text.count("\n"),
        text.count("\\n"),
        text.count("\\h"),
        text.count("\\e"),
        text.count("\\"),
    )


def validate(source: str, translation: str, lang: str) -> list[str]:
    problems: list[str] = []
    if not translation or not translation.strip():
        return ["empty output"]
    if "�" in translation:
        problems.append("replacement character")
    if signature(source) != signature(translation):
        problems.append(f"placeholder/escape mismatch: {source!r} -> {translation!r}")
    if lang == "en_us":
        if CJK_RE.search(translation):
            problems.append(f"Chinese characters left in output: {translation!r}")
        if len(translation) > max(40, len(source) * 8):
            problems.append("output suspiciously long")
    else:
        if not CJK_RE.search(translation) and CJK_RE.search(source):
            problems.append("output lost Chinese text")
        if similarity(comparable(oc_convert(lang, source)), comparable(translation)) < 0.65 and len(source) >= 10:
            problems.append("output drifted too far from the source")
        leaked = SIMPLIFIED_ONLY.intersection(translation)
        if leaked and lang in {"zh_tw", "zh_hk"}:
            # A handful of shared characters (e.g. 台/干) are excluded above;
            # anything left is a likely Simplified leak.
            problems.append(f"Simplified characters in output: {''.join(sorted(leaked))}")
    return problems


EN_SYSTEM = """You are a professional software localizer. Translate Chinese UI strings from Foxes Launcher (FXL), a Minecraft launcher, into natural, concise English used by desktop launcher software.
The user sends one item per line as: id<TAB>Chinese text.
Return ONLY a valid JSON object mapping each id (as a string) to the English translation. No markdown, no commentary.
Rules:
- The tokens [[LF]], [[BN]], [[BH]], [[BE]], [[TB]] and [[BS]] stand for a line break, a literal \\n, a literal \\h, a literal \\e, a tab and a literal backslash. Copy them exactly as they are.
- Preserve every placeholder such as {0}, {1}, {2} exactly, in the original order.
- Preserve ${...} variables, URLs, file names, version numbers and paths exactly.
- Keep product and proper names unchanged: FXL, Minecraft, Java, Forge, NeoForge, Fabric, Quilt, OptiFine, LiteLoader, CurseForge, Modrinth, Microsoft, Xbox, Mojang, LittleSkin, EasyTier, Windows, BMCLAPI.
- Use sentence case (capitalize only the first word and proper nouns), never Title Case.
- Buttons and short labels stay short: 确定=OK, 取消=Cancel, 关闭=Close, 保存=Save, 删除=Delete, 重试=Retry.
- Never add, omit or explain information.
- Glossary: 版本=version; 实例=instance; 整合包=modpack; 模组/Mod=mod; 资源包=resource pack; 光影/光影包=shader pack; 数据包=data pack; 正版账号=premium account; 正版游戏=official game; 正版=official/premium; 离线=offline; 账户/账号=account; 登录=sign in; 设置=settings; 启动=launch; 下载=download; 安装=install; 存档=save; 联机=multiplayer; 皮肤=skin; 文件夹=folder; 目录=directory; 缓存=cache; 崩溃=crash; 日志=log; 提示=prompt; 加速器=booster."""

ZH_SYSTEM = {
    "zh_tw": """你是專業的軟體在地化譯者，負責把 Minecraft 啟動器 Foxes Launcher 的介面文字改寫成臺灣常用的繁體中文（臺灣正體）。
每行輸入：id<TAB>簡體原文<TAB>繁體草稿（機械轉換結果）。
請修訂草稿，使其自然、符合臺灣軟體用語，不要逐字轉換，也不要改變原意。
只輸出 JSON 物件（鍵為 id 字串，值為字串），不要輸出其他內容。
只調整字形、用詞與語序；不得刪減、增加、合併或解釋原文內容，資訊量與語氣必須保持一致。
[[LF]]、[[BN]]、[[BH]]、[[BE]]、[[TB]]、[[BS]] 分別代表換行、字面 \\n、字面 \\h、字面 \\e、定位字元、單個反斜線，必須原樣保留。
完整保留 {0}、{1} 佔位符與 ${...} 變數；保留 FXL、Minecraft、Java、Forge、Fabric、CurseForge、Modrinth、Microsoft、Xbox、Mojang 等專有名詞。
用語：啟動、設定、檔案、資料夾、資訊、資料、預設、儲存、載入、介面、網路、使用者、記憶體、快取、伺服器、程式、支援、權限、最佳化、整合包、模組、資源包、光影包、資料包、實例、離線、帳號、登入、主頁、連接埠、多人連線。""",
    "zh_hk": """你是專業的軟體在地化譯者，負責把 Minecraft 啟動器 Foxes Launcher 的介面文字改寫成香港特別行政區常用的繁體中文書面語。
每行輸入：id<TAB>簡體原文<TAB>繁體草稿（機械轉換結果）。
請修訂草稿，使其自然、符合香港軟件用語，不要逐字轉換，也不要改變原意。
只輸出 JSON 物件（鍵為 id 字串，值為字串），不要輸出其他內容。
只調整字形、用詞與語序；不得刪減、增加、合併或解釋原文內容，資訊量與語氣必須保持一致。
[[LF]]、[[BN]]、[[BH]]、[[BE]]、[[TB]]、[[BS]] 分別代表換行、字面 \\n、字面 \\h、字面 \\e、定位字元、單個反斜線，必須原樣保留。
完整保留 {0}、{1} 佔位符與 ${...} 變數；保留 FXL、Minecraft、Java、Forge、Fabric、CurseForge、Modrinth、Microsoft、Xbox、Mojang 等專有名詞。
用語：啟動、設定、檔案、資料夾、資訊、數據、預設、儲存、載入、介面、網絡、用戶、記憶體、快取、伺服器、程式、軟件、支援、權限、最佳化、整合包、模組、資源包、光影包、資料包、實例、離線、帳戶、登入、主頁、連結、搜尋、影片、滑鼠、列印、端口、多人連線。""",
}

OUTPUT_SCHEMA = {"type": "object", "additionalProperties": {"type": "string"}}


_CONVERTERS: dict[str, OpenCC] = {}


def oc_convert(lang: str, text: str) -> str:
    if lang not in _CONVERTERS:
        _CONVERTERS[lang] = OpenCC("s2twp" if lang == "zh_tw" else "s2hk")
    converted = _CONVERTERS[lang].convert(text)
    return normalize_draft(lang, converted)


# Deterministic fixes for the OpenCC draft.  They also serve as the fallback
# when the model output fails validation.
DRAFT_FIXES = {
    "zh_tw": [
        ("許可權", "權限"),
        ("全域性", "全域"),
        ("重灌", "重新安裝"),
        ("彙報", "回報"),
        ("免責宣告", "免責聲明"),
        ("賬號", "帳號"),
        ("賬戶", "帳戶"),
        ("後臺", "後台"),
        ("影片設定", "顯示設定"),
        ("視頻", "視訊"),
        ("質量", "品質"),
        ("其它", "其他"),
        ("數據", "資料"),
        ("聯機", "連線"),
        ("端口", "連接埠"),
        ("您", "你"),
    ],
    "zh_hk": [
        ("文件夾", "資料夾"),
        ("文件", "檔案"),
        ("程序", "程式"),
        ("信息", "資訊"),
        ("默認", "預設"),
        ("保存", "儲存"),
        ("加載", "載入"),
        ("界面", "介面"),
        ("鼠標", "滑鼠"),
        ("內存", "記憶體"),
        ("服務器", "伺服器"),
        ("硬盤", "硬碟"),
        ("光標", "游標"),
        ("屏幕", "螢幕"),
        ("設置", "設定"),
        ("鏈接", "連結"),
        ("質量", "質素"),
        ("視頻", "影片"),
        ("賬號", "帳號"),
        ("賬戶", "帳戶"),
        ("許可權", "權限"),
        ("全域性", "全域"),
        ("聯機", "連線"),
        ("您", "你"),
        ("賬户", "帳戶"),
        ("帳户", "帳戶"),
        ("啓", "啟"),
        ("爲", "為"),
        ("登錄", "登入"),
        ("窗口", "視窗"),
        ("配置檔案", "設定檔"),
        ("配置檔", "設定檔"),
    ],
}


def normalize_draft(lang: str, text: str) -> str:
    for old, new in DRAFT_FIXES.get(lang, []):
        text = text.replace(old, new)
    return text


# Short labels where the model tends to drop a distinguishing character.
KEY_OVERRIDES = {
    ("zh_tw", "端口号"): "連接埠號",
    ("zh_tw", "个性化"): "個人化",
    ("zh_hk", "端口号"): "端口號",
    ("zh_hk", "个性化"): "個人化",
    ("en_us", "个"): "this file",
    ("en_us", "些"): "these files",
    ("en_us", "是否要将这{0}文件作为 Mod 安装到 {1}？"): "Do you want to install {0} into {1} as a mod?",
    ("en_us", "百宝箱"): "Toolbox",
    ("en_us", "水槽包"): "Kitchen sink",
    ("en_us", "游戏本体"): "Base game",
    ("en_us", "游戏本体设置"): "Base game settings",
    ("en_us", "游戏本体个人信息"): "Base game personal info",
    ("en_us", "已绘制的地图"): "Filled maps",
    ("en_us", "结束处理"): "Finishing up",
    ("en_us", "文件拖拽"): "Drag and drop",
    ("en_us", "协议"): "Licenses",
    ("en_us", "取消安装"): "Cancel installation",
    ("en_us", "如果确实要删除，请在下面输入【Potato】这个单词以继续。"): 'If you really want to delete, please enter the word "Potato" below to continue.',
    ("en_us", "安装 OptiFine（方式 A）失败"): "Failed to install OptiFine (Method A)",
    ("en_us", "安装 OptiFine（方式 B）失败"): "Failed to install OptiFine (Method B)",
    ("en_us", "原版游戏"): "Vanilla game",
    ("en_us", "获取原版 json 文件下载地址"): "Get vanilla JSON file download address",
    ("en_us", "»  购买正版"): "» Buy Minecraft",
    ("en_us", "你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。"): "You haven't purchased Minecraft, or your Xbox Game Pass has expired.",
    ("en_us", "{0} 万"): "{0} × 10,000",
    ("en_us", "{0} 亿"): "{0} × 100,000,000",
    ("zh_tw", "协议"): "授權",
    ("zh_hk", "协议"): "授權",
    ("zh_tw", "结束处理"): "完成處理",
    ("zh_hk", "结束处理"): "完成處理",
    ("zh_tw", "是否要将这{0}文件作为 Mod 安装到 {1}？"): "是否要將這{0}檔案作為 Mod 安裝到 {1}？",
    ("zh_hk", "是否要将这{0}文件作为 Mod 安装到 {1}？"): "是否要將這{0}檔案作為 Mod 安裝到 {1}？",
    ("zh_tw", "启动进程"): "啟動處理程序",
    ("zh_tw", "尝试强制结束 Minecraft 进程"): "嘗試強制結束 Minecraft 處理程序",
    ("zh_tw", "已强制结束 Minecraft 进程"): "已強制結束 Minecraft 處理程序",
    ("zh_tw", "进程优先级"): "處理程序優先順序",
    ("zh_tw", "查看许可文档"): "查看授權文件",
    ("zh_hk", "启动进程"): "啟動處理程序",
    ("zh_hk", "尝试强制结束 Minecraft 进程"): "嘗試強制結束 Minecraft 處理程序",
    ("zh_hk", "已强制结束 Minecraft 进程"): "已強制結束 Minecraft 處理程序",
    ("zh_hk", "进程优先级"): "處理程序優先順序",
    ("zh_hk", "查看许可文档"): "查看授權檔案",
}

# Value-level fixes that must also apply inside longer sentences.
VALUE_FIXES = {
    "en_us": [
        ("Treasure Chest", "Toolbox"),
        ("Treasure chest", "Toolbox"),
    ],
}


def normalize_output(lang: str, text: str) -> str:
    for old, new in VALUE_FIXES.get(lang, []):
        text = text.replace(old, new)
    return text


def similarity(a: str, b: str) -> float:
    from difflib import SequenceMatcher

    return SequenceMatcher(None, a, b).ratio()


def comparable(text: str) -> str:
    for old, new in (("啓", "啟"), ("爲", "為"), ("裏", "裡"), ("衞", "衛"), ("着", "著")):
        text = text.replace(old, new)
    return text


def build_prompt(lang: str, items: list[tuple[int, str]]) -> tuple[str, str]:
    if lang == "en_us":
        lines = "\n".join(f"{i}\t{encode(src)}" for i, src in items)
        return EN_SYSTEM, "Translate every item:\n" + lines
    lines = "\n".join(f"{i}\t{encode(src)}\t{encode(oc_convert(lang, src))}" for i, src in items)
    return ZH_SYSTEM[lang], "修訂每一項：\n" + lines


class Ollama:
    def __init__(self, url: str, model: str, temperature: float, num_ctx: int):
        self.url = url
        self.model = model
        self.temperature = temperature
        self.num_ctx = num_ctx

    def chat(self, system: str, user: str, num_predict: int = 4096) -> str:
        body = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "stream": False,
            "format": OUTPUT_SCHEMA,
            "think": False,
            "keep_alive": "2h",
            "options": {
                "temperature": self.temperature,
                "num_ctx": self.num_ctx,
                "num_predict": num_predict,
            },
        }
        data = json.dumps(body).encode("utf-8")
        last_error: Exception | None = None
        for attempt in range(3):
            try:
                request = urllib.request.Request(
                    self.url, data=data, headers={"Content-Type": "application/json"}
                )
                with urllib.request.urlopen(request, timeout=1800) as response:
                    payload = json.loads(response.read().decode("utf-8"))
                content = payload.get("message", {}).get("content", "")
                if not content.strip():
                    raise RuntimeError(
                        f"empty model response (done_reason={payload.get('done_reason')})"
                    )
                return content
            except (urllib.error.URLError, TimeoutError, RuntimeError, json.JSONDecodeError) as ex:
                last_error = ex
                time.sleep(5 * (attempt + 1))
        raise RuntimeError(f"ollama request failed: {last_error}")


def request_batch(client: Ollama, lang: str, items: list[tuple[int, str]]) -> dict[int, str]:
    system, user = build_prompt(lang, items)
    raw = client.chat(system, user)
    data = json.loads(raw)
    if not isinstance(data, dict):
        raise ValueError("model did not return a JSON object")
    result: dict[int, str] = {}
    for i, src in items:
        value = data.get(str(i))
        if isinstance(value, str):
            value = decode(value.strip())
            if lang != "en_us":
                value = normalize_draft(lang, value)
            result[i] = normalize_output(lang, value)
    return result


def translate_batch(
    client: Ollama,
    lang: str,
    items: list[tuple[int, str]],
    done: dict[str, str],
    failures: list[tuple[str, str]],
) -> None:
    if not items:
        return
    try:
        result = request_batch(client, lang, items)
    except Exception as ex:  # noqa: BLE001 - the model is untrusted
        if len(items) == 1:
            failures.append((items[0][1], f"request failed: {ex}"))
            return
        middle = len(items) // 2
        print(f"    batch of {len(items)} failed ({ex}); splitting", flush=True)
        translate_batch(client, lang, items[:middle], done, failures)
        translate_batch(client, lang, items[middle:], done, failures)
        return

    retry: list[tuple[int, str]] = []
    for i, src in items:
        value = result.get(i)
        problems = validate(src, value or "", lang)
        if problems:
            retry.append((i, src))
        else:
            done[src] = value
    if retry:
        if len(items) == 1:
            src = items[0][1]
            value = result.get(items[0][0], "")
            if value and lang != "en_us":
                # The OpenCC draft is a safe, readable fallback for Chinese.
                draft = oc_convert(lang, src)
                done[src] = draft
                failures.append((src, f"used OpenCC draft: {'; '.join(validate(src, value, lang))}"))
            else:
                failures.append((src, f"invalid output: {'; '.join(validate(src, value or '', lang))}"))
            return
        print(f"    {len(retry)} item(s) invalid in batch of {len(items)}; retrying smaller", flush=True)
        middle = len(retry) // 2
        translate_batch(client, lang, retry[:middle], done, failures)
        translate_batch(client, lang, retry[middle:], done, failures)


def make_batches(keys: list[str], lang: str, max_items: int, max_chars: int) -> list[list[tuple[int, str]]]:
    batches: list[list[tuple[int, str]]] = []
    current: list[tuple[int, str]] = []
    current_chars = 0
    for index, key in enumerate(keys):
        cost = len(encode(key)) * (3 if lang != "en_us" else 1)
        if current and (len(current) >= max_items or current_chars + cost > max_chars):
            batches.append(current)
            current, current_chars = [], 0
        current.append((index, key))
        current_chars += cost
    if current:
        batches.append(current)
    return batches


def load_cache(path: Path, lang: str, base: dict[str, str]) -> dict[str, str]:
    cache: dict[str, str] = {}
    if path.is_file():
        try:
            cache = json.loads(path.read_text(encoding="utf-8"))
        except Exception as ex:  # noqa: BLE001
            print(f"warning: ignoring unreadable cache {path}: {ex}", file=sys.stderr)
            cache = {}
    valid: dict[str, str] = {}
    for src, value in cache.items():
        if src not in base or not isinstance(value, str):
            continue
        if not validate(src, value, lang):
            valid[src] = value
    return valid


def save_json(path: Path, data: dict) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    tmp.replace(path)


def write_language(lang: str, base: dict[str, str], done: dict[str, str]) -> Path:
    missing = [key for key in base if key not in done]
    if missing:
        print(f"error: {len(missing)} key(s) still missing for {lang}", file=sys.stderr)
        for key in missing[:5]:
            print(f"  {key!r}", file=sys.stderr)
        raise SystemExit(1)
    ordered = {}
    for key in base:
        value = KEY_OVERRIDES.get((lang, key), done[key])
        if lang != "en_us":
            value = normalize_draft(lang, value)
        ordered[key] = normalize_output(lang, value)
    path = LANG_DIR / f"{lang}.json"
    save_json(path, ordered)
    return path


def update_manifest(languages: list[dict]) -> None:
    path = LANG_DIR / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    existing = {item["code"]: item for item in manifest.get("languages", [])}
    for item in languages:
        existing[item["code"]] = item
    order = ["zh_cn", "zh_tw", "zh_hk", "en_us"]
    manifest["languages"] = sorted(
        existing.values(),
        key=lambda item: order.index(item["code"]) if item["code"] in order else len(order),
    )
    save_json(path, manifest)


LANGUAGE_META = {
    "zh_tw": {"code": "zh_tw", "name": "繁體中文（中國臺灣）", "authors": ["FoxesLauncher"], "version": "1.0.0"},
    "zh_hk": {"code": "zh_hk", "name": "繁體中文（香港特別行政區）", "authors": ["FoxesLauncher"], "version": "1.0.0"},
    "en_us": {"code": "en_us", "name": "English (US)", "authors": ["FoxesLauncher"], "version": "1.0.0"},
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lang", default="all", choices=["all", "zh_tw", "zh_hk", "en_us"])
    parser.add_argument("--model", default="gemma4:12b")
    parser.add_argument("--url", default=OLLAMA_URL)
    parser.add_argument("--cache", default=str(DEFAULT_CACHE))
    parser.add_argument("--temperature", type=float, default=0.15)
    parser.add_argument("--num-ctx", type=int, default=8192)
    parser.add_argument("--batch-items", type=int, default=None)
    parser.add_argument("--batch-chars", type=int, default=None)
    parser.add_argument("--limit", type=int, default=None, help="only translate the first N keys (debug)")
    parser.add_argument("--start", type=int, default=0, help="skip the first N keys (debug sampling)")
    args = parser.parse_args()

    base_path = LANG_DIR / "zh_cn.json"
    base: dict[str, str] = json.loads(base_path.read_text(encoding="utf-8-sig"))
    keys = list(base)[args.start :]
    if args.limit:
        keys = keys[: args.limit]

    languages = ["zh_tw", "zh_hk", "en_us"] if args.lang == "all" else [args.lang]
    cache_dir = Path(args.cache)
    cache_dir.mkdir(parents=True, exist_ok=True)
    client = Ollama(args.url, args.model, args.temperature, args.num_ctx)

    for lang in languages:
        cache_path = cache_dir / f"{lang}.json"
        done = load_cache(cache_path, lang, base)
        pending = [key for key in keys if key not in done]
        print(f"== {lang}: {len(done)} cached, {len(pending)} pending", flush=True)
        max_items = args.batch_items or (30 if lang == "en_us" else 20)
        max_chars = args.batch_chars or (1600 if lang == "en_us" else 1200)
        batches = make_batches(pending, lang, max_items, max_chars)
        failures: list[tuple[str, str]] = []
        start = time.time()
        for index, batch in enumerate(batches, 1):
            translate_batch(client, lang, batch, done, failures)
            save_json(cache_path, done)
            elapsed = time.time() - start
            rate = index / elapsed if elapsed else 0
            print(
                f"  batch {index}/{len(batches)}  done={len(done)}/{len(keys)}  "
                f"{elapsed / 60:.1f} min  ({rate * 60:.1f} batch/min)",
                flush=True,
            )
        if failures:
            report = cache_dir / f"{lang}.failures.txt"
            report.write_text(
                "\n\n".join(f"{src!r}\n  {reason}" for src, reason in failures), encoding="utf-8"
            )
            print(f"warning: {len(failures)} item(s) need review; see {report}", file=sys.stderr)
        if args.limit or args.start:
            print(f"debug limit active; not writing {lang}.json", flush=True)
            continue
        path = write_language(lang, base, done)
        print(f"wrote {path}", flush=True)
        update_manifest([LANGUAGE_META[lang]])
        print(f"updated manifest for {lang}", flush=True)

    return 0


if __name__ == "__main__":
    sys.exit(main())
