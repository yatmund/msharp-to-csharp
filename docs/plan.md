# msharp-to-csharp: plan

Goal: a one-shot, cross-platform tool that takes an M# repo, removes every Geeks Ltd dependency, leaves plain C#/ASP.NET Core, and keeps the repo's own tests passing. The result must build and deploy like any regular .NET app on any OS or cloud: no Geeks packages, tools or build steps, and no Windows-only runtime requirements.

Research sources (cloned/decompiled 2026-10-30): `Geeksltd/MSharp.Docs`, `Olive`, `Olive.MvcTemplate`, `Olive.MvcJs`, `msharp-build`, `Msharp-Olive-Examples`, `Olive.Generators`, and the `MSharp` 6.1.0 NuGet package.

---

## 1. What M# actually is

An M# solution has four parts:

| Folder | What it is | Geeks dependency |
|---|---|---|
| `M#/Model` (`#Model.csproj`) | Entity definitions in a fluent C# DSL (`class Contact : EntityType { String("First name").Mandatory(); }`) | `MSharp` NuGet |
| `M#/UI` (`#UI.csproj`) | Pages and modules in the same DSL (`ListModule<Contact>`, `FormModule<T>`, `SubPage<T>`, button `OnClick(x => x.SaveInDatabase())`) | `MSharp` NuGet |
| `Domain` | Hand-written partial classes and logic, plus **generated** entities/DAL | Olive packages |
| `Website` | Hand-written ASP.NET Core startup/controllers, plus **generated** controllers/views | Olive packages, Olive.MvcJs front end |

Build pipeline, from `msharp-build/OliveSolution.cs`:

```
build #Model → MSharp generate /model → build Domain → build #UI → MSharp generate /ui → yarn → tsc → sass → build Website
```

How generation works (from the decompiled package):

1. `#Model.csproj`/`#UI.csproj` have an `AfterBuild` Exec: `dotnet msharp.dsl.dll /build /model|/ui`.
2. `MSharp.DSL.dll` (net6.0) extracts an embedded `msharp.exe` + `MSharp.dll` + `MSharp.Framework.Core.dll`, then runs it.
3. `msharp.exe` calls back into `MSharp.DSL.dll`, which loads the compiled `#Model.dll`/`#UI.dll`, runs the DSL constructors, and serialises the model to XML on stdout.
4. `MSharp.dll` is the real generator, about 74k lines decompiled (`MSharp.Model.Programmer`, `ControllerGenerator`, `MvcGenerator`, `View`, `DbScript`). It is **closed source**, "Copyright Geeks Ltd – All rights reserved". It is not on GitHub.

What it writes, all of which is **gitignored** in the template:

- `Domain/[GEN-Entities]/`: partial entity classes on Olive `GuidEntity`, with validation, cascade-delete handlers and associations
- `Domain/[GEN-DAL]/`: ADO.NET data providers with the exact table/column/SQL mapping
- `DB/Tables/*.sql`: create scripts
- `Website/Controllers/{Pages,Modules}/`, `Website/Views/{Pages,Modules}/`: MVC controllers, view models and Razor
- `Website/app_Start/TaskManager.cs`, `M#/UI/SchemaExtensions.cs`

So the repo in git holds only the DSL. The C# the app runs never exists in source control.

### Why it isn't cross-platform

M# has these Windows-only bits:

- `MCSharpLoader`/`Compile` hard-code `%ProgramFiles%\dotnet\dotnet.exe` and `lib\MSharp.DSL.exe`.
- Backslash paths throughout: `"Controllers\\Modules"`, `"DB\\Tables"`, `"..\\..\\..\\Website\\bin\\debug\\"`.
- The csproj runs `start /min "" msharp /diagnose`, and the template ships `Build.bat`.
- `Website.csproj` runs `wwwroot\Styles\build\SassCompiler.exe` (WebCompiler) as a build task.

Olive itself is **already cross-platform**: netstandard2.0 for core/Entities/Data, net10.0 for Mvc, SkiaSharp with Linux native assets. The one exception is the Windows-only `System.Drawing.Common` paths in `Olive.Drawing`.

### What Olive is, and its size

Olive is open source under **GPL v3**, with a commercial licence on request. An app that ships with it is GPL-bound unless it holds that commercial licence. Vendoring Olive's source would not change that; only replacing it does. (This is a reading of `License.md`, not legal advice.)

These are the parts a typical M# app pulls in (C# LOC):

