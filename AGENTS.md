# Repository Guidelines

## Project Structure & Module Organization

This is a Visual Basic .NET Windows Forms solution targeting .NET Framework 4.6.1. The main project is `EVE Isk per Hour.vbproj`; most source files live at the repository root. Forms use the standard split: `frmName.vb`, `frmName.Designer.vb`, and `frmName.resx`.

Key folders:

- `My Project/`: VB project settings, resources, and application manifest.
- `Resources/`: image assets used by the UI.
- `EVE-IPH-Update-Program/`: updater project included in the solution.
- `EVEIPH SQLite DLL Updater/`: SQLite DLL updater utility.
- `Root Directory/`: build output and runtime files.
- `packages/` and `packages.config`: NuGet package dependencies.

## Build, Test, and Development Commands

Use Visual Studio 2019+ or a Developer PowerShell with MSBuild available.

```powershell
nuget restore "EVE Isk per Hour.sln"
msbuild "EVE Isk per Hour.sln" /p:Configuration=Debug /p:Platform=x86
msbuild "EVE Isk per Hour.sln" /p:Configuration=Release /p:Platform=x86
```

The solution maps Any CPU builds to x86. Build output is configured to write to `..\Root Directory\`.

## Coding Style & Naming Conventions

Follow the existing VB.NET style in nearby files. Use `Option Explicit On`; the project has `Option Strict Off` and `Option Infer On`, so avoid broad type changes unless required. The `.editorconfig` disables IDE1006 naming warnings, so preserve established names such as `frmMain`, `EVEAssets`, and generated WinForms members.

Do not hand-edit `*.Designer.vb` files unless the change is designer-generated or unavoidable. Keep UI resources in `.resx` files and `Resources/`.

## Testing Guidelines

There is no dedicated unit test project. Validate changes by building the full solution and manually exercising the affected WinForms workflow. For data, pricing, ESI, or SQLite changes, test startup plus the screen or calculation path touched.

If adding automated tests, use a separate test project and name test classes after the unit under test, for example `BlueprintTests`.

## Commit & Pull Request Guidelines

Recent history uses short commit subjects such as `Build 5.1.9626.40194` and `Removed unused scopes from selection`; there is no strict Conventional Commits pattern. Use concise imperative summaries.

Pull requests should include a description, affected forms/modules, manual validation steps, and screenshots for visible UI changes. Link related issues when available.

## Security & Configuration Tips

Do not commit personal tokens, ESI credentials, or local IDE state. Keep dependency updates explicit in `packages.config` and verify SQLite x86/x64 runtime files before release builds.
