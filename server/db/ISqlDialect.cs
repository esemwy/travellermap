#nullable enable
using System.Collections.Generic;
using System.Data;

namespace Maps.Database
{
    // Abstracts SQL syntax differences between SQL Server and MariaDB.
    internal interface ISqlDialect
    {
        // "SELECT DISTINCT TOP n ... FROM (SELECT TOP n ...) AS TT" pattern.
        string FormatDistinctTopQuery(string distinctFields, string allFields,
            string table, string where, string orderBy, int limit);

        // Random-order expression used in ORDER BY clause.
        string RandomOrder { get; }

        // DDL to drop a table only if it exists.
        string DropTableIfExists(string tableName);

        // DDL to create a non-clustered / plain index.
        string CreateIndex(string indexName, string tableName, string column);

        // Column type for fixed-length string (nchar on SQL Server, char on MariaDB).
        string CharType(int n);

        // Column type for variable-length string (nvarchar on SQL Server, varchar on MariaDB).
        string VarCharType(int n);

        // Insert rows in batches using the provided open connection.
        void BulkInsert(IDbConnection connection, string tableName,
            string[] columns, IEnumerable<object?[]> rows, int batchSize = 1000);
    }
}
