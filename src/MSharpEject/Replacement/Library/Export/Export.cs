using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Platform.Entities;

namespace Platform.Export
{
    public enum Format { ExcelXml = 0, Csv = 1 }

    public class Cell
    {
        public Cell(string text = null) => Text = text;

        public string Text { get; set; }

        public string Type { get; set; }

        public CellStyle Style { get; set; } = new CellStyle();

        public Cell SetStyle(Action<CellStyle> setter)
        {
            setter(Style);
            return this;
        }

        public override string ToString() => Text;
    }

    public class CellStyle
    {
        public Dictionary<string, string> Settings = new Dictionary<string, string>();

        public Exporter.HorizentalAlignment Alignment { get; set; } = Exporter.HorizentalAlignment.Left;
        public Exporter.VerticalAlignment VerticalAlignment { get; set; } = Exporter.VerticalAlignment.Center;
        public Exporter.CellOrientation Orientation { get; set; } = Exporter.CellOrientation.Horizontal;
        public int FontSize { get; set; } = 10;
        public string BackgroundColor { get; set; }
        public string BorderColor { get; set; }
        public int BorderWidth { get; set; } = 1;
        public string FontName { get; set; } = "Arial";
        public string NumberFormat { get; set; }
        public bool Bold { get; set; }
        public bool WrapText { get; set; } = true;
        public bool Italic { get; set; }
        public string ForeColor { get; set; } = "#000000";

        public CellStyle Set(string key, string value)
        {
            Settings[key] = value;
            return this;
        }

        internal string Body()
        {
            var body = new StringBuilder();
            body.Append($"<Alignment ss:Horizontal=\"{Alignment}\" ss:Vertical=\"{VerticalAlignment}\" ss:Rotate=\"{(Orientation == Exporter.CellOrientation.Vertical ? 90 : 0)}\"{(WrapText ? " ss:WrapText=\"1\"" : "")}/>\n");
            body.Append($"<Font ss:FontName=\"{FontName}\" x:Family=\"Swiss\" ss:Size=\"{FontSize}\" ss:Color=\"{ForeColor}\" ss:Bold=\"{(Bold ? 1 : 0)}\" ss:Italic=\"{(Italic ? 1 : 0)}\" />\n");
            if (!string.IsNullOrEmpty(BackgroundColor)) body.Append($"<Interior ss:Color=\"{BackgroundColor}\" ss:Pattern=\"Solid\"/>\n");
            if (!string.IsNullOrEmpty(BorderColor))
            {
                body.Append("<Borders>\n");
                foreach (var side in new[] { "Bottom", "Left", "Right", "Top" })
                    body.Append($"<Border ss:Position=\"{side}\" ss:LineStyle=\"Continuous\" ss:Weight=\"{BorderWidth}\" ss:Color=\"{BorderColor}\"/>\n");
                body.Append("</Borders>\n");
            }
            if (!string.IsNullOrEmpty(NumberFormat)) body.Append($"<NumberFormat ss:Format=\"{NumberFormat}\" />\n");
            return body.ToString();
        }

        // Stable across processes, unlike string.GetHashCode().
        public string GetStyleId()
        {
            uint hash = 2166136261;
            foreach (var c in Body()) hash = (hash ^ c) * 16777619;
            return "s" + (int)hash;
        }

        public override bool Equals(object obj) => obj is CellStyle other && other.Body() == Body();

        public override int GetHashCode() => Body().GetHashCode();

        public static bool operator ==(CellStyle style1, CellStyle style2) => ReferenceEquals(style1, style2) || (!ReferenceEquals(style1, null) && style1.Equals(style2));

        public static bool operator !=(CellStyle style1, CellStyle style2) => !(style1 == style2);

        internal static CellStyle Header() => new CellStyle { BackgroundColor = "#DDDDDD", BorderColor = "#aaaaaa" };

