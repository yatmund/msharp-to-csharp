# Replace Olive

`msharp-eject de-olive <repo>` (or **Replace Olive…** in the app) rewrites code that uses Olive so it uses .NET instead. It runs after [eject](eject.md): it refuses while `M#/` exists, because M# would regenerate over the changes.

It uses the same safety model as eject: a preview first, a clean git tree, and a build and test before and after.

## What it rewrites today

These are Olive extension methods with an exact .NET equivalent. Each one was checked against Olive's source (`Olive/-Extensions/String.cs`, `Linq.cs`), including how it treats `null`:

| Olive | Becomes | Notes |
|---|---|---|
| `s.HasValue()` | `!string.IsNullOrEmpty(s)` | |
| `!s.HasValue()` | `string.IsNullOrEmpty(s)` | Negations are folded, so you never get `!!x` |
| `s.IsEmpty()` | `string.IsNullOrEmpty(s)` | |
| `s.OrEmpty()` | `s ?? string.Empty` | |
| `items.HasAny()` | `items?.Any() == true` | Olive returns `false` for `null` |
| `items.None()` | `items?.Any() != true` | Olive returns `true` for `null` |
| `items.None(predicate)` | `!items.Any(predicate)` | The `(item, index)` overload is left alone; .NET has no equivalent |
| `items.Lacks(item)` | `!items.Contains(item)` | Only the generic overload. `string.Lacks` and the `T?` overload are left alone. |

Matching is by exact symbol: the method must bind to `Olive.OliveExtensions` with the matching overload. Your own `HasValue()`, or one from another library, is never touched.

Parentheses are added only where precedence needs them. `using System.Linq;` is added when a rewrite needs it and the project doesn't already have it as a global using. `using Olive;` is removed once nothing else in the file binds to the `Olive` namespace.

## The replacement library

Calls with no exact .NET equivalent move to `Platform`, a library the tool adds to the repo (`Platform/Platform.csproj`). It is written fresh, never copied from Olive, and uses Olive's exact signatures, so code that compiled against Olive compiles against it. `using Olive...` becomes `using Platform...`, and `OliveExtensions` becomes `Extensions`.

- **Helpers first, file by file.** A file that only uses Olive's static helpers (extension methods and the like) moves once the library has every helper it uses.
- **Types all at once.** Entities, the database API and MVC types can't be half swapped. They move together with the package references once the library covers everything the repo uses. Razor `@using`, `@inherits` and tag helpers are rewritten in the same step, and vendored Olive source is deleted.
- **Nothing moves that breaks the build.** Each step is compiled in memory first, and anything that adds an error stays on Olive.
- **Gaps are listed.** Every Olive member the library doesn't provide yet is reported with how often it's used. The full list is in the log.

What the library covers today, as public and protected members of each Olive package (reported by `ApiCoverage` in `tests/Platform.Compat.Tests`). Overridable members must also match Olive's access and `virtual`, or an app's `override` wouldn't compile:

| Olive package | Covered | How it's checked |
|---|---|---|
| `Olive` (core helpers) | strings, numbers, dates, collections, plus `Context`, `Config`, `Log`, `LocalTime`, events | Each helper runs next to the real Olive on the same inputs; results and exceptions must match |
| `Olive.Entities` | all 754 members | API match; behaviour through the data tests |
| `Olive.Entities.Data` | 454 of 462 | The generated SQL and parameters are compared with Olive's for inserts, updates, deletes, inheritance joins, soft delete, auto-numbers, paging and the query translations |
| `Olive.Entities.Data.SqlServer` | all | as above |
| `Olive.Audit` | all | API match |
| `Olive.CSV` | all | Reading and writing compared with Olive on the same input, including quoting and blank rows |
| `Olive.Export` | all | The Excel XML and CSV output are compared byte for byte |
| `Olive.ApiClient`, `Olive.Microservices` | all | API match |
| `Olive.Web` | all | API match |
| `Olive.Mvc.Security` | all | API match. Auth cookies aren't wire-compatible, so users sign in again after the switch |
| `Olive.Mvc` | all | The same controllers run in a test server on Olive's `Startup` and on ours. Responses must match for routing, model binding, the binding hooks, validation, file downloads, cookies and the JSON sent to the Olive.MvcJs front end. The middleware order, registered services and tag helper attribute names are compared too |
| `Olive.MvcJs` (npm) | all 50 modules | Rewritten from its typings and observed behaviour. The same pages run in Chromium on Olive.MvcJs and on ours, then the DOM, URL, requests, alerts and third-party plugin calls are compared (`tests/frontend-parity`) |
| `Olive.Email` | all | The stored attachments string, the vCalendar view and the template merge are compared with Olive's, so queued messages still send |

