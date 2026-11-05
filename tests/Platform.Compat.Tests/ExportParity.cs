using System.Data;
using System.Text.RegularExpressions;

namespace CompatTests;

public class ExportParity
{
    public class Row { public string Name { get; set; } public int? Age { get; set; } }

    // Style IDs are hashes; they only have to be consistent within a document.
    static string Normal(string xml)
    {
        var ids = new Dictionary<string, string>();
        xml = Regex.Replace(xml, "\"s-?\\d+\"", m => ids.TryGetValue(m.Value, out var id) ? id : ids[m.Value] = "\"s" + ids.Count + "\"");
        return Regex.Replace(xml, @"\s+", " ").Replace("> <", "><").Trim();
    }

    static void Same(string ours, string theirs)
    {
        if (ours == theirs) return;
        var at = 0;
        while (at < Math.Min(ours.Length, theirs.Length) && ours[at] == theirs[at]) at++;
        var from = Math.Max(0, at - 80);
        Assert.Fail($"differs at {at}\nours:  ...{ours.Substring(from, Math.Min(200, ours.Length - from))}\nOlive: ...{theirs.Substring(from, Math.Min(200, theirs.Length - from))}");
    }

    [Test]
    public async Task Simple_export_matches()
    {
        var items = new[] { new Row { Name = "Bob & <Al>", Age = 3 }, new Row { Name = "Sue", Age = null } };

        var theirs = new Olive.Export.Exporter<Row>("People");
        theirs.AddColumn("Name", "string", r => r.Name);
        theirs.AddColumn("Age", "number", r => r.Age).SetHeaderStyle(s => s.Bold = true);
        theirs.AddRows(items);

        var ours = new Platform.Export.Exporter<Row>("People");
        ours.AddColumn("Name", "string", r => r.Name);
        ours.AddColumn("Age", "number", r => r.Age).SetHeaderStyle(s => s.Bold = true);
        ours.AddRows(items);

        Same(ours.Generate(Platform.Export.Format.Csv), theirs.Generate(Olive.Export.Format.Csv));
        Same(Normal(ours.Generate(Platform.Export.Format.ExcelXml)), Normal(theirs.Generate(Olive.Export.Format.ExcelXml)));
        await Assert.That(ours.GetFileExtension(Platform.Export.Format.ExcelXml)).IsEqualTo(theirs.GetFileExtension(Olive.Export.Format.ExcelXml));
    }

