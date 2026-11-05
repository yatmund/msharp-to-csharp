using System.Data;

namespace CompatTests;

public class CsvParity
{
    public class Row { public string Name { get; set; } public int? Age { get; set; } public string Note { get; set; } }

    static readonly string[] Inputs = ["A,B\n1,2\n3,4", "A,B\r\n\"x,y\",\"q\"\"q\"\r\n", "A,B\n1\n2,3,4", "A, B \n 1 , 2 ", "", "A,B\n\n1,2\n", "A;B\n1;2", "Name\nBob"];

    public static IEnumerable<int> Cases() => Enumerable.Range(0, Inputs.Length);

    static string Run(Func<object> f)
    {
        try { return f() is DataTable t ? Show(t) : f() is string[] a ? string.Join("|", a) : f()?.ToString(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string Show(DataTable t) =>
        string.Join("|", t.Columns.Cast<DataColumn>().Select(c => c.ColumnName)) + " :: " +
        string.Join(" / ", t.Rows.Cast<DataRow>().Select(r => string.Join("|", r.ItemArray.Select(v => v is DBNull ? "NULL" : $"<{v}>"))));

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task Read_matches(int index)
    {
        var csv = Inputs[index];
        foreach (var (header, min) in new[] { (true, 0), (false, 0), (true, 1), (true, 3) })
            await Assert.That(Run(() => Platform.Csv.CsvReader.Read(csv, header, min))).IsEqualTo(Run(() => Olive.Csv.CsvReader.Read(csv, header, min)));
        await Assert.That(Run(() => Platform.Csv.CsvReader.GetColumns(csv))).IsEqualTo(Run(() => Olive.Csv.CsvReader.GetColumns(csv)));
    }

    static async Task Same(string ours, string theirs)
    {
        static string Escape(string s) => s?.Replace("\r", "\\r").Replace("\n", "\\n");
        if (ours != theirs) Assert.Fail("ours:  " + Escape(ours) + "\nOlive: " + Escape(theirs));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Writing_matches()
    {
        var items = new[] { new Row { Name = "Bob", Age = 3, Note = "a,b" }, new Row { Name = "Al \"x\"", Age = null, Note = "line1\nline2" } };
        await Same(Platform.Csv.CsvWriter.ToCsv(items), Olive.Csv.CsvWriter.ToCsv(items));

        var dict = new Dictionary<string, string> { ["a"] = "1", ["b"] = "x,y", ["c"] = null };
        await Same(Platform.Csv.CsvWriter.ToCsv(dict), Olive.Csv.CsvWriter.ToCsv(dict));

        var table = new DataTable();
        table.Columns.Add("A");
        table.Columns.Add("B c");
        table.Columns.Add("N", typeof(int));
        table.Rows.Add("1", "two, three", 5);
        table.Rows.Add(null, "q\"q", DBNull.Value);
        await Same(Platform.Csv.CsvWriter.ToCsv(table), Olive.Csv.CsvWriter.ToCsv(table));
    }
}