| Package | LOC | What the generated and hand-written code uses it for |
|---|---|---|
| `Olive` | 18.7k | **1,290 extension methods** (`HasValue()`, `IsEmpty()`, `ToString(", ")`, `Do()`, `WithMax()`…), `Context.Current`, config, `EscapeGCop` |
| `Olive.Entities` | 5.3k | `IEntity`, `GuidEntity`, `IDatabase`, `IDatabaseQuery<T>`, criteria, attributes, `Blob`, validation, save/delete events |
| `Olive.Entities.Data` (+`.SqlServer`) | 8.8k | `Database.Get/GetList/Save/Update/Delete/Of<T>()`, in-memory entity cache, transactions, LINQ-to-SQL criteria |
| `Olive.Mvc` (+Paging, Security, Web) | 8.2k | controller base, entity model binders (Guid → entity), view-model binding, HTML helpers, ajax "command" responses |
| `Olive.MvcJs` (TypeScript) | 6.0k | the front-end protocol the generated Razor relies on: ajax navigation, modals, form posting, validation |
| Audit, CSV, Export, PDF, Hangfire, Email, Blob, Security.* | ~5k | per-feature, varies by app |

Generated code is wired straight into this. Fragments emitted by the generator include `static IDatabase Database => Context.Current.Database();`, `Database.GetList<{0}>({1} => {1}.{2}Id == ID)`, `SoftDeleteAttribute.MarkDeleted(entity)`, `throw new ValidationException(...)` and `Database.CreateTransactionScope()`.

---

## 2. Why not a source generator

- **The DSL has to be executed, not read.** Definitions are constructors with arbitrary C#: lambdas, loops, `"c#:item.Name"` strings, inheritance between definitions. A Roslyn generator sees syntax only, so it would have to re-implement both the DSL runtime and the 74k-line generator.
- **It's a permanent dependency.** A generator runs on every build forever, which is the opposite of "run once and remove".
- **Fidelity.** A re-implementation would emit *different* code from what production runs today, and those differences become test failures and production bugs. The original generator's own output is, by definition, what the app runs now.

So the deliverable is a **one-shot `dotnet tool` CLI** (net10.0, runs on Windows, Linux and macOS) that materialises the generated code into source control, then strips and rewrites the dependencies. Working name: `msharp-eject`.

---

## 3. Phases

### Phase 0: `audit` (read-only)

`msharp-eject audit <repo>` loads the solution with Roslyn (`Microsoft.CodeAnalysis.Workspaces.MSBuild`) and reports:

- DSL inventory: entity types, pages, modules, workflows, `c#:` expressions
- whether generated output is present and current (generated files newer than the DSL and the `#Model`/`#UI` DLLs)
- every package reference from Geeks: `MSharp`, `Olive*`, `GCop`
- **Olive symbol usage**, resolved through the semantic model: each method, type and attribute from an Olive assembly, with counts and call sites, split into generated and hand-written code
- Windows-only build steps (`Exec` with `start`, `.bat`, `.exe` build tasks)
- deployment blockers: Windows-only APIs (`System.Drawing`, `Microsoft.Win32`, `DllImport`), Windows Docker base images, IIS-only `web.config`, hard-coded `\` paths in hand-written code, Geeks-hosted endpoints or tools in config/CI

This report sizes phase 2 for the real repo. Phase 2 can't be estimated properly without it.

### Phase 1: `eject` (removes M#; mechanical, exact)

1. **Get final generated output.**
   - Default: require generated files already on disk from a normal build, and verify they are current.
   - `--generate`: run the original M# pipeline one last time. This works on Windows only, because M# is Windows-bound. On Linux/macOS the tool prints the one-off CI recipe (a `windows-latest` GitHub Actions job that runs generation and uploads the output). This is the **only** Windows-dependent step, and it runs once.
2. **Commit the generated code.** Remove the `[GEN-*]`, `Controllers/Pages|Modules`, `Views/Pages|Modules` and `TaskManager.cs` ignores from `.gitignore`. Normalise line endings. Optionally rename `[GEN-Entities]` → `Entities`, keeping namespaces unchanged.
3. **Delete M#.** Remove `M#/`, remove `#Model`/`#UI` from the `.sln`/`.slnx`, remove the `MSharp` package and the `Generate code` Exec targets, delete `Build.bat` and `_M#_TEMP`.
4. **Fix cross-platform builds.** Swap the `SassCompiler.exe` WebCompiler task for a non-Geeks equivalent: `sass` in `package.json` driven by an MSBuild `Exec`, or the `AspNetCore.SassCompiler` package. Make `yarn`/`tsc` steps plain `npm`/`dotnet build` hooks.
5. **Verify** (see §5): `dotnet build` passes, the public API matches before vs after, and the repo's tests pass.

After phase 1 the repo is plain C#/Razor, builds on any OS, and has **no M#**. It still depends on Olive.

### Phase 2: `de-olive` (removes Olive; codemods, in dependency order)

Each step is a separate command that removes one package group. After each one the build and tests must pass. Order goes from leaves to roots:

1. **Leaf packages** (CSV, Export, PDF, Audit, Email, Hangfire, Blob, Security.*): map only what the audit shows is used, onto BCL or mainstream libraries. Example: `Olive.CSV` → `CsvHelper`.
2. **Olive core extensions.** Rewrite call sites with Roslyn:
   - Trivial BCL equivalents are inlined: `x.HasValue()` → `!string.IsNullOrEmpty(x)`, `x.None()` → `!x.Any()`.
   - The rest go into one project-local `Extensions.cs` holding **only** the methods the audit found in use. These are written fresh from documented behaviour, not copied, to avoid carrying GPL code.
3. **Olive.Mvc + Olive.MvcJs.** Replace the controller base, entity model binders, view-model binding, HTML helpers and the ajax command protocol with plain ASP.NET Core, plus a small hand-written JS module for the behaviours actually used (modal open/close, ajax reload, form post, notify). The generated controllers are now committed source, so this is a rewrite of known, finite code.
4. **Olive.Entities/Data → EF Core**, mapped onto the **existing schema** with no migration of data:
   - The generated `[GEN-DAL]` holds the exact table/column/SQL mapping. The tool reads it to emit `IEntityTypeConfiguration<T>` per entity.
   - `GuidEntity` → a plain POCO with `Guid Id`. `Database.Get/GetList/Save/Delete/Of<T>()` → `DbContext` calls.
   - Traps to preserve and cover with tests: `IsNew` semantics, the in-memory entity cache (reads after writes, stale data), `Saving/Saved/Deleting` event handlers in generated and partial code, code-level cascade delete, `[SoftDelete]` query filters, `ValidationException` from `Validate()`, transaction scopes, `Blob` file properties, and `[CacheObjects]`/`[LogEvents]`.

Phase 2 is **not** a generic, run-on-any-repo tool. The codemods are driven by what the audit finds, and steps 3–4 are a real port. Expect hand-finishing per repo.

---

## 4. Tool layout

```
src/MSharpEject/            dotnet tool, net10.0
  Program.cs                arg parsing: audit | eject | de-olive <step>; --dry-run, --repo
  Audit/                    Roslyn workspace load, symbol usage report (JSON + markdown)
  Eject/                    gitignore, sln, csproj (XDocument), file moves
  Rewrites/                 one CSharpSyntaxRewriter per codemod
  Verify/                   build/test runner, public-API diff
tests/MSharpEject.Tests/    TUnit
tests/fixtures/             Olive.MvcTemplate (GPL, public) + its generated output
```

Dependencies: `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.Build.Locator`, `TUnit`. Project files are handled with `System.Xml.Linq`, and arguments with a hand-rolled switch, since there are only a few commands.

Performance: the run is one-shot, so the cost is the single Roslyn solution load. Rewrites run per document in parallel, and each step does one build and one test run.

Every command supports `--dry-run`, which prints a diff and writes nothing. The tool also refuses to run on a dirty git tree, so every step can be undone with `git checkout`.

---

## 5. Tests (TUnit)

| Kind | What it proves |
|---|---|
| Unit: each rewriter | Input C# → expected C#, including trivia and comments kept, and `using` changes. Table-driven with `[Arguments]`. |
| Unit: csproj/sln/gitignore edits | Exact XML/text output. Leaves unrelated content untouched. |
| Unit: audit | Symbol counts on small in-memory compilations that reference Olive. |
| Idempotency | Running any command twice gives an empty second diff. |
| Dry run | No file changes on disk. |
| Integration: template fixture | Eject on a temp copy of `Olive.MvcTemplate` + generated output → `dotnet build` passes on ubuntu/windows/macos in the CI matrix. |
| **API equivalence** | Public types and members of `Domain.dll`/`Website.dll` match before vs after phase 1 (via `MetadataLoadContext`). This is the main guard that phase 1 changed nothing. |
| EF mapping (phase 2) | Given a `[GEN-DAL]` provider, the emitted EF config produces the same SQL table/column names. Round-trip CRUD on SQLite/Testcontainers SQL Server, compared against Olive's behaviour on the same data. |
| Acceptance | The target repo's own test suite passes after each phase. |

The fixture's generated output has to be produced once on Windows (see §3, phase 1, step 1) and committed as test data.

---

## 6. Open questions (these block a firm estimate)

1. **Which repo is the target?** Phase 2's size depends on the audit of it.
2. ~~Does "nothing from Geeks Ltd" include Olive?~~ **Decided 2026-10-30: yes.** Remove both M# and Olive (phases 1 and 2). The audit runs first.
3. Is generated output already on disk from a recent build, or does M# need one final Windows run?
4. Which database (SQL Server?), and which Olive extras are in play (microservices/Hub, Auth0, Hangfire)?
5. What tests does the repo have today? That decides how much "tests pass" actually guarantees in phase 2.
