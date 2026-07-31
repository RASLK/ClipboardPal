using ClipboardPal.Core.Abstractions;

namespace ClipboardPal.Platform;

public sealed class AvaloniaUiDispatcher(Avalonia.Threading.Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) =>
        dispatcher.Post(action, Avalonia.Threading.DispatcherPriority.Normal);

    public Task InvokeAsync(Action action) =>
        dispatcher.InvokeAsync(action).GetTask();

    public bool CheckAccess() => dispatcher.CheckAccess();
}
