using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Platform.Entities;

namespace Platform.Csv
{
    public static class CsvReader
    {
        public static async Task<DataTable> ReadAsync(Blob csv, bool isFirstRowHeaders, int minimumFieldCount = 0) =>
            Read(await csv.GetContentTextAsync(), isFirstRowHeaders, minimumFieldCount);

        public static async Task<DataTable> ReadAsync(FileInfo csv, bool isFirstRowHeaders, int minimumFieldCount = 0) =>
            Read(await Task.Run(() => File.ReadAllText(csv.FullName)), isFirstRowHeaders, minimumFieldCount);

        public static DataTable Read(string csvContent, bool isFirstRowHeaders, int minimumFieldCount = 0)
        {
            using (var reader = new CsvHelper.CsvReader(new StringReader(csvContent), CultureInfo.InvariantCulture))
            {
                reader.Read();
                reader.ReadHeader();
                var headers = reader.HeaderRecord;
                var table = new DataTable();
                var count = Math.Max(headers.Length, minimumFieldCount);
                for (var i = 0; i < count; i++) table.Columns.Add(isFirstRowHeaders ? headers[i] : "Column" + i);

                if (!isFirstRowHeaders) table.Rows.Add(headers.Take(count).Cast<object>().ToArray());
                while (reader.Read())
                {
                    var row = table.NewRow();
                    for (var i = 0; i < count; i++) row[i] = reader.GetField(i);
                    table.Rows.Add(row);
                }
                return table;
            }
        }

        public static async Task<string[]> GetColumnsAsync(Blob blob) => GetColumns(await blob.GetContentTextAsync());

        public static async Task<string[]> GetColumnsAsync(FileInfo file) => GetColumns(await Task.Run(() => File.ReadAllText(file.FullName)));

        public static string[] GetColumns(string csvContent)
        {
            using (var reader = new CsvHelper.CsvReader(new StringReader(csvContent), CultureInfo.InvariantCulture))
            {
                reader.Read();
                reader.ReadHeader();
                return reader.HeaderRecord;
            }
        }
    }

    public static class CsvWriter
    {
        public static void Save(this string @this, FileInfo path) => File.WriteAllText(path.FullName, @this);

        // Unescaped, as in Olive: a comma in a value makes an extra column.
        public static string ToCsv(this Dictionary<string, string> @this) =>
            string.Concat(@this.Select(pair => pair.Key + "," + pair.Value + "\r\n"));

        public static string ToCsv<T>(this IEnumerable<T> @this) where T : class
        {
            using (var text = new StringWriter())
            using (var writer = new CsvHelper.CsvWriter(text, new CsvConfiguration(CultureInfo.InvariantCulture) { ShouldQuote = a => a.FieldType == typeof(string) || string.IsNullOrEmpty(a.Field) }))
            {
                writer.WriteRecords(@this);
                writer.Flush();
                return text.ToString();
            }
        }

        public static string ToCsv(this DataTable @this)
        {
            using (var text = new StringWriter())
            using (var writer = new CsvHelper.CsvWriter(text, CultureInfo.InvariantCulture))
            {
                foreach (DataColumn column in @this.Columns) writer.WriteField(column.ColumnName);
                writer.NextRecord();
                foreach (DataRow row in @this.Rows)
                {
                    foreach (var value in row.ItemArray) writer.WriteField(value is DBNull ? null : value);
                    writer.NextRecord();
                }
                writer.Flush();
                return text.ToString();
            }
        }
    }
}
