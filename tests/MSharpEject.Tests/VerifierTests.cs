namespace MSharpEject.Tests;

public class VerifierTests
{
    static VerifyResult Result(bool built = true, bool hasTests = true, int exit = 0, int passed = 3, params string[] failed) =>
        new(built, hasTests, exit, failed.ToHashSet(), passed);

    [Test]
    public async Task Compare_passes_when_nothing_new_fails()
    {
        await Assert.That(Verifier.Compare(Result(), Result())).IsNull();
        await Assert.That(Verifier.Compare(Result(exit: 1, failed: "Old"), Result(exit: 1, failed: "Old"))).IsNull();
        await Assert.That(Verifier.Compare(Result(), Result(hasTests: false))).IsNull();
    }

    [Test]
    public async Task Compare_flags_broken_build_and_new_failures()
    {
        await Assert.That(Verifier.Compare(Result(), Result(built: false))).Contains("doesn't build");
        await Assert.That(Verifier.Compare(Result(failed: "Old"), Result(exit: 1, failed: ["Old", "New"]))).IsEqualTo("1 tests fail now that passed before: New");
        await Assert.That(Verifier.Compare(Result(), Result(exit: 1))).Contains("no failures were reported");
    }

    [Test]
    public async Task ReadTrx_counts_outcomes()
    {
        using var dir = new TempDir();
        var trx = dir.Write("a.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="A" outcome="Passed" />
                <UnitTestResult testName="B" outcome="Failed" />
                <UnitTestResult testName="C" outcome="NotExecuted" />
                <UnitTestResult testName="D" outcome="Passed" />
              </Results>
            </TestRun>
            """);

        var (failed, passed) = Verifier.ReadTrx([trx]);

        await Assert.That(passed).IsEqualTo(2);
        await Assert.That(failed).IsEquivalentTo(["B"]);
    }

    [Test]
    [Arguments("<PackageReference Include=\"Microsoft.NET.Test.Sdk\" />", true)]
    [Arguments("<PackageReference Include=\"NUnit\" />", true)]
    [Arguments("<IsTestProject>true</IsTestProject>", true)]
    [Arguments("<PackageReference Include=\"Olive\" />", false)]
    public async Task IsTestProject(string xml, bool expected) =>
        await Assert.That(Verifier.IsTestProject(xml)).IsEqualTo(expected);

    [Test]
    public async Task FindSolution_prefers_slnx()
    {
        using var dir = new TempDir();
        dir.Write("B.sln", "");
        dir.Write("A.slnx", "<Solution />");

        await Assert.That(Path.GetFileName(Verifier.FindSolution(dir.Path))).IsEqualTo("A.slnx");
    }
}
