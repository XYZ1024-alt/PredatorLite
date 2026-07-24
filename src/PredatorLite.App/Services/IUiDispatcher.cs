using Microsoft.UI.Dispatching;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public interface IUiDispatcher
{
    void Post(Func<Task> callback);
}

public sealed class WinUiDispatcher(DispatcherQueue dispatcher, IAppLogger logger) : IUiDispatcher
{
    public void Post(Func<Task> callback)
    {
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await callback();
            }
            catch (Exception exception)
            {
                logger.LogError("Dispatched UI action failed", exception);
            }
        }))
        {
            logger.LogError("The UI dispatcher rejected an action.");
        }
    }
}
