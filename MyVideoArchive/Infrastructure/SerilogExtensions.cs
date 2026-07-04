using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

namespace MyVideoArchive.Infrastructure;

/// <summary>
/// Database-backed Serilog sinks. The relational sink is chosen from
/// <see cref="Constants.DatabaseProviders"/> so logs land in the same database engine EF uses.
/// </summary>
internal static class SerilogExtensions
{
    private const string LogTableName = "Log";

    /// <summary>
    /// Adds a relational log sink using the same connection string as the application database,
    /// picking the sink that matches <c>Database:Provider</c> (defaults to PostgreSQL).
    /// </summary>
    public static LoggerConfiguration WriteToMvaDatabase(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("DefaultConnection");
        string provider = configuration["Database:Provider"] ?? Constants.DatabaseProviders.Npgsql;

        return string.IsNullOrWhiteSpace(connectionString)
            ? loggerConfiguration
            : provider switch
            {
                Constants.DatabaseProviders.Npgsql => loggerConfiguration.WriteTo.PostgreSQL(
                    connectionString,
                    LogTableName,
                    PostgreSqlColumnWriters(),
                    needAutoCreateTable: true),

                Constants.DatabaseProviders.SqlServer => loggerConfiguration.WriteTo.MSSqlServer(
                    connectionString,
                    new MSSqlServerSinkOptions
                    {
                        TableName = LogTableName,
                        AutoCreateSqlTable = true,
                    },
                    columnOptions: new ColumnOptions()),

                Constants.DatabaseProviders.Sqlite => loggerConfiguration.WriteTo.SQLite(
                    SqliteDatabasePath(connectionString),
                    tableName: LogTableName,
                    restrictedToMinimumLevel: LogEventLevel.Information),

                Constants.DatabaseProviders.MySql => loggerConfiguration.WriteTo.MySQL(
                    connectionString,
                    tableName: LogTableName,
                    restrictedToMinimumLevel: LogEventLevel.Information),

                _ => loggerConfiguration,
            };
    }

    private static Dictionary<string, ColumnWriterBase> PostgreSqlColumnWriters() =>
        new()
        {
            { "message", new RenderedMessageColumnWriter() },
            { "message_template", new MessageTemplateColumnWriter() },
            { "level", new LevelColumnWriter() },
            { "timestamp", new TimestampColumnWriter() },
            { "exception", new ExceptionColumnWriter() },
            { "properties", new LogEventSerializedColumnWriter() },
        };

    private static string SqliteDatabasePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return builder.DataSource;
    }
}
