"""Build and verify a self-contained Windows distribution. Python 3.10+, Markdown==3.7."""
from pathlib import Path
import argparse
import hashlib
import html
import json
import shutil
import subprocess
import urllib.request
import zipfile
import xml.etree.ElementTree as ET
import markdown

ROOT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(ROOT / "Directory.Build.props").findtext(".//Version")
PAWN_URL = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
PAWN_SHA = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032"
STYLE='body{font:16px/1.8 "Segoe UI","Malgun Gothic",sans-serif;color:#203633;background:#f4f8f5;margin:auto;max-width:1000px;padding:40px}h1,h2,h3{line-height:1.4;color:#133e32}h2{margin-top:44px;border-bottom:1px solid #cadad2;padding-bottom:12px}table{border-collapse:collapse;width:100%;background:white}td,th{border:1px solid #cadad2;padding:10px;text-align:left}pre{background:#182c29;color:#e6f4ea;padding:18px;overflow:auto;border-radius:10px}code{font-family:Consolas,monospace}a{color:#137656}img{max-width:100%}blockquote{border-left:4px solid #4b9b75;padding-left:20px} @media print{body{padding:0;font-size:11pt}pre{white-space:pre-wrap}h2{break-after:avoid}table{break-inside:avoid}}'


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def render_docs():
    for name in ("USER_GUIDE.ko", "DEVELOPMENT.ko", "NOISE.ko"):
        source = ROOT / "docs" / f"{name}.md"
        content = markdown.markdown(source.read_text(encoding="utf-8"), extensions=["tables", "fenced_code", "toc"])
        # Keep local documentation navigation usable without a Markdown viewer.
        for target in ("USER_GUIDE.ko", "DEVELOPMENT.ko", "NOISE.ko", "VALIDATION"):
            content = content.replace(f'href="{target}.md"', f'href="{target}.html"')
        (ROOT / "docs" / f"{name}.html").write_text(
            f'<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{html.escape(name)} — Pocket Quiet</title><style>{STYLE}</style><body>{content}</body></html>', encoding="utf-8")
    validation = markdown.markdown((ROOT / "docs/VALIDATION.md").read_text(encoding="utf-8"), extensions=["tables", "fenced_code"])
    (ROOT / "docs/VALIDATION.html").write_text(f'<!doctype html><html lang="en"><meta charset="utf-8"><title>Validation — Pocket Quiet</title><style>{STYLE}</style><body>{validation}</body></html>', encoding="utf-8")


def verify(directory):
    manifest = json.loads((directory / "deployment-manifest.json").read_text(encoding="utf-8"))
    if manifest["Version"] != VERSION:
        raise SystemExit("Package version mismatch")
    for relative, digest in manifest["Files"].items():
        path = (directory / relative).resolve()
        if not path.is_relative_to(directory.resolve()) or not path.is_file() or sha256(path) != digest:
            raise SystemExit(f"Package hash mismatch: {relative}")
    print(f"Verified {len(manifest['Files'])} package files")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--render-docs", action="store_true")
    parser.add_argument("--verify", type=Path)
    options = parser.parse_args()
    if options.verify:
        verify(options.verify)
        return
    render_docs()
    if options.render_docs:
        return
    for part, assembly in (("app", "PocketQuiet"), ("service", "PocketQuiet.Service")):
        deps = ROOT / "artifacts" / part / f"{assembly}.deps.json"
        if not deps.exists():
            raise SystemExit("Run scripts/build.ps1 or scripts/build.sh from the source root first")
        if f"{assembly}/{VERSION}" not in json.loads(deps.read_text())["libraries"]:
            raise SystemExit(f"Stale {part} build; expected version {VERSION}")
    if sha256(ROOT / "artifacts/app/PocketQuiet.Core.dll") != sha256(ROOT / "artifacts/service/PocketQuiet.Core.dll"):
        raise SystemExit("App/service Core binaries differ. Rebuild both from the same source.")
    driver = ROOT / "third-party/PawnIO_setup.exe"
    driver.parent.mkdir(exist_ok=True)
    if not driver.exists():
        with urllib.request.urlopen(PAWN_URL, timeout=60) as response:
            driver.write_bytes(response.read())
    if sha256(driver) != PAWN_SHA:
        raise SystemExit("PawnIO SHA-256 mismatch")
    out = ROOT / "artifacts/package/Pocket Quiet"
    if out.exists():
        shutil.rmtree(out)
    shutil.copytree(ROOT / "artifacts/app", out, ignore=shutil.ignore_patterns("*.pdb"))
    shutil.copytree(ROOT / "artifacts/service", out / "service", ignore=shutil.ignore_patterns("*.pdb"))
    for folder in ("docs", "licenses", "scripts"):
        shutil.copytree(ROOT / folder, out / folder, ignore=shutil.ignore_patterns("__pycache__"))
    for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "CHANGELOG.md", "RELEASE_NOTES.md"):
        shutil.copy2(ROOT / name, out / name)
    (out / "third-party").mkdir()
    shutil.copy2(driver, out / "third-party/PawnIO_setup.exe")
    for name, title in (("USER_GUIDE.ko", "사용설명서"), ("DEVELOPMENT.ko", "개발명세서"), ("NOISE.ko", "소음줄이기")):
        document = (out / f"docs/{name}.html").read_text(encoding="utf-8")
        for target in ("USER_GUIDE.ko", "DEVELOPMENT.ko", "NOISE.ko", "VALIDATION"):
            document = document.replace(f'href="{target}.html"', f'href="docs/{target}.html"')
        (out / f"{title}.html").write_text(document, encoding="utf-8")
    for task in ("Install", "Recover", "Uninstall"):
        (out / f"{task}.cmd").write_bytes(('@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\Elevate.ps1" -Task ' + task + '\r\nif errorlevel 1 pause\r\n').encode("ascii"))
    source = out / "source"
    source.mkdir()
    for folder in ("src", "tests", "scripts", "docs", "licenses", ".github"):
        shutil.copytree(ROOT / folder, source / folder, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "*.user"))
    for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "CHANGELOG.md", "RELEASE_NOTES.md", "Directory.Build.props", "PocketQuiet.slnx", ".gitignore", "global.json"):
        shutil.copy2(ROOT / name, source / name)
    try:
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip())
    except (OSError, subprocess.CalledProcessError):
        commit, dirty = None, None
    manifest = {"Version": VERSION, "SourceCommit": commit, "SourceDirty": dirty,
                "Files": {str(path.relative_to(out)).replace("\\", "/"): sha256(path) for path in sorted(out.rglob("*")) if path.is_file()}}
    (out / "deployment-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    verify(out)
    zip_path = ROOT / "artifacts" / f"PocketQuiet-{VERSION}-win-x64.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(out.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(out.parent))
    digest = sha256(zip_path)
    (ROOT / "artifacts/SHA256SUMS.txt").write_text(digest + "  " + zip_path.name + "\n", encoding="ascii")
    print(zip_path)
    print("SHA256", digest)


if __name__ == "__main__":
    main()