Where Olive has a quirk, the replacement keeps it, because code may rely on it.

When the last Olive reference goes, the front end moves too. The modules are copied to `wwwroot/scripts/platform` as TypeScript, so the app's own build compiles them. The `olive.mvc` npm package becomes `pako`, and `tsconfig.json`, `references.js` and the app's scripts are repointed (`AppPage extends PlatformPage`, imports from `platform/...`).

Known limits:

- **Encrypted properties.** `[EncryptedProperty]` interceptors aren't replaced, because their cipher format has to match what's already in your database. A repo that uses them sees them listed as a gap.
- **Generated data access classes.** M# apps may ship generated data provider classes that override Olive's column mapping. If yours do, they show up as gaps on the first run, and they're the next thing to cover.
- **Not run against a live database yet.** The SQL matches Olive's, but saving, loading, transactions and the cache haven't been exercised against SQL Server in the tests.
- **Error notification emails** don't include the request URL, IP and user, so the Email module doesn't depend on ASP.NET.
- **Where Olive crashes, ours doesn't.** An unparsable non-nullable `DateTime` or `bool` in a form gives a validation error instead of a 500.
- **Download names with non-ASCII characters** are encoded so browsers show them. Olive replaces those characters with `?`.
- **Olive's request timing log** isn't replaced.
- **Third-party UI plugins are recorded, not run, in the browser tests.** The options passed to the date picker, typeahead, file upload, slider and similar plugins are compared, and their callbacks are driven by hand. The plugins' own behaviour isn't exercised.
- **No generated M# pages were available.** The front-end scenarios use markup written to match what the code reads, plus the sample app's real scripts and `references.js`. Check a real app in a browser after the switch.
- **Deliberately not copied from Olive.MvcJs:** a literal `class="undefined"` on search results, and `document.title` becoming `undefined` when a modal closes.
- **Run `yarn install` after the switch** so `pako` lands in `wwwroot/lib`. An app with its own `@types/jquery` may need to delete `wwwroot/scripts/platform/globals.d.ts` declarations that clash.
- **Razor views aren't run in the tests.** Tag helpers are checked one by one, and `asp-replace-this` couldn't be exercised at all because Olive's version fails outside a real view.
- **Not covered yet:** PDF, Drawing, PassiveBackgroundTasks, Mvc.Testing, and about 1,500 of Olive's core helpers (mostly async LINQ and IO).

## What it deliberately leaves

- **Calls inside expression trees**, such as `Database.GetList<Contact>(c => c.Name.HasValue())`. Olive translates those lambdas to SQL, and it might not translate `string.IsNullOrEmpty`. They're counted in the output and move to EF Core with the data layer.
- **Calls in a `?.` chain**, such as `a?.Name.HasValue()`, where the receiver isn't an expression on its own.
- **Anything without an exact equivalent.** Better to leave Olive in place than to change behaviour.

## Tests that guard this

- One test per rewrite and per left-alone case, with exact expected output.
- A behaviour test that compiles the original and the rewritten code, runs both on `null`, empty and normal inputs, and checks the results match.
- An end-to-end test on a small solution: rewrite, build, and run its MSTest suite before and after.

## What's next

What gets replaced next is decided by audits of real repos (the **Olive symbols** tab). It's done in this order, each step leaving the repo building with its tests green:

1. More core extensions, picked by how often real repos use them. Methods with no inline equivalent get a fresh, project-local implementation (never copied from Olive, which is GPL).
2. Leaf packages still to do: PDF, Drawing, background tasks.
3. The data layer to EF Core, on the existing schema, with EF Core migrations replacing Olive's script-based temp databases.
4. Olive package references, removed from each project once nothing uses them.

[plan.md](plan.md) has the detail, including the traps each step has to preserve.
