# Repository Guidelines

## Project Structure & Module Organization

The engine project lives in `my-world/`. Under `my-world/Assets/Game/`:

- `Scripts/Sim/`: engine-independent, deterministic C# simulation.
- `Scripts/App/`: sessions, tick scheduling, queries, logging, and replay.
- `Scripts/Presentation/`: camera, rendering, selection, and runtime UI.
- `Editor/`: bootstrap scene/configuration generator.
- `Scenes/` and `Config/`: startup scene and world configuration assets.
- `Tests/EditMode/` and `Tests/PlayMode/`: simulation and runtime tests.

Read `my-world/Docs/Architecture.md` before architectural changes; consult `Docs/Roadmap.md` and `Docs/Progress/` for milestone scope. Placeholder artwork is generated at runtime.

## Build, Test, and Development Commands

Use Tuanjie 1.8.2, editor `2022.3.62t4`. Open `my-world/` in Tuanjie Hub, load `Assets/Game/Scenes/Bootstrap.unity`, and press Play. Create player builds through File → Build Settings with Bootstrap enabled.

Default validation runs directly from code, without starting or connecting to the editor:

```bash
python3 tools/test.py                         # Fast NUnit tests; incremental compilation is cached
python3 tools/test.py --filter HuntingGameplayTests
python3 tools/test.py full                    # All portable tests, including long simulations
python3 tools/test.py --no-build              # Require an up-to-date cache; never compile
```

Use `python3 -m unittest discover -s tools/tests` when changing the test launcher/cache. Code reports: `my-world/Logs/TestResults/Code-*.json` and `.xml`. The launcher reuses the installed editor's Mono runtime and restored NUnit/Newtonsoft packages, but does not launch the editor or require a bridge. See `my-world/Docs/Testing.md` for prerequisites and suite boundaries.

For engine integration changes, stop Play Mode and use Tools → My AI World → Verification → Run PlayMode Smoke Tests. Full editor suites are optional broader checks for relevant changes or release validation. Reports: `my-world/Logs/TestResults/*-CurrentProject.json`.

Run CLI tests from the workspace root:

```bash
TUANJIE="/Applications/Tuanjie/Hub/Editor/2022.3.62t4/Tuanjie.app/Contents/MacOS/Tuanjie"
PROJECT="$PWD/my-world"
mkdir -p "$PROJECT/Logs/TestResults"
"$TUANJIE" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$PROJECT/Logs/TestResults/EditMode.xml"
```

For runtime tests, replace `EditMode` with `PlayMode` in both arguments and omit `-nographics`. Close the editor before CLI testing. Never add `-quit` to test commands.

Tools → My AI World → Build Bootstrap Scene regenerates the scene and resets configuration defaults; use it deliberately.

## Coding Style & Naming Conventions

Follow `my-world/.editorconfig`: UTF-8, LF, four-space C# indentation, Allman braces, block-scoped namespaces, and System imports first. JSON, asmdef, and serialized asset files use two spaces. Use `MyAiWorld.*` namespaces, PascalCase types/methods/properties, camelCase locals, and `_camelCase` private fields. Match filenames to principal types. No standalone formatter or linter command is configured.

## Testing Guidelines

Use NUnit `[Test]` for simulation, persistence, commands and immutable read models; run these through the standalone code launcher first. Keep engine-dependent tests in PlayMode (or `EditorConfigurationTests.cs` for serialized editor assets), using `[UnityTest]` only when frames are required. Do not add scene loading, screenshots or real-time waits to test pure logic. Name files `*Tests.cs` and methods descriptively, e.g. `Simulation_SameSeedAndCommands_ProducesSameHash`. Add invariant and fixed-seed regression tests for simulation changes. Mark long simulations `LongRunning`; retain their assertions and run them with `full` or `slow` when relevant. Use the four `EngineSmoke` checks for runtime wiring changes, and targeted PlayMode/visual review only when rendering, UI or assets change. Do not run both full editor suites after every edit. No numeric coverage threshold is specified.

## Architecture & Asset Rules

Keep `Sim` free of Unity dependencies. Preserve stable entity ordering, seeded random streams, and tick-boundary commands. Presentation and future LLM integrations must not mutate world state directly. Preserve asset `.meta` files; exclude generated caches, logs, and IDE projects according to `my-world/ignore.conf`.

## Commit & Pull Request Guidelines

This checkout contains Plastic SCM metadata but no Git history to establish commit conventions. Use concise imperative summaries, such as `Fix deterministic pathfinding tie breaks`. PRs should describe behavior changes, link relevant issues/milestones, report test results, and include screenshots for visual changes.
