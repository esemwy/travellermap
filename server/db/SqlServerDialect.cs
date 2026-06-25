#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Maps.Database
{
    internal sealed class SqlServerDialect : ISqlDialect
    {
        public string RandomOrder => "NEWID()";

        public string FormatDistinctTopQuery(string distinctFields, string allFields,
            string table, string where, string orderBy, int limit) =>
            $"SELECT DISTINCT TOP {limit} {distinctFields} FROM " +
            $"(SELECT TOP {limit} {allFields} FROM {table} WHERE {where} {orderBy}) AS TT";

        public string DropTableIfExists(string tableName) =>
            $"IF EXISTS(SELECT 1 FROM sys.objects WHERE OBJECT_ID = OBJECT_ID(N'{tableName}') " +
            $"AND type = (N'U')) DROP TABLE {tableName}";

        public string CreateIndex(string indexName, string tableName, string column) =>
            $"CREATE NONCLUSTERED INDEX {indexName} ON {tableName} ( {column} ASC )";

        public string CharType(int n) => $"nchar({n})";

        public string VarCharType(int n) => $"nvarchar({n})";

        public void BulkInsert(IDbConnection connection, string tableName,
            string[] columns, IEnumerable<object?[]> rows, int batchSize = 1000)
        {
            if (connection is not SqlConnection sqlConn)
                throw new InvalidOperationException("SqlServerDialect requires SqlConnection.");

            foreach (var batch in rows.Chunk(batchSize))
            {
                var sql = new StringBuilder();
                sql.Append($"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ");

                var allParams = new List<SqlParameter>();
                var rowParts = new List<string>();

                for (int r = 0; r < batch.Length; r++)
                {
                    var paramParts = new List<string>();
                    for (int c = 0; c < columns.Length; c++)
                    {
                        string pname = $"@p{r}_{c}";
                        paramParts.Add(pname);
                        allParams.Add(new SqlParameter(pname, batch[r][c] ?? DBNull.Value));
                    }
                    rowParts.Add("(" + string.Join(", ", paramParts) + ")");
                }

                sql.Append(string.Join(", ", rowParts));

                using var cmd = new SqlCommand(sql.ToString(), sqlConn);
                cmd.Parameters.AddRange(allParams.ToArray());
                cmd.ExecuteNonQuery();
            }
        }
    }

    internal sealed class SqlServerConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        public ISqlDialect Dialect { get; } = new SqlServerDialect();

        public SqlServerConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
