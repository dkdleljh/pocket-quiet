"""Assemble Windows distribution. Requires Python 3.10+ and Markdown==3.7."""
from pathlib import Path
import hashlib, html, shutil, urllib.request, zipfile
import markdown
ROOT=Path(__file__).resolve().parents[1]
VERSION='0.1.0'
PAWN_URL='https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
PAWN_SHA='1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'
STYLE='body{font:16px/1.8 "Segoe UI","Malgun Gothic",sans-serif;color:#203633;background:#f4f8f5;margin:auto;max-width:1000px;padding:40px}h1,h2,h3{line-height:1.4;color:#133e32}h2{margin-top:44px;border-bottom:1px solid #cadad2;padding-bottom:12px}table{border-collapse:collapse;width:100%;background:white}td,th{border:1px solid #cadad2;padding:10px;text-align:left}pre{background:#182c29;color:#e6f4ea;padding:18px;overflow:auto;border-radius:10px}code{font-family:Consolas,monospace}a{color:#137656}img{max-width:100%}blockquote{border-left:4px solid #4b9b75;padding-left:20px} @media print{body{padding:0;font-size:11pt}pre{white-space:pre-wrap}h2{break-after:avoid}table{break-inside:avoid}}'
def render_docs():
 for name in ('USER_GUIDE.ko','DEVELOPMENT.ko'):
  source=ROOT/'docs'/f'{name}.md'
  content=markdown.markdown(source.read_text(encoding='utf-8'),extensions=['tables','fenced_code','toc'])
  (ROOT/'docs'/f'{name}.html').write_text(f'<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{html.escape(name)} — Pocket Quiet</title><style>{STYLE}</style><body>{content}</body></html>',encoding='utf-8')
def main():
 render_docs()
 for part in ('app','service'):
  if not (ROOT/'artifacts'/part).is_dir():raise SystemExit('Run scripts/build.ps1 or scripts/build.sh first')
 driver=ROOT/'third-party/PawnIO_setup.exe';driver.parent.mkdir(exist_ok=True)
 if not driver.exists():driver.write_bytes(urllib.request.urlopen(PAWN_URL).read())
 if hashlib.sha256(driver.read_bytes()).hexdigest()!=PAWN_SHA:raise SystemExit('PawnIO SHA-256 mismatch')
 out=ROOT/'artifacts/package/Pocket Quiet'
 if out.exists():shutil.rmtree(out)
 shutil.copytree(ROOT/'artifacts/app',out,ignore=shutil.ignore_patterns('*.pdb'))
 shutil.copytree(ROOT/'artifacts/service',out/'service',ignore=shutil.ignore_patterns('*.pdb'))
 for folder in ('docs','licenses','scripts'):shutil.copytree(ROOT/folder,out/folder)
 for name in ('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md'):shutil.copy2(ROOT/name,out/name)
 (out/'third-party').mkdir();shutil.copy2(driver,out/'third-party/PawnIO_setup.exe')
 shutil.copy2(out/'docs/USER_GUIDE.ko.html',out/'사용설명서.html');shutil.copy2(out/'docs/DEVELOPMENT.ko.html',out/'개발명세서.html')
 for task in ('Install','Recover','Uninstall'):
  (out/f'{task}.cmd').write_text('@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\Elevate.ps1" -Task '+task+'\r\nif errorlevel 1 pause\r\n',encoding='ascii')
 source=out/'source';source.mkdir()
 for folder in ('src','tests','scripts','docs','licenses','.github'):
  shutil.copytree(ROOT/folder,source/folder,ignore=shutil.ignore_patterns('bin','obj','__pycache__','*.user'))
 for name in ('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md','Directory.Build.props','PocketQuiet.slnx','.gitignore'):
  shutil.copy2(ROOT/name,source/name)
 zip_path=ROOT/'artifacts'/f'PocketQuiet-{VERSION}-win-x64.zip'
 with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
  for f in sorted(out.rglob('*')):
   if f.is_file():archive.write(f,f.relative_to(out.parent))
 digest=hashlib.sha256(zip_path.read_bytes()).hexdigest()
 (ROOT/'artifacts/SHA256SUMS.txt').write_text(digest+'  '+zip_path.name+'\n',encoding='ascii')
 print(zip_path);print('SHA256',digest)
if __name__=='__main__':main()
