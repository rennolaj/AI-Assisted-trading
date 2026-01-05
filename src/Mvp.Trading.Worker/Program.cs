using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;

namespace Mvp.Trading.Worker;

/// <summary>
/// Entry point for the alert processing worker.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.Configure<WorkerOptions>(options =>
        {
            var redisSection = builder.Configuration.GetSection("Redis");
            options.RedisConnectionString = redisSection["ConnectionString"] ?? string.Empty;
            options.AlertQueueKey = redisSection["AlertQueueKey"] ?? "mvp:alerts";
            options.PollIntervalMs = int.TryParse(builder.Configuration["Worker:PollIntervalMs"], out var ms) ? ms : 500;
        });

        builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var connectionString = builder.Configuration.GetSection("Postgres")["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Postgres connection string is required (Postgres:ConnectionString).");
            }

            return new NpgsqlDataSourceBuilder(connectionString).Build();
        });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            {
                throw new InvalidOperationException("Redis connection string is required (Redis:ConnectionString)." );
            }

            return ConnectionMultiplexer.Connect(options.RedisConnectionString);
        });

        builder.Services.AddSingleton<IAlertProcessingStore, PostgresAlertProcessingStore>();
        builder.Services.AddHostedService<AlertWorker>();

        await builder.Build().RunAsync();
    }
}
