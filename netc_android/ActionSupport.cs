using NetcShared;

namespace NetcAndroid;

/// <summary>Прогон действия по списку ПК: отдельное соединение на каждый ПК, ограничение параллелизма.</summary>
public static class ActionSupport
{
    public static async Task RunAsync(
        IReadOnlyList<string> targets,
        AppSettings settings,
        byte[] key,
        Action<string> log,
        Func<ScreenClient, Task<string>> action,
        int concurrency = 8)
    {
        using var sem = new SemaphoreSlim(concurrency);
        var tasks = targets.Select(async addr =>
        {
            await sem.WaitAsync();
            try
            {
                using var client = new ScreenClient(addr, settings.ControlPort, key);
                await client.ConnectAsync();
                string message = await action(client);
                log($"{addr}: {message}");
            }
            catch (Exception ex)
            {
                log($"{addr}: ошибка — {ex.Message}");
            }
            finally { sem.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
    }
}