        internal static CellStyle Group() => new CellStyle { Alignment = Exporter.HorizentalAlignment.Center, ForeColor = "#ffffff", Bold = true, BackgroundColor = "#777777" };
    }

    public class Column<TColumn>
    {
        internal string EnumerationName;
        internal object[] PossibleValues;

        public Column() { }

        public Column(string headerText, string dataType)
        {
            HeaderText = headerText;
            DataType = dataType;
        }

        public int? Width { get; set; }
        public CellStyle HeaderStyle { get; set; } = CellStyle.Header();
        public CellStyle GroupingStyle { get; set; } = CellStyle.Group();
        public CellStyle RowStyle { get; set; } = new CellStyle();
        public string HeaderText { get; set; }
        public string DataType { get; set; }
        public string Formula { get; set; }
        public string GroupName { get; set; }
        public Func<TColumn, object> Data { get; set; }

        public Column<TColumn> SetRowStyle(Action<CellStyle> setter)
        {
            setter(RowStyle);
            return this;
        }

        public Column<TColumn> SetHeaderStyle(Action<CellStyle> setter)
        {
            setter(HeaderStyle);
            return this;
        }

        public Column<TColumn> Customize(Action<Column<TColumn>> customisations)
        {
            customisations(this);
            return this;
        }
    }

    public class Exporter<T>
    {
        public List<Column<T>> Columns = new List<Column<T>>();
        public List<object[]> DataRows = new List<object[]>();

        public Exporter(string documentName) => DocumentName = documentName;

        public Exporter(DataTable dataTable)
        {
            DocumentName = dataTable.TableName;
            foreach (DataColumn column in dataTable.Columns) AddColumn(column.ColumnName, "String");
            foreach (DataRow row in dataTable.Rows) AddRow(row.ItemArray.Select(v => v is DBNull ? null : v).ToArray());
        }

        public static string LinkSeperator => "¦";

        public string DocumentName { get; set; }
        public string HeaderBackGroundColor { get; set; }
        public string HeaderFontName { get; set; }
        public string HeaderGroupBackgroundColor { get; set; }
        public bool FreezeHeader { get; set; }
        public bool FreezeFirstColumn { get; set; }
        public double DefaultColumnWidth { get; set; }
        public bool ExcludeHeader { get; set; }

        public Column<T> GetColumn(string headerText) => Columns.FirstOrDefault(c => c.HeaderText == headerText);

        public Column<T> AddColumn(string headerText) => AddColumn(headerText, "String");

        public Column<T> AddColumn(string headerText, string type) => AddColumn(headerText, type, null);

        public Column<T> AddColumn(string headerText, string type, Func<T, object> data)
        {
            var column = new Column<T>(headerText, type) { Data = data };
            Columns.Add(column);
            return column;
        }

        public Column<T> AddDropDownColumn(string headerText, string type, string enumerationName, IEnumerable<object> possibleValues)
        {
            var column = AddColumn(headerText, type);
            column.EnumerationName = enumerationName;
            column.PossibleValues = possibleValues.ToArray();
            return column;
        }

        public void RemoveColumn(string headerText) => RemoveColumn(GetColumn(headerText));

        public void RemoveColumn(Column<T> column) => RemoveColumn(Columns.IndexOf(column));

        public void RemoveColumn(int columnIndex)
        {
            if (columnIndex < 0) return;
            Columns.RemoveAt(columnIndex);
            for (var i = 0; i < DataRows.Count; i++)
                if (DataRows[i].Length > columnIndex) DataRows[i] = DataRows[i].Where((_, index) => index != columnIndex).ToArray();
        }

        public void AddRow(params object[] dataCells) => DataRows.Add(dataCells);

        public void AddRows(IEnumerable<T> dataItems)
        {
            var missing = Columns.FirstOrDefault(c => c.Data == null);
            if (missing != null) throw new Exception($"Column.Data should be specified for Exporter.AddRows() method to work. For '{missing.HeaderText}' it is null.");
            foreach (var item in dataItems) DataRows.Add(Columns.Select(c => c.Data(item)).ToArray());
        }

        public string GetFileExtension(Format output) => output == Format.Csv ? ".csv" : ".xls";

        public Blob ToDocument(Format type) => new Blob(Encoding.UTF8.GetBytes(Generate(type)), DocumentName + GetFileExtension(type));

        public string Generate(Format format) => format == Format.Csv ? GenerateCsv() : GenerateExcelXml(this);

        string GenerateCsv()
        {
            var csv = new StringBuilder();
            if (!ExcludeHeader) csv.AppendLine(string.Join(",", Columns.Select(c => Escape(c.HeaderText))));
            foreach (var row in DataRows) csv.AppendLine(string.Join(",", row.Select(v => Escape(v?.ToString()))));
            return csv.ToString();
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) > -1 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string GenerateExcelXml(params Exporter<T>[] sheets)
        {
            var styles = new List<CellStyle>();
            void Use(CellStyle style)
            {
                if (!styles.Contains(style)) styles.Add(style);
            }

            foreach (var sheet in sheets)
            {
                foreach (var column in sheet.Columns)
                {
                    Use(column.HeaderStyle);
                    Use(column.RowStyle);
                }
                if (sheet.Columns.Any(c => c.GroupName != null)) Use(sheet.Columns.First().GroupingStyle);
            }

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><?mso-application progid=\"Excel.Sheet\"?>\n");
            xml.Append("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"\nxmlns:o=\"urn:schemas-microsoft-com:office:office\"\nxmlns:x=\"urn:schemas-microsoft-com:office:excel\"\nxmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"\nxmlns:html=\"http://www.w3.org/TR/REC-html40\">\n");
            xml.Append("<Styles>\n<Style ss:ID=\"linkStyle\">\n<Font ss:Color=\"#0000FF\" ss:Underline=\"Single\"/>\n</Style>\n");
            foreach (var style in styles) xml.Append($"<Style ss:ID=\"{style.GetStyleId()}\">\n{style.Body()}</Style>\n\n");
            xml.Append("</Styles>\n\n<Names>");
            foreach (var column in sheets.SelectMany(s => s.Columns).Where(c => c.EnumerationName != null))
                xml.Append($"<NamedRange ss:Name=\"{column.EnumerationName}\" ss:RefersTo=\"={column.EnumerationName}!R1C1:R{column.PossibleValues.Length}C1\"/>");
            xml.Append("</Names>\n");
            foreach (var sheet in sheets) xml.Append(sheet.Worksheet());
            foreach (var column in sheets.SelectMany(s => s.Columns).Where(c => c.EnumerationName != null))
            {
                xml.Append($"<Worksheet ss:Name=\"{column.EnumerationName}\">\n<Table>\n");
                foreach (var value in column.PossibleValues)
                    xml.Append($"<Row><Cell><Data ss:Type=\"string\">{Xml(value?.ToString())}</Data><NamedCell ss:Name=\"{column.EnumerationName}\"/></Cell></Row>\n");
                xml.Append("</Table>\n<WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\">\n<Visible>SheetHidden</Visible>\n</WorksheetOptions>\n</Worksheet>\n");
            }
            xml.Append("</Workbook>\n");
            return xml.ToString();
        }

        static string Xml(string text) => System.Security.SecurityElement.Escape(text ?? string.Empty);

        string Worksheet()
        {
            var xml = new StringBuilder();
            xml.Append($"<Worksheet ss:Name=\"{Xml(DocumentName)}\">\n<Table DefaultColumnWidth=\"{DefaultColumnWidth}\">\n");
            xml.Append(string.Join("\n", Columns.Select((c, i) => c.Width.HasValue ? $"<Column ss:Index=\"{i + 1}\" ss:Width=\"{c.Width}\"/>" : null).Where(c => c != null))).Append('\n');

            if (Columns.Any(c => c.GroupName != null))
            {
                var groupStyle = Columns.First().GroupingStyle.GetStyleId();
                xml.Append("<Row>\n");
                for (var i = 0; i < Columns.Count;)
                {
                    var name = Columns[i].GroupName;
                    var count = Columns.Skip(i).TakeWhile(c => c.GroupName == name).Count();
                    xml.Append($"<Cell ss:StyleID=\"{groupStyle}\" ss:MergeAcross=\"{count - 1}\"><Data ss:Type=\"String\" >{Xml(name)}</Data></Cell>\n");
                    i += count;
                }
                xml.Append("</Row>\n");
            }

            if (!ExcludeHeader)
            {
                xml.Append("\n<Row>\n");
                foreach (var column in Columns)
                    xml.Append($"<Cell ss:StyleID=\"{column.HeaderStyle.GetStyleId()}\"><Data ss:Type=\"String\">{Xml(column.HeaderText)}</Data>\n</Cell>\n");
                xml.Append("</Row>\n");
            }

            xml.Append('\n');
            foreach (var row in DataRows)
            {
                xml.Append("<Row>\n");
                for (var i = 0; i < Columns.Count; i++)
                {
                    var column = Columns[i];
                    var value = i < row.Length ? row[i] : null;
                    var formula = column.Formula == null ? "" : $" ss:Formula=\"{Xml(column.Formula)}\"";
                    xml.Append(value == null
                        ? $"<Cell ss:StyleID=\"{column.RowStyle.GetStyleId()}\"{formula}></Cell>\n"
                        : $"<Cell ss:StyleID=\"{column.RowStyle.GetStyleId()}\"{formula}><Data ss:Type=\"{column.DataType}\">{Xml(value.ToString())}</Data>\n</Cell>\n");
                }
                xml.Append("</Row>\n");
            }
            xml.Append("\n</Table>\n");

            for (var i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].EnumerationName == null) continue;
                var lastRow = DataRows.Count + (ExcludeHeader ? 0 : 1);
                xml.Append($"<DataValidation xmlns=\"urn:schemas-microsoft-com:office:excel\">\n<Type>List</Type>\n<Range>R1C{i + 1}:R{lastRow}C{i + 1}</Range>\n<Value>{Columns[i].EnumerationName}</Value>\n</DataValidation>\n");
            }

            xml.Append("<WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\">\n<Unsynced/>\n<Selected/>\n<FreezePanes/>\n<FrozenNoSplit/>\n");
            if (FreezeHeader) xml.Append("<SplitHorizontal>1</SplitHorizontal><TopRowBottomPane>1</TopRowBottomPane>\n");
            if (FreezeFirstColumn) xml.Append("<SplitVertical>1</SplitVertical><LeftColumnRightPane>1</LeftColumnRightPane>\n");
            xml.Append("<ProtectObjects>False</ProtectObjects>\n<ProtectScenarios>False</ProtectScenarios>\n</WorksheetOptions>\n</Worksheet>\n\n");
            return xml.ToString();
        }
    }

    public class Exporter : Exporter<object>
    {
        public Exporter(string documentName) : base(documentName) { }

        public Exporter(DataTable dataTable) : base(dataTable) { }

        public enum CellOrientation { Vertical = 0, Horizontal = 1 }

        public enum HorizentalAlignment { Left = 0, Center = 1, Right = 2 }

        public enum VerticalAlignment { Top = 0, Center = 1, Bottom = 2 }

        public class Styles
        {
            public const string NumberFormat_Format = "NumberFormat.Format";
            public const string Alignment_Vertical = "Alignment.Vertical";
            public const string Alignment_Horizontal = "Alignment.Horizontal";
            public const string Font_FontName = "Font.FontName";
            public const string Font_Bold = "Font.Bold";
            public const string Font_Italic = "Font.Italic";
            public const string Font_Size = "Font.Size";
            public const string Font_Color = "Font.Color";
            public const string Interior_Color = "Interior.Color";
            public const string Interior_Pattern = "Interior.Pattern";
        }
    }

    public static class ExtensionMethods
    {
        public static async Task<FileInfo> ToCsvFile(this Platform.Entities.Data.DataAccessProfiler.ReportRow[] lines)
        {
            var exporter = new Exporter<Platform.Entities.Data.DataAccessProfiler.ReportRow>("Sql Profile Report");
            exporter.AddColumn("Command", "String", l => l.Command);
            exporter.AddColumn("Calls", "Number", l => l.Calls);
            exporter.AddColumn("Total ms", "Number", l => l.Total);
            exporter.AddColumn("Average ms", "Number", l => l.Average);
            exporter.AddColumn("Median ms", "Number", l => l.Median);
            exporter.AddColumn("Longest ms", "Number", l => l.Longest);
            exporter.AddRows(lines);
            var file = new FileInfo(Path.Combine(Path.GetTempPath(), "Sql.Profile." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"));
            await Task.Run(() => File.WriteAllText(file.FullName, exporter.Generate(Format.Csv)));
            return file;
        }
    }
}
