using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using PingMonitor.Web.Data;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services;

internal sealed class DynamicPingMonitorDbContextFactory : IDbContextFactory<PingMonitorDbContext>
{
    private readonly IStartupDatabaseConfigurationStore _configurationStore;
    private readonly StartupGateOptions _startupGateOptions;
    private readonly DbActivityCommandInterceptor _dbActivityCommandInterceptor;

    public DynamicPingMonitorDbContextFactory(
        IStartupDatabaseConfigurationStore configurationStore,
        Microsoft.Extensions.Options.IOptions<StartupGateOptions> startupGateOptions,
        DbActivityCommandInterceptor dbActivityCommandInterceptor)
    {
        _configurationStore = configurationStore;
        _startupGateOptions = startupGateOptions.Value;
        _dbActivityCommandInterceptor = dbActivityCommandInterceptor;
    }

    public PingMonitorDbContext CreateDbContext()
    {
        var configuration = _configurationStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var password = configuration is null
            ? null
            : _configurationStore.LoadPasswordAsync(CancellationToken.None).GetAwaiter().GetResult();
        var connectionString = configuration is not null && configuration.IsComplete && !string.IsNullOrWhiteSpace(password)
            ? _configurationStore.BuildConnectionString(configuration, password)
            : $"Server=localhost;Port={_startupGateOptions.DefaultMySqlPort};Database=pingmonitor_placeholder;User ID=placeholder;Password=placeholder;SslMode=Preferred;AllowPublicKeyRetrieval=True";

        var optionsBuilder = new DbContextOptionsBuilder<PingMonitorDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        optionsBuilder.AddInterceptors(_dbActivityCommandInterceptor);
        return new PingMonitorDbContext(optionsBuilder.Options);
    }

    public Task<PingMonitorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}
