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
2. Leaf packages: CSV, Export, PDF, Audit, Email, Hangfire, Security.
3. The MVC layer and the Olive.MvcJs front end.
4. The data layer to EF Core, on the existing schema, with EF Core migrations replacing Olive's script-based temp databases.
5. Olive package references, removed from each project once nothing uses them.

[plan.md](plan.md) has the detail, including the traps each step has to preserve.
