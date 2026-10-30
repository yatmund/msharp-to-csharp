# msharp-eject

Move an [M#](https://github.com/Geeksltd/MSharp.Docs) application off M# and the [Olive](https://github.com/Geeksltd/Olive) framework, so it becomes an ordinary ASP.NET Core app that builds and deploys on any OS or cloud.

> **Status: early.** Only `audit` works today. `eject` (remove M#) and `de-olive` (remove Olive) are planned; see [docs/plan.md](docs/plan.md).
>
> Not affiliated with or endorsed by Geeks Ltd. M# and Olive are their products.

## Why

An M# solution keeps its entity and UI definitions in a C# DSL (`M#/Model`, `M#/UI`). At build time a closed-source generator turns them into the entities, data access, controllers and Razor views the app actually runs. That generated code is gitignored, so it never reaches source control.

This causes three problems:

- **The build only works on Windows.** The generator hard-codes `C:\Program Files\dotnet\dotnet.exe` and backslash paths, and the template's build runs `.bat` files and a Windows-only Sass compiler.
- **The app depends on Olive at runtime.** The generated code is built on Olive's entity, data-access and MVC layers, plus the Olive.MvcJs front end. Olive is GPL v3 unless you buy a commercial licence.
- **The code you deploy isn't the code you review**, because the real C# only exists after a build.

`msharp-eject` is meant to be run **once** per repo. Its end state is plain C# and Razor in git, with no Geeks packages, tools or build steps, which builds and deploys on Linux, macOS, Windows or any container platform. The repo's own tests must still pass.

## How it works

The work is done in three passes, each safe to review on its own:

| Command | What it does | Status |
|---|---|---|
| `audit` | Read-only report: Geeks packages, M# definitions, generated output, Windows-only build steps, deployment blockers, and every Olive symbol the code uses | ✅ |
| `eject` | Commits M#'s final generated output as source, deletes the `M#` folder and `MSharp` package, and replaces Windows-only build steps | planned |
| `de-olive` | Roslyn codemods that replace Olive with the .NET base library, ASP.NET Core and EF Core (mapped onto your existing database schema) | planned |

Source generators are deliberately not used. M# definitions have to be *run* to mean anything, and a generator would add a permanent build dependency, which defeats the point. [docs/plan.md](docs/plan.md) covers the reasoning and the research behind it.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows, Linux or macOS
- For the full audit, the target repo must restore (`dotnet restore`) on your machine. That means access to the Olive packages on nuget.org and to any private feeds it uses.

## Usage

Not on NuGet yet. Run it from source:

```bash
git clone https://github.com/<owner>/msharp-to-csharp.git
```

```bash
dotnet run --project msharp-to-csharp/src/MSharpEject -- audit path/to/your-msharp-repo --out audit.md
```

| Option | Meaning |
|---|---|
| `--out <file>` | Write the markdown report to a file instead of stdout |
| `--no-semantic` | Skip restoring and loading projects with Roslyn. File scans only, so much faster, but there's no Olive symbol table |

The full audit runs `dotnet restore` on each non-M# project and loads it with Roslyn's `MSBuildWorkspace`. It writes nothing into your repo apart from what `dotnet restore` creates in `obj/`.

[docs/audit.md](docs/audit.md) explains every section and finding in the report and what to do about it.

## Development

```bash
dotnet test
```

Tests use [TUnit](https://tunit.dev) on Microsoft.Testing.Platform (configured in `global.json`). They cover:

- unit tests for each scanner, with real csproj and source snippets from M# projects
- Olive symbol resolution against an in-memory fake `Olive` assembly
- an end-to-end audit of a small fake M# repo, which loads real projects through `MSBuildWorkspace`

CI runs the suite on Ubuntu, Windows and macOS.

Layout:

```
src/MSharpEject/          the dotnet tool
  Program.cs              command-line entry
  Audit/RepoScan.cs       file-level scans (csproj, package.json, DSL, blockers)
  Audit/OliveUsage.cs     Roslyn pass that counts Olive symbols
  Audit/Auditor.cs        runs the scans, renders the markdown report
tests/MSharpEject.Tests/  TUnit tests
docs/                     plan and reference docs
```

Contributions are welcome. Audit output from real M# repos (with anything private stripped) is the most useful input right now: it shows which Olive APIs the codemods need to cover first.

Olive is GPL v3, so don't copy its source into this repo or into code the tool generates. Write replacements from documented behaviour.

## License

[MIT](LICENSE)
