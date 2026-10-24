"""Check shared app version, bundled notes, and optional vX.Y.Z release tag."""
import os
import re
from pathlib import Path
from xml.etree import ElementTree

root = Path(__file__).resolve().parent.parent
version = ElementTree.parse(root / "Version.props").findtext("./PropertyGroup/Version")
if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version):
    raise SystemExit("Version.props must contain a major.minor.patch version")
notes = (root / "CHANGELOG.md").read_text(encoding="utf-8")
if not re.search(r"^## " + re.escape(version) + r"$", notes, re.M):
    raise SystemExit("CHANGELOG.md must include the current version")
ref = os.environ.get("GITHUB_REF", "")
if ref.startswith("refs/tags/") and ref != "refs/tags/v" + version:
    raise SystemExit("Release tag must match v" + version)
print("Release metadata verified: v" + version)
