"""Exercise cache safety using temporary files and a compiler stub, without C# or an editor."""

import importlib.util
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("code_test_cli", Path(__file__).parents[1] / "test.py")
CLI = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CLI)


class BuildCacheTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        self.patch_cache = patch.object(CLI, "CACHE", self.root)
        self.patch_cache.start()
        self.addCleanup(self.patch_cache.stop)
        self.framework = self.root / "framework"
        (self.framework / "Facades").mkdir(parents=True)
        for name in ("mscorlib", "System", "System.Core", "System.Xml", "System.Numerics", "System.Runtime.Serialization"):
            (self.framework / (name + ".dll")).write_bytes(b"framework")
        (self.framework / "Facades/netstandard.dll").write_bytes(b"facade")
        self.compiler = self.root / "csc.exe"
        self.compiler.write_bytes(b"compiler")
        self.source = self.root / "Source.cs"
        self.source.write_text("class Source {}")
        self.reference = self.root / "Dependency.dll"
        self.reference.write_bytes(b"dependency")
        self.records = {}
        self.process = patch.object(CLI.subprocess, "run", side_effect=self.fake_compiler).start()
        self.addCleanup(patch.stopall)

    @staticmethod
    def fake_compiler(command, **kwargs):
        response = Path(command[-1][1:])
        arguments = shlex.split(response.read_text())
        output = next(arg[len("-out:"):] for arg in arguments if arg.startswith("-out:"))
        Path(output).write_bytes(b"compiled-output")

    def build(self, no_build=False, sources=None):
        return CLI.compile_assembly("Example.dll", [self.source] if sources is None else sources,
                                    [self.reference], self.root / "mono", self.compiler,
                                    self.framework, self.records, no_build)

    def test_unchanged_inputs_reuse_output_without_invoking_compiler(self):
        self.assertEqual(self.build(), "compiled")
        self.process.reset_mock()
        self.assertEqual(self.build(no_build=True), "cached")
        self.process.assert_not_called()

    def test_source_change_is_rejected_by_no_build_then_recompiled(self):
        self.build()
        self.source.write_text("class Source { int Changed; }")
        self.process.reset_mock()
        with self.assertRaisesRegex(RuntimeError, "stale"):
            self.build(no_build=True)
        self.process.assert_not_called()
        self.assertEqual(self.build(), "compiled")

    def test_dependency_change_rebuilds_its_consumer(self):
        self.build()
        self.reference.write_bytes(b"changed-dependency")
        self.assertEqual(self.build(), "compiled")

    def test_source_addition_and_removal_invalidate_cache(self):
        self.build()
        added = self.root / "Added.cs"
        added.write_text("class Added {}")
        self.assertEqual(self.build(sources=[self.source, added]), "compiled")
        self.assertEqual(self.build(sources=[self.source]), "compiled")

    def test_output_tampering_and_missing_outputs_cannot_pass_no_build(self):
        self.build()
        output = self.root / "Example.dll"
        output.write_bytes(b"different-assembly")
        with self.assertRaisesRegex(RuntimeError, "stale"):
            self.build(no_build=True)
        output.unlink()
        with self.assertRaisesRegex(RuntimeError, "missing"):
            self.build(no_build=True)

    def test_compile_failure_preserves_last_output_and_fingerprint(self):
        self.build()
        previous = dict(self.records)
        self.source.write_text("invalid C#")
        self.process.side_effect = subprocess.CalledProcessError(1, ["compiler"])
        with self.assertRaises(subprocess.CalledProcessError):
            self.build()
        self.assertEqual((self.root / "Example.dll").read_bytes(), b"compiled-output")
        self.assertEqual(self.records, previous)


if __name__ == "__main__":
    unittest.main()
