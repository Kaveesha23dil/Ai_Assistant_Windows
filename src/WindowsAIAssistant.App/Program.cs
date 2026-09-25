using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WindowsAIAssistant.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}