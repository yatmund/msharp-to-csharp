using MSharpEject.Audit;

namespace MSharpEject.Tests;

public class RemainingTests
{
    static AuditResult Result(List<PackageRef> packages, List<SymbolUse> symbols, List<Finding> blockers, bool semantic = true) =>
        new("/repo", packages, [], [], [], null, blockers, semantic, symbols, []);

    [Test]
    public async Task Items_group_olive_by_area_and_blockers_by_rule()
    {
        var items = Remaining.Items(Result(
            [new("Website/Website.csproj", "Olive.Mvc", "3"), new("Domain/Domain.csproj", "Olive", "2"), new("Website/package.json", "olive.mvcjs", "^1"),
             new("Domain/Domain.csproj", "Olive.Email", "3"), new("Website/Website.csproj", "Olive.Mvc.Security", "3")],
            [new("Olive.Entities", "Olive.Entities.Database", "class", 7, 2, 3)],
            [new("Logic/Pdf.cs", 4, "font", "x"), new("Logic/Pdf.cs", 9, "font", "y")]));

        await Assert.That(items.Select(i => i.Item)).IsEquivalentTo(
            ["Data layer", "MVC", "Front end", "Service integrations", "Core extensions and runtime", "font"]);
        await Assert.That(items.Single(i => i.Item == "MVC").Found).IsEqualTo("Olive.Mvc");
        await Assert.That(items.Single(i => i.Item == "Front end").Found).IsEqualTo("olive.mvcjs");
        await Assert.That(items.Single(i => i.Item == "Service integrations").Found).IsEqualTo("Olive.Email, Olive.Mvc.Security");
        await Assert.That(items.Single(i => i.Item == "Data layer").Found).IsEqualTo("7 uses of 1 symbols (5 hand-written)");
        await Assert.That(items.Single(i => i.Item == "font")).IsEqualTo(new LeftItem("Linux", "font", "2 in 1 files", "Not migrated", Remaining.RuleNotes["font"]));
    }

    [Test]
    public async Task Verdicts_say_what_remains()
    {
        var left = Remaining.Verdicts(Result([new("D.csproj", "Olive", "2"), new("M.csproj", "MSharp", "6")],
            [new("Olive", "OliveExtensions.OrEmpty", "extension", 3, 0, 1)], [new("a.cs", 1, "windows-path", "x")]));

        await Assert.That(left).IsEquivalentTo(["Olive-free: no. 1 Geeks package references and 3 Olive symbol uses remain.",
            "Linux-ready: no. 0 build steps and 1 deployment blockers remain."]);
        await Assert.That(Remaining.Verdicts(Result([], [], []))[0]).IsEqualTo("Olive-free: yes.");
        await Assert.That(Remaining.Verdicts(Result([], [], [], semantic: false))[0]).StartsWith("Olive-free: unconfirmed.");
    }
}
