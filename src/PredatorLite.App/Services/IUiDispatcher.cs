using System.Windows.Threading;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public interface IUiDispatcher
{
    void Post(Func<Task> callback);
}

public sealed class WpfUiDispatcher(Dispatcher dispatcher, IAppLogger logger) : IUiDispatcher
{
    public void Post(Func<Task> callback)
    {
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await callback();
            }
            catch (Exception exception)
            {
                logger.Error("Dispatched UI action failed", exception);
            }
        });
    }
}