    [Test]
    public async Task Styled_grouped_dropdown_export_matches()
    {
        var theirs = new Olive.Export.Exporter<Row>("People") { FreezeHeader = false, FreezeFirstColumn = true, DefaultColumnWidth = 80 };
        var tc = theirs.AddColumn("Name", "string", r => r.Name);
        tc.Width = 120;
        tc.GroupName = "G1";
        tc.SetRowStyle(s => { s.Italic = true; s.Alignment = Olive.Export.Exporter.HorizentalAlignment.Right; s.BackgroundColor = "#eeeeee"; s.NumberFormat = "0.00"; s.FontSize = 12; s.ForeColor = "#123456"; s.BorderColor = "#000"; s.BorderWidth = 2; s.WrapText = false; s.Orientation = Olive.Export.Exporter.CellOrientation.Vertical; s.VerticalAlignment = Olive.Export.Exporter.VerticalAlignment.Top; s.FontName = "Calibri"; });
        theirs.AddColumn("Link", "string", r => "http://x.com|Click").GroupName = "G1";
        theirs.AddColumn("When", "DateTime", r => new DateTime(2024, 1, 2, 3, 4, 5));
        theirs.AddColumn("Ok", "Boolean", r => true);
        theirs.AddDropDownColumn("Pick", "string", "Choices", new object[] { "A", "B" }).Data = r => "B";
        theirs.AddRows(new[] { new Row { Name = "Bob" } });
        theirs.AddRow("Manual", "y", null, false, "A");

        var ours = new Platform.Export.Exporter<Row>("People") { FreezeHeader = false, FreezeFirstColumn = true, DefaultColumnWidth = 80 };
        var oc = ours.AddColumn("Name", "string", r => r.Name);
        oc.Width = 120;
        oc.GroupName = "G1";
        oc.SetRowStyle(s => { s.Italic = true; s.Alignment = Platform.Export.Exporter.HorizentalAlignment.Right; s.BackgroundColor = "#eeeeee"; s.NumberFormat = "0.00"; s.FontSize = 12; s.ForeColor = "#123456"; s.BorderColor = "#000"; s.BorderWidth = 2; s.WrapText = false; s.Orientation = Platform.Export.Exporter.CellOrientation.Vertical; s.VerticalAlignment = Platform.Export.Exporter.VerticalAlignment.Top; s.FontName = "Calibri"; });
        ours.AddColumn("Link", "string", r => "http://x.com|Click").GroupName = "G1";
        ours.AddColumn("When", "DateTime", r => new DateTime(2024, 1, 2, 3, 4, 5));
        ours.AddColumn("Ok", "Boolean", r => true);
        ours.AddDropDownColumn("Pick", "string", "Choices", new object[] { "A", "B" }).Data = r => "B";
        ours.AddRows(new[] { new Row { Name = "Bob" } });
        ours.AddRow("Manual", "y", null, false, "A");

        Same(ours.Generate(Platform.Export.Format.Csv), theirs.Generate(Olive.Export.Format.Csv));
        Same(Normal(ours.Generate(Platform.Export.Format.ExcelXml)), Normal(theirs.Generate(Olive.Export.Format.ExcelXml)));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Table_formula_and_frozen_header_exports_match()
    {
        var dt = new DataTable();
        dt.Columns.Add("X");
        dt.Columns.Add("Y", typeof(int));
        dt.Rows.Add("a,b", 1);
        Same(new Platform.Export.Exporter(dt).Generate(Platform.Export.Format.Csv), new Olive.Export.Exporter(dt).Generate(Olive.Export.Format.Csv));
        Same(Normal(new Platform.Export.Exporter(dt).Generate(Platform.Export.Format.ExcelXml)), Normal(new Olive.Export.Exporter(dt).Generate(Olive.Export.Format.ExcelXml)));

        var theirs = new Olive.Export.Exporter("Doc") { FreezeHeader = true };
        theirs.AddColumn("A", "Number").GroupName = "Grp";
        theirs.AddColumn("F", "Number").Formula = "=RC[-1]*2";
        theirs.AddColumn("L", "link");
        theirs.AddRow(1.5, null, "http://x.com|Click");
        theirs.AddRow(2, 4, "plain");

        var ours = new Platform.Export.Exporter("Doc") { FreezeHeader = true };
        ours.AddColumn("A", "Number").GroupName = "Grp";
        ours.AddColumn("F", "Number").Formula = "=RC[-1]*2";
        ours.AddColumn("L", "link");
        ours.AddRow(1.5, null, "http://x.com|Click");
        ours.AddRow(2, 4, "plain");

        Same(ours.Generate(Platform.Export.Format.Csv), theirs.Generate(Olive.Export.Format.Csv));
        Same(Normal(ours.Generate(Platform.Export.Format.ExcelXml)), Normal(theirs.Generate(Olive.Export.Format.ExcelXml)));

        var e3 = new Platform.Export.Exporter("Ex") { ExcludeHeader = true };
        e3.AddColumn("A");
        e3.AddRow("q\"q, r");
        var t3 = new Olive.Export.Exporter("Ex") { ExcludeHeader = true };
        t3.AddColumn("A");
        t3.AddRow("q\"q, r");
        Same(e3.Generate(Platform.Export.Format.Csv), t3.Generate(Olive.Export.Format.Csv));
        Same(Normal(e3.Generate(Platform.Export.Format.ExcelXml)), Normal(t3.Generate(Olive.Export.Format.ExcelXml)));

        Same(Normal(Platform.Export.Exporter<object>.GenerateExcelXml(new Platform.Export.Exporter("S1"), new Platform.Export.Exporter("S2"))),
            Normal(Olive.Export.Exporter<object>.GenerateExcelXml(new Olive.Export.Exporter("S1"), new Olive.Export.Exporter("S2"))));
        await Task.CompletedTask;
    }
}
