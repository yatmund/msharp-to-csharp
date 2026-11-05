using MSharpEject.Replacement;

namespace MSharpEject.Tests;

public class FrontEndSwapTests
{
    [Test]
    public async Task Package_json_swaps_olive_for_pako()
    {
        var edited = FrontEndSwap.PackageJson("{\n  \"dependencies\": {\n    \"jquery\": \"1\",\n    \"olive.mvc\": \"Geeksltd/Olive.MvcJs#2.3.33\",\n    \"bootstrap-select\": \"1\"\n  }\n}");
        await Assert.That(edited).IsEqualTo("{\n  \"dependencies\": {\n    \"jquery\": \"1\",\n    \"pako\": \"^2.1.0\",\n    \"bootstrap-select\": \"1\"\n  }\n}");
    }

    [Test]
    public async Task Package_json_keeps_valid_json_when_olive_is_last()
    {
        var edited = FrontEndSwap.PackageJson("{\n  \"dependencies\": {\n    \"jquery\": \"1\",\n    \"olive.mvc\": \"x\"\n  }\n}");
        await Assert.That(System.Text.Json.JsonDocument.Parse(edited).RootElement.GetProperty("dependencies").GetProperty("pako").GetString()).IsEqualTo("^2.1.0");
    }

    [Test]
    public async Task Tsconfig_points_at_the_library()
    {
        var edited = FrontEndSwap.TsConfig("{\n  \"paths\": {\n    \"olive/*\": [ \"wwwroot/lib/olive.mvc/typings/*\" ]\n  },\n  \"exclude\": [\n    \"wwwroot/lib/olive.mvc/src\",\n    \"obj\"\n  ]\n}");
        await Assert.That(edited).Contains("\"platform/*\": [ \"wwwroot/scripts/platform/*\" ]");
        await Assert.That(edited).DoesNotContain("olive");
    }

    [Test]
    public async Task References_rename_modules()
    {
        var edited = FrontEndSwap.References("map: { 'olive': \"olive.mvc/dist\" },\nshim: { \"olive/olivePage\": [\"alertify\"] }\nrequirejs([\"app/appPage\", \"olive/olivePage\"]);\nif (window[\"IsOliveMvcLoaded\"] === undefined) return false;");
        await Assert.That(edited).Contains("'platform': \"../scripts/platform\"");
        await Assert.That(edited).Contains("\"platform/platformPage\": [\"alertify\"]");
        await Assert.That(edited).Contains("IsPlatformLoaded");
        await Assert.That(edited.ToLowerInvariant()).DoesNotContain("olive");
    }

    [Test]
    public async Task App_scripts_extend_the_new_page()
    {
        var edited = FrontEndSwap.Script("import OlivePage from 'olive/olivePage';\nimport { ConfirmBox } from \"olive/plugins/confirmBox\";\nexport default class AppPage extends OlivePage { }");
        await Assert.That(edited).IsEqualTo("import PlatformPage from 'platform/platformPage';\nimport { ConfirmBox } from \"platform/plugins/confirmBox\";\nexport default class AppPage extends PlatformPage { }");
    }

    [Test]
    public async Task Ships_every_module_and_no_olive_names()
    {
        var sources = FrontEndSwap.Sources();
        await Assert.That(sources.Select(s => s.Path)).Contains("platformPage.ts");
        await Assert.That(sources.Select(s => s.Path)).Contains("plugins/fileUpload.ts");
        await Assert.That(sources.Count).IsGreaterThan(45);
        foreach (var (path, text) in sources)
            await Assert.That(text.ToLowerInvariant().Contains("olive")).IsFalse().Because(path);
    }

    [Test]
    public async Task Plan_rewrites_a_website_and_skips_others()
    {
        var repo = Directory.CreateTempSubdirectory("frontend").FullName;
        var website = Path.Combine(repo, "Website");
        Directory.CreateDirectory(Path.Combine(website, "wwwroot", "scripts"));
        File.WriteAllText(Path.Combine(website, "package.json"), "{\n  \"dependencies\": {\n    \"olive.mvc\": \"x\"\n  }\n}");
        File.WriteAllText(Path.Combine(website, "tsconfig.json"), "{ \"paths\": { \"olive/*\": [ \"wwwroot/lib/olive.mvc/typings/*\" ] } }");
        File.WriteAllText(Path.Combine(website, "wwwroot", "scripts", "AppPage.ts"), "import OlivePage from 'olive/olivePage';");
        File.WriteAllText(Path.Combine(website, "wwwroot", "scripts", "Other.ts"), "export const x = 1;");

        var files = FrontEndSwap.Plan(repo, website).OfType<Eject.EditFile>().Select(c => c.Path).ToList();
        await Assert.That(files).Contains("Website/package.json");
        await Assert.That(files).Contains("Website/tsconfig.json");
        await Assert.That(files).Contains("Website/wwwroot/scripts/AppPage.ts");
        await Assert.That(files).Contains("Website/wwwroot/scripts/platform/platformPage.ts");
        await Assert.That(files).DoesNotContain("Website/wwwroot/scripts/Other.ts");

        Directory.CreateDirectory(Path.Combine(repo, "Plain"));
        await Assert.That(FrontEndSwap.Plan(repo, Path.Combine(repo, "Plain")).Any()).IsFalse();
        Directory.Delete(repo, recursive: true);
    }
}
