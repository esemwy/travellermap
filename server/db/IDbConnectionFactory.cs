#nullable enable
using System.Data;

namespace Maps.Database
{
    internal interface IDbConnectionFactory
    {
        // Returns an OPEN connection.
        IDbConnection CreateConnection();
        ISqlDialect Dialect { get; }
    }
}
