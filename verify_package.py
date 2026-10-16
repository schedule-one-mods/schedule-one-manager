"""Validate the locally prepared Thunderstore/manual-install archive."""
import argparse
import json
import re
import struct
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("package")
args = parser.parse_args()
with zipfile.ZipFile(args.package) as archive:
    files = {name for name in archive.namelist() if not name.endswith("/")}
    expected = {
        "manifest.json", "README.md", "icon.png", "CHANGELOG.md",
        "THIRD-PARTY-NOTICES.md", "compatibility.json", "Mods/LaunderingStatus.dll",
    }
    assert files == expected, ("Unexpected/missing package files", files ^ expected)
    assert archive.testzip() is None, "ZIP integrity check failed"
    manifest = json.loads(archive.read("manifest.json"))
    assert manifest["name"] == "Businesses"
    assert re.fullmatch(r"\d+\.\d+\.\d+", manifest["version_number"])
    assert len(manifest["description"]) <= 250
    assert manifest["dependencies"] == ["LavaGang-MelonLoader-0.7.3"]
    png = archive.read("icon.png")
    assert png[:8] == b"\x89PNG\r\n\x1a\n"
    assert struct.unpack(">II", png[16:24]) == (256, 256)
    assert archive.read("Mods/LaunderingStatus.dll")[:2] == b"MZ"
    compatibility = json.loads(archive.read("compatibility.json"))
    assert compatibility["scripting_backend"] == "IL2CPP"
    for name in ("README.md", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md"):
        archive.read(name).decode("utf-8")
    print(f"PASS: Businesses {manifest['version_number']}; ZIP integrity, metadata, icon and file allowlist")
    print("Tested game versions:", ", ".join(compatibility["game_versions_tested"]))
