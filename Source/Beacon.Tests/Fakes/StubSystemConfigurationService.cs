using Beacon.Core.Services;

namespace Beacon.Tests.Fakes;

internal sealed class StubSystemConfigurationService : ISystemConfigurationService
{
    private readonly SystemConfig _config;

    public StubSystemConfigurationService(SystemConfig? config = null)
        => _config = config ?? new SystemConfig();

    public SystemConfig Get() => _config;
    public Task SaveAsync(SystemConfig config) => Task.CompletedTask;
}
