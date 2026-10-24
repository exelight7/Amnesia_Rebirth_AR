"""Offline integration tests: generated game-shaped files, never a real game installation.

Requires Windows, Python 3 and the Windows .NET Framework C# compiler. No downloads.
Run: python tests/run_tests.py
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


TESTS = Path(__file__).resolve().parent
REPO = TESTS.parent
COUNT = 5008
LANG = "config/lang_main/english.lang"
FONT = "fonts/arabic_72.fnt"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def put(root, relative, data):
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def snapshot(root, include_state=False):
    return {
        p.relative_to(root).as_posix(): digest(p.read_bytes())
        for p in root.rglob("*")
        if p.is_file()
        and (include_state or ".amnesia-arabic" not in p.relative_to(root).parts)
    }


def fixture(work, name, corrupt=False, unknown=False):
    root = work / name
    root.mkdir()
    (root / "maps").mkdir()
    original = b"FAKE DATA - NOT AN EXECUTABLE\x00\x11\x22\x33\x44 END"
    offset = original.index(b"\x11\x22\x33\x44")
    patched = original[:offset] + b"\xaa\xbb\xcc\xdd" + original[offset + 4:]
    put(root, "AmnesiaRebirth.exe", b"unknown fake executable" if unknown else original)
    put(root, "config/game.cfg", b'<Config Fonts="work_72.fnt architect_56.fnt"/>')
    put(root, "script/custom/modules/FontHandler.hps",
        b'tString GetFont(int alSize) { if(alSize > 0) { return "work_72.fnt"; } return "architect_56.fnt"; }\nvoid Other() {}')
    put(root, "config/default_user_settings.cfg", b'<Game />\n<Sound ShowSubtitles="false" Volume="0.8" />\n<Main />')
    put(root, "unrelated.dat", b"Must remain byte identical\r\n\x00\xff")
    # Existing font content must be restored; newly introduced assets must disappear.
    put(root, FONT, b"old font fixture")
    xml = ET.Element("LANGUAGE")
    category = ET.SubElement(xml, "CATEGORY", Name="Fixture")
    entries = []
    for i in range(COUNT):
        ET.SubElement(category, "Entry", Name="key_" + str(i)).text = "English " + str(i)
        entries.append(dict(id=i, file=LANG, category="Fixture", key="key_" + str(i),
                            visual="نص تجريبي " + str(i)))
    put(root, LANG, ET.tostring(xml, encoding="utf-8", xml_declaration=True))
    assets = {FONT: b"new fake font", "fonts/arabic_72_0.png": b"fake atlas bytes"}
    manifest = dict(version="1.0.0", entries=entries, binaries=[dict(
        name="AmnesiaRebirth.exe", before_sha256=digest(original), after_sha256=digest(patched),
        patches=[dict(offset=offset, before="11223344", after="aabbccdd")])],
        assets=[dict(path=k, sha256=digest(v)) for k, v in assets.items()])
    payload = work / (name + ".zip")
    with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False))
        for path, data in assets.items():
            archive.writestr("assets/" + path, data + b"tampered" if corrupt else data)
    return root, payload, patched


def main():
    if os.name != "nt":
        raise SystemExit("These tests require Windows .NET Framework; no packages are installed.")
    csc = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    if not csc.exists():
        csc = csc.parent.parent.parent / "Framework/v4.0.30319/csc.exe"
    if not csc.exists():
        raise SystemExit("Windows .NET Framework C# compiler not found.")
    # Only this new private directory and its descendants contain test mutations.
    base = REPO.parent / "test_work"
    base.mkdir(exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="installer-", dir=base))
    harness = work / "InstallerHarness.exe"
    subprocess.run([str(csc), "/nologo", "/target:exe", "/out:" + str(harness),
                    "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll",
                    "/r:System.Web.Extensions.dll", "/r:System.Xml.dll", "/r:System.Core.dll",
                    str(TESTS / "InstallerHarness.cs"), str(REPO / "installer/InstallerEngine.cs")],
                   check=True)

    def run(mode, root, payload, success=True):
        result = subprocess.run([str(harness), mode, str(root), str(payload)],
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
        assert (result.returncode == 0) == success, (mode, result.returncode, result.stderr)
        if not success:
            assert result.returncode == 2 and result.stderr.strip(), result

    def state(root):
        return json.loads((root / ".amnesia-arabic/state.json").read_text(encoding="utf-8"))

    passed = []
    root, payload, patched = fixture(work, "roundtrip")
    before = snapshot(root)
    run("install", root, payload)
    assert (root / "AmnesiaRebirth.exe").read_bytes() == patched
    nodes = ET.parse(root / LANG).findall("./CATEGORY/Entry")
    assert len(nodes) == COUNT
    assert all(n.text == "نص تجريبي " + str(i) for i, n in enumerate(nodes))
    assert b"arabic_72.fnt arabic_56.fnt" in (root / "config/game.cfg").read_bytes()
    assert b"arabic_" in (root / "script/custom/modules/FontHandler.hps").read_bytes()
    assert b"void Other() {}" in (root / "script/custom/modules/FontHandler.hps").read_bytes()
    settings = ET.fromstring("<Fixture>" + (root / "config/default_user_settings.cfg").read_text(encoding="utf-8") + "</Fixture>")
    assert settings.find("Sound").get("ShowSubtitles") == "true"
    assert settings.find("Sound").get("Volume") == "0.8"
    assert state(root)["status"] == "installed"
    installed = snapshot(root, True)
    run("install", root, payload)
    assert snapshot(root, True) == installed, "Second install changed files or backup state"
    run("uninstall", root, payload)
    assert snapshot(root) == before, "Uninstall did not restore exact original bytes/file set"
    assert state(root)["status"] == "removed"
    removed = snapshot(root, True)
    run("uninstall", root, payload)
    assert snapshot(root, True) == removed
    # Reinstall after removal must create a usable second set of backups.
    run("install", root, payload)
    run("uninstall", root, payload)
    assert snapshot(root) == before
    passed.append("5008 entries, binary/font/settings changes, idempotency, exact restore and reinstall")

    for name, options in [("unknown-exe", dict(unknown=True)), ("corrupt-asset", dict(corrupt=True))]:
        root, payload, _ = fixture(work, name, **options)
        before = snapshot(root, True)
        run("install", root, payload, False)
        assert snapshot(root, True) == before, name + " wrote files before rejecting input"
        passed.append(name + " rejected without writes")

    root, payload, _ = fixture(work, "tampered-installed")
    run("install", root, payload)
    put(root, LANG, b"user changed this after installation")
    before = snapshot(root, True)
    run("uninstall", root, payload, False)
    assert snapshot(root, True) == before, "Protected uninstall partially restored files"
    run("install", root, payload, False)
    assert snapshot(root, True) == before
    passed.append("tampered installed file blocks uninstall and reinstall without writes")

    root, payload, _ = fixture(work, "prepared-recovery")
    before = snapshot(root)
    run("install", root, payload)
    journal = state(root)
    journal["status"] = "prepared"
    # Simulate power loss mid-write: some files before, some after, new asset not yet created.
    for record in journal["records"][::2]:
        target = root / record["path"]
        if record["existed"]:
            target.write_bytes((root / ".amnesia-arabic" / record["backup"]).read_bytes())
        else:
            target.unlink()
    put(root, ".amnesia-arabic/state.json", json.dumps(journal).encode())
    interrupted = snapshot(root, True)
    run("install", root, payload, False)
    assert snapshot(root, True) == interrupted
    run("uninstall", root, payload)
    assert snapshot(root) == before
    passed.append("mixed before/after prepared journal recovers exact original bytes")

    root, payload, _ = fixture(work, "callback-failure")
    before = snapshot(root)
    run("interrupt", root, payload, False)
    assert snapshot(root) == before
    assert state(root)["status"] == "rolled_back"
    passed.append("failure after first target write rolls back exact original bytes")

    root, payload, _ = fixture(work, "damaged-backup")
    run("install", root, payload)
    backup = next(r for r in state(root)["records"] if r["existed"])
    put(root, ".amnesia-arabic/" + backup["backup"], b"damaged backup")
    before = snapshot(root, True)
    run("uninstall", root, payload, False)
    assert snapshot(root, True) == before
    passed.append("damaged backup rejected before any restore")

    skipped = []
    root, payload, _ = fixture(work, "linked-target")
    linked = root / LANG
    outside = work / "outside-lang.xml"
    outside.write_bytes(linked.read_bytes())
    linked.unlink()
    try:
        os.symlink(outside, linked)
    except OSError as error:
        skipped.append("Symlink test requires Windows link privileges: " + str(error.winerror))
    else:
        before = snapshot(root, True)
        outside_before = outside.read_bytes()
        run("install", root, payload, False)
        assert snapshot(root, True) == before
        assert outside.read_bytes() == outside_before
        passed.append("symlinked target rejected without writes inside or outside fixture")

    report = dict(passed=passed, skipped=skipped, game_launched=False, work_directory=str(work))
    (work / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    for item in passed:
        print("PASS: " + item)
    for item in skipped:
        print("SKIP: " + item)
    print("Fixtures and results retained at " + str(work))


if __name__ == "__main__":
    main()
