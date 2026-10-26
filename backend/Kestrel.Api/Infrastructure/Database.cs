using System.Reflection;
using DbUp;
using MySqlConnector;

namespace Kestrel.Api.Infrastructure;

public static class Database
{
    public static void Migrate(string connectionString)
    {
        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .WithVariablesDisabled()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("Database migration failed.", result.Error);
    }
}
