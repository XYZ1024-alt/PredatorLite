using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public sealed class DeferredFpsSource : IFpsSource
{
    private readonly Func<IFpsSource> _factory;
    private IFpsSource? _inner;

    public DeferredFpsSource(Func<IFpsSource> factory)
    {
        _factory = factory;
    }

    public bool IsRunning => _inner?.IsRunning == true;

    public double? FramesPerSecond => _inner?.FramesPerSecond;

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        _inner ??= _factory();
        return _inner.StartAsync(cancellationToken);
    }

    public Task StopAsync() => _inner?.StopAsync() ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_inner is not null)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _inner = null;
        }
    }
}
