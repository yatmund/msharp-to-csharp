namespace CompatTests;

// Depends on today's date, so fixed dates in MatchesOlive can't cover it.
public class FriendlyDateParity
{
    public static IEnumerable<int> Offsets() => Enumerable.Range(-20, 41);

    [Test]
    [MethodDataSource(nameof(Offsets))]
    public async Task Matches_relative_to_today(int days)
    {
        var date = DateTime.Today.AddDays(days).AddHours(8.5);
        await Assert.That(Platform.Extensions.ToFriendlyDateString(date)).IsEqualTo(Olive.OliveExtensions.ToFriendlyDateString(date));
    }
}
