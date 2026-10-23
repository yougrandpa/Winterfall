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

Editor tests: stop Play Mode, then use Tools → My AI World → Verification. Reports: `my-world/Logs/TestResults/*-CurrentProject.json`.

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

Use Unity Test Framework with NUnit `[Test]` and coroutine `[UnityTest]`. Name files `*Tests.cs` and methods descriptively, e.g. `Simulation_SameSeedAndCommands_ProducesSameHash`. Add invariant and fixed-seed regression tests for simulation changes; use PlayMode tests for scene/UI integration. Run both suites and manually inspect visual changes. No numeric coverage threshold is specified.

## Architecture & Asset Rules

Keep `Sim` free of Unity dependencies. Preserve stable entity ordering, seeded random streams, and tick-boundary commands. Presentation and future LLM integrations must not mutate world state directly. Preserve asset `.meta` files; exclude generated caches, logs, and IDE projects according to `my-world/ignore.conf`.

## Commit & Pull Request Guidelines

This checkout contains Plastic SCM metadata but no Git history to establish commit conventions. Use concise imperative summaries, such as `Fix deterministic pathfinding tie breaks`. PRs should describe behavior changes, link relevant issues/milestones, report test results, and include screenshots for visual changes.
