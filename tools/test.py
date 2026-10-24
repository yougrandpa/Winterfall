#!/usr/bin/env python3
"""Run the real NUnit code tests without starting or connecting to Unity/Tuanjie."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "my-world"
GAME = PROJECT / "Assets/Game"
CACHE = PROJECT / "Library/CodeTests"
REPORTS = PROJECT / "Logs/TestResults"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def editor_contents(value):
    path = Path(value).expanduser()
    for candidate in (path, path / "Contents", path.parent.parent):
        if (candidate / "MonoBleedingEdge/bin/mono").is_file():
            return candidate
    raise RuntimeError("Cannot find the bundled Mono runtime at " + str(path))


def runtime(value):
    if value:
        contents = editor_contents(value)
    else:
        # Read the pinned version; using its runtime does not launch the editor.
        line = next(line for line in (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()
                    if line.startswith("m_EditorVersion:"))
        version = line.split(":", 1)[1].strip()
        candidates = (
            Path("/Applications/Tuanjie/Hub/Editor") / version / "Tuanjie.app/Contents",
            Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents",
        )
        contents = next((p for p in candidates if (p / "MonoBleedingEdge/bin/mono").is_file()), None)
        if contents is None:
            raise RuntimeError("Pass --editor /path/to/Tuanjie.app or set TUANJIE_EDITOR. No editor will be launched.")
    base = contents / "MonoBleedingEdge"
    return base / "bin/mono", base / "lib/mono/4.5/csc.exe", base / "lib/mono/4.7.1-api"


def dependency(pattern, relative):
    paths = sorted((PROJECT / "Library/PackageCache").glob(pattern + "/" + relative))
    if len(paths) != 1:
        raise RuntimeError("Expected one restored " + pattern + " dependency; found " + str(len(paths)) +
                           ". Use this project's existing package cache; do not download a replacement test framework.")
    return paths[0]


def compile_assembly(name, sources, references, mono, compiler, framework, fingerprints, no_build):
    output = CACHE / name
    standard = [framework / (name + ".dll") for name in
                ("mscorlib", "System", "System.Core", "System.Xml", "System.Numerics", "System.Runtime.Serialization")]
    references = standard + [framework / "Facades/netstandard.dll"] + references
    inputs = [compiler, Path(__file__)] + sources + references
    signature = hashlib.sha256()
    signature.update(b"code-tests-v1:release:csharp9")
    for path in inputs:
        signature.update(str(path).encode())
        signature.update(path.read_bytes())
    fingerprint = signature.hexdigest()
    record = fingerprints.get(name, {})
    if output.is_file() and record.get("inputs") == fingerprint and record.get("output") == digest(output):
        return "cached"
    if no_build:
        raise RuntimeError(name + " is missing or stale; rerun without --no-build to compile changed sources.")

    with tempfile.TemporaryDirectory(prefix="compile-", dir=CACHE) as temporary:
        target = Path(temporary) / name
        args = ["-nologo", "-nostdlib+", "-langversion:9.0", "-optimize+",
                "-target:" + ("exe" if name.endswith(".exe") else "library"), "-out:" + str(target)]
        args += ["-r:" + str(path) for path in references]
        args += [str(path) for path in sources]
        response = Path(temporary) / "compile.rsp"
        # The response file is parsed by Roslyn, never by a shell.
        response.write_text("\n".join('"' + arg.replace('"', '\\"') + '"' for arg in args))
        subprocess.run([str(mono), str(compiler), "-noconfig", "@" + str(response)], check=True, cwd=ROOT)
        target.replace(output)
    fingerprints[name] = {"inputs": fingerprint, "output": digest(output)}
    return "compiled"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("suite", nargs="?", choices=("fast", "full", "slow"), default="fast",
                        help="fast excludes LongRunning; full includes all portable tests; slow runs LongRunning only")
    parser.add_argument("--filter", action="append", default=[], metavar="NAME",
                        help="match a fixture or test name substring; repeat for OR")
    parser.add_argument("--list", action="store_true", help="discover matching tests without executing them")
    parser.add_argument("--no-build", action="store_true", help="use the cache; fail if source or dependencies changed")
    parser.add_argument("--editor", default=os.environ.get("TUANJIE_EDITOR"), help="installed editor path, for bundled Mono only")
    args = parser.parse_args()
    started = time.perf_counter()
    REPORTS.mkdir(parents=True, exist_ok=True)
    report = REPORTS / ("Code-" + args.suite + ".json")
    # Build failures must not leave an older success report at the current run's path.
    if not args.list:
        report.unlink(missing_ok=True)
        report.with_suffix(".xml").unlink(missing_ok=True)
    mono, compiler, framework = runtime(args.editor)
    nunit = dependency("com.unity.ext.nunit@*", "net35/unity-custom/nunit.framework.dll")
    json_library = dependency("com.unity.nuget.newtonsoft-json@*", "Runtime/Newtonsoft.Json.dll")
    CACHE.mkdir(parents=True, exist_ok=True)
    manifest = CACHE / "fingerprints.json"
    fingerprints = json.loads(manifest.read_text()) if manifest.exists() else {}
    for reference in (nunit, json_library):
        target = CACHE / reference.name
        if not target.exists() or digest(target) != digest(reference):
            shutil.copyfile(reference, target)

    builds = {}
    builds["Sim"] = compile_assembly("MyAiWorld.Sim.dll", sorted((GAME / "Scripts/Sim").rglob("*.cs")), [],
                                     mono, compiler, framework, fingerprints, args.no_build)
    # These two files are engine adapters; the rest of App is the exact production source.
    app = sorted(path for path in (GAME / "Scripts/App").rglob("*.cs")
                 if path.name not in {"SimulationRunner.cs", "WorldConfigAsset.cs"})
    builds["App"] = compile_assembly("MyAiWorld.App.dll", app, [CACHE / "MyAiWorld.Sim.dll", json_library],
                                     mono, compiler, framework, fingerprints, args.no_build)
    tests = sorted(path for path in (GAME / "Tests/EditMode").rglob("*.cs")
                   if path.name != "EditorConfigurationTests.cs")
    builds["Tests"] = compile_assembly("MyAiWorld.Tests.EditMode.exe", tests + [ROOT / "tools/CodeTestRunner.cs"],
                                       [CACHE / "MyAiWorld.Sim.dll", CACHE / "MyAiWorld.App.dll", nunit, json_library],
                                       mono, compiler, framework, fingerprints, args.no_build)
    manifest.write_text(json.dumps(fingerprints, indent=2) + "\n")
    build_seconds = time.perf_counter() - started
    print("Build: " + ", ".join(name + " " + state for name, state in builds.items()) +
          " ({:.3f}s). No editor connection.".format(build_seconds), flush=True)
    # Each process has isolated real files; no fake filesystem or player's save directory is used.
    with tempfile.TemporaryDirectory(prefix="code-tests-", dir=CACHE) as temporary:
        environment = dict(os.environ, MY_AI_WORLD_DATA_PATH=temporary)
        run = subprocess.run([str(mono), str(CACHE / "MyAiWorld.Tests.EditMode.exe"), args.suite, str(report),
                              "list" if args.list else "run"] + args.filter, cwd=ROOT, env=environment)
    elapsed = time.perf_counter() - started
    if not args.list and report.exists():
        data = json.loads(report.read_text())
        data.update(builds=builds, buildSeconds=build_seconds, elapsedSeconds=elapsed)
        report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
        print("Report: " + str(report))
    print("Total: {:.3f}s".format(elapsed))
    return run.returncode


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RuntimeError, OSError, subprocess.CalledProcessError, ValueError) as error:
        print("Code tests: " + str(error), file=sys.stderr)
        sys.exit(2)
