using System.Data.SqlClient;
using System.Linq;
using Platform.Entities.Data;

namespace Platform.Entities.Data
{
    public class SqlServerSqlCommandGenerator : SqlCommandGenerator
    {
        public override string SafeId(string id) => id.StartsWith("[") ? id : $"[{id}]";

        public override string UnescapeId(string id) => id.Trim('[', ']');

        public override string GenerateSelectCommand(IDatabaseQuery iquery, string tables, string fields) =>
            $"SELECT{(iquery.TakeTop.HasValue && iquery.PageSize == null ? " TOP " + iquery.TakeTop : "")} {fields} FROM {tables}{GenerateWhere(iquery)}{GenerateSort(iquery)}{GeneratePagination(iquery)}";

        public override string GenerateSort(IDatabaseQuery query) => base.GenerateSort(query);

        public override string GeneratePagination(IDatabaseQuery query)
        {
            if (query.PageSize == null) return string.Empty;
            var sort = query.OrderByParts.Count == 0 ? $" ORDER BY {query.Column("ID")}" : "";
            return $"{sort} OFFSET {query.PageStartIndex} ROWS FETCH NEXT {query.PageSize} ROWS ONLY";
        }
    }

    public static class SqlDataAccessExtensions
    {
        public static DataAccessOptions SqlServer(this DataAccessOptions @this) => @this.Add<SqlConnection, SqlServerSqlCommandGenerator>();
    }

    public class SqlServerManager : DatabaseServer
    {
        string MasterConnectionString => new SqlConnectionStringBuilder(DataAccess.GetCurrentConnectionString()) { InitialCatalog = "master" }.ToString();

        void Run(string sql, string database = null)
        {
            var builder = new SqlConnectionStringBuilder(MasterConnectionString);
            if (database != null) builder.InitialCatalog = database;
            using (var connection = new SqlConnection(builder.ToString()))
            {
                connection.Open();
                // GO separates batches in scripts but isn't SQL.
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Where(b => b.Trim().Length > 0))
                    using (var cmd = new SqlCommand(batch, connection)) cmd.ExecuteNonQuery();
            }
        }

        public override void Delete(string databaseName)
        {
            ClearConnectionPool();
            Run($"IF DB_ID('{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END");
        }

        public override void Execute(string sql, string database = null) => Run(sql, database);

        public void Detach(string databaseName) => Run($"EXEC sp_detach_db '{databaseName}', 'true'");

        public override void ClearConnectionPool() => SqlConnection.ClearAllPools();

        public override bool Exists(string database, string filePath)
        {
            using (var connection = new SqlConnection(MasterConnectionString))
            {
                connection.Open();
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", connection))
                {
                    cmd.Parameters.AddWithValue("@name", database);
                    return (int)cmd.ExecuteScalar() > 0;
                }
            }
        }
    }
}
