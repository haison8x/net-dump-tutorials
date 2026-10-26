namespace CrashLab;

public sealed class NotificationSender
{
    // BUG: async void - exceptions cannot be observed by the caller and are
    // re-thrown on the SynchronizationContext / thread pool, crashing the process.
    public async void SendWelcomeEmail(int customerId)
    {
        await Task.Delay(100);
        throw new TimeoutException($"SMTP server did not respond while emailing customer {customerId}");
    }
}
