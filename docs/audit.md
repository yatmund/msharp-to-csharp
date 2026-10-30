# Audit report reference

`msharp-eject audit <repo>` writes a markdown report. This page explains each section, what it detects and what to do about it.

The audit never changes source files. The full (semantic) mode runs `dotnet restore`, which writes to `obj/` in the usual way.

Folders skipped everywhere: `bin`, `obj`, `node_modules`, `.git`, `.vs`, `packages`, `wwwroot/lib`.

## Geeks packages and tools

Every dependency published by Geeks Ltd. This is the list `eject` and `de-olive` have to empty.

| Source | Matched when |
|---|---|
| `*.csproj` `<PackageReference>` | Id is, or starts with `<prefix>.`, one of `Olive`, `MSharp`, `GCop`, `Geeks`, `Zebble`, `Pangolin`, `msharp-build` (case-insensitive). `OliveOil` does not match. |
| `.config/dotnet-tools.json` | Tool id matches the same rule |
| `package.json` `dependencies` / `devDependencies` | Name matches the same rule, or the version spec points at the `Geeksltd` GitHub org (e.g. `"olive.mvc": "Geeksltd/Olive.MvcJs#2.3.33"`) |

Packages referenced only from `M#/Model` and `M#/UI` disappear with `eject`. Every other one is removed by `de-olive`.

## M# definitions

Counts the classes under `M#/` by their DSL base type, which shows how much the generator was producing:

| Base type | Generates |
|---|---|
| `EntityType`, `SubType<T>` | entity class, data provider, table script |
| `ListModule<T>`, `FormModule<T>`, `ViewModule<T>`, `GenericModule`, `MenuModule` | controller, view model, Razor view |
| `RootPage`, `SubPage<T>` | page controller and view, routing |
| `c# expressions` | `"c#:..."` string literals. These are C# snippets the generator pastes into its output, and a common source of surprises during review. |

## Generated output on disk

Each folder M# writes to, with its file count and the newest write time. Only `.cs`, `.cshtml` and `.sql` files count. The template commits a `DB/Tables/-Warning!.txt`, which is ignored here.

| Folder | Contents |
|---|---|
| `Domain/[GEN-Entities]` | partial entity classes |
| `Domain/[GEN-DAL]` | ADO.NET data providers: table, column and SQL mapping |
| `Website/Controllers/Pages`, `Website/Controllers/Modules` | MVC controllers and view models |
| `Website/Views/Pages`, `Website/Views/Modules` | Razor views |
| `DB/Tables` | create scripts |

Possible notes under the table:

- **No generated output. M# must run once before `eject`.** Build the solution once the M# way (`msharp-build`, on Windows) so the generated code exists. `eject` commits it as source.
- **Generated output is older than the M# definitions.** Some definitions changed after the last generation. Rebuild first, or the committed code won't match the definitions. A fresh `git clone` resets file times, so this can be a false alarm right after cloning.

## Windows-only or Geeks build steps

Found in `*.csproj`:

| Rule | Matches | Typical example | Fix |
|---|---|---|---|
| `build-exec` | `<Exec Command>` containing `msharp`, `start `, `.bat`, `.cmd` or `.exe` | `dotnet msharp.dsl.dll /build /ui`, `start /min "" msharp /diagnose` | M# steps are deleted by `eject`. Swap others for a cross-platform command. |
| `build-task-exe` | `<UsingTask AssemblyFile="*.exe">` | `wwwroot\Styles\build\SassCompiler.exe` (WebCompiler) | Use the `sass` npm package or a NuGet Sass compiler |

## Deployment blockers

Things that would stop the app building or running on Linux or in a standard container. Files under `M#/` and the generated folders are excluded; `eject` and `de-olive` deal with those.

| Rule | Files | Matches | Fix |
|---|---|---|---|
| `windows-api` | `*.cs` | `System.Drawing`, `Microsoft.Win32`, `[DllImport`, `Registry.`, `System.Windows.Forms`, `System.Management` | SkiaSharp or ImageSharp in place of `System.Drawing`; configuration in place of the registry; cross-platform libraries otherwise |
| `windows-path` | `*.cs` | Drive letters (`"C:\..."`), or a backslash between path segments in a string (`"App_Data\\Files"`, `@"App_Data\Files"`) | `Path.Combine`, or forward slashes |
| `container-image` | `Dockerfile*` | `FROM` a Windows image (`nanoserver`, `windowsservercore`), or a retired one (`microsoft/aspnetcore*`, `microsoft/dotnet`, `mcr.microsoft.com/dotnet/core/*`) | Eject switches these to `mcr.microsoft.com/dotnet/aspnet|sdk:<version>` |
| `eol-framework` | `*.csproj` | A target framework Microsoft no longer supports: `netcoreapp*`, `net5.0`–`net7.0`, .NET Framework below 4.6.2 | Retarget to a supported version (currently `net8.0` or later) and re-run the tests. The M# template targets `net6.0`. |
| `geeks-reference` | `*.json`, `*.yml`, `*.yaml`, `*.config`, `*.xml`, `*.ps1`, `*.bat`, `*.cmd`, `Jenkinsfile` | `geeks.ltd`, `geeksltd`, `msharp.co.uk`, or the word `msharp` | Remove, or swap in your own infrastructure (CI templates, licensing endpoints, front-end packages) |
| `iis-config` | `web.config` | Any `web.config` | Kestrel ignores it. Move any settings it holds to `appsettings.json` or middleware. |
| `windows-script` | `*.bat`, `*.cmd` | Any batch file | Use a `dotnet` command, an npm script or a cross-platform shell script |

These are text matches, so expect some false positives, such as a regex pattern that looks like a Windows path. Every finding gives the file and line so you can check it.

## Olive symbol usage

Present only in the full (semantic) mode. Skipped with `--no-semantic`.

Each non-M# project is restored and loaded with Roslyn's `MSBuildWorkspace`. Every name in the code is resolved through the semantic model and counted when it comes from an assembly matching the package rule above.

- **Overloads, generic instantiations and constructors** are grouped under one definition: `OliveExtensions.HasValue`, or `Olive.GuidEntity` for both `GuidEntity` and `new GuidEntity()`.
- **Extension methods** are reported under their declaring class, e.g. `OliveExtensions.HasValue` for `name.HasValue()`.
- **Code that doesn't compile is still counted.** Hand-written partial classes often reference generated members that aren't on disk yet. When overload resolution fails, the first candidate symbol is used.
- **Vendored Olive source** (a project whose assembly name is `Olive*`) is not counted as usage.

Columns:

| Column | Meaning |
|---|---|
| Symbol | Type, or `Type.Member` |
| Kind | `extension`, `class`, `interface`, `attribute`, `method`, `property`, … |
| Assembly | The Olive assembly that declares it |
| Uses | Total references |
| Generated | References inside generated folders. These get replaced along with the generated code they live in. |
| Files | Distinct files that reference it |

The summary table above the full list gives totals per assembly. Together they size the `de-olive` work: the hand-written uses (Uses − Generated) of a few hundred symbols are what the codemods and manual porting must cover.

If projects fail to load, the report lists the errors and says the counts may be low. The usual cause is a restore failure, such as a private feed or a missing SDK.
