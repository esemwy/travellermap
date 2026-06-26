#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using MySqlConnector;

namespace Maps.Database
{
    internal sealed class MariaDbDialect : ISqlDialect
    {
        public string RandomOrder => "RAND()";

        public string FormatDistinctTopQuery(string distinctFields, string allFields,
            string table, string where, string orderBy, int limit) =>
            $"SELECT DISTINCT {distinctFields} FROM " +
            $"(SELECT {allFields} FROM {table} WHERE {where} {orderBy} LIMIT {limit}) AS TT LIMIT {limit}";

        public string DropTableIfExists(string tableName) =>
            $"DROP TABLE IF EXISTS {tableName}";

        public string CreateIndex(string indexName, string tableName, string column) =>
            $"CREATE INDEX {indexName} ON {tableName} ( {column} )";

        public string CharType(int n) => $"char({n})";

        public string VarCharType(int n) => $"varchar({n})";

        public void BulkInsert(IDbConnection connection, string tableName,
            string[] columns, IEnumerable<object?[]> rows, int batchSize = 1000)
        {
            if (connection is not MySqlConnection mysqlConn)
                throw new InvalidOperationException("MariaDbDialect requires MySqlConnection.");

            foreach (var batch in rows.Chunk(batchSize))
            {
                if (batch.Length == 0) continue;

                var sql = new StringBuilder();
                sql.Append($"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ");

                using var cmd = new MySqlCommand("", mysqlConn);
                var rowParts = new List<string>();

                for (int r = 0; r < batch.Length; r++)
                {
                    var paramParts = new List<string>();
                    for (int c = 0; c < columns.Length; c++)
                    {
                        string pname = $"@p{r}_{c}";
                        paramParts.Add(pname);
                        cmd.Parameters.AddWithValue(pname, batch[r][c] ?? DBNull.Value);
                    }
                    rowParts.Add("(" + string.Join(", ", paramParts) + ")");
                }

                sql.Append(string.Join(", ", rowParts));
                cmd.CommandText = sql.ToString();
                cmd.ExecuteNonQuery();
            }
        }
    }

    internal sealed class MariaDbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        public ISqlDialect Dialect { get; } = new MariaDbDialect();

        public MariaDbConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
