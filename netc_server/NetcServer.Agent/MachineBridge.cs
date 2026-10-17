using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Обмен «агент → служба-сторож» через общую папку (<see cref="MachinePersonalization.ExchangeDirectory"/>).
/// Используется для машинных операций, которые агент выполнить не может: настройки HKLM (экран блокировки),
/// перезапуск агента и принудительное завершение процессов (в т.ч. запущенных от администратора).
/// </summary>
internal static class MachineBridge
{
    /// <summary>Асинхронный запрос; возвращает результат, сообщение и числовой счётчик сторожа.</summary>
    public static async Task<(bool Ok, string Message, int Count)> RequestAsync(
        string action, string? path, IEnumerable<int>? pids, int timeoutMs, CancellationToken ct)
    {
        try
        {
            string dir = MachinePersonalization.ExchangeDirectory;
            Directory.CreateDirectory(dir);

            string id = Guid.NewGuid().ToString("N");
            string reqPath = Path.Combine(dir, MachinePersonalization.RequestPrefix + id + ".json");
            string resPath = Path.Combine(dir, MachinePersonalization.ResponsePrefix + id + ".json");

            var request = new MachinePersonalization.Request { Action = action, Path = path };
            if (pids is not null) request.Pids = pids.ToList();
            MachinePersonalization.WriteRequest(reqPath, request);

            int waited = 0;
            while (waited < timeoutMs)
            {
                if (File.Exists(resPath))
                {
                    var res = MachinePersonalization.ReadResponse(resPath);
                    try { File.Delete(resPath); } catch { }
                    if (res is not null) return (res.Ok, res.Message, res.Count);
                    break;
                }
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { break; }
                waited += 100;
            }

            try { File.Delete(reqPath); } catch { }
            return (false, "служба-сторож не ответила", 0);
        }
        catch (Exception ex)
        {
            return (false, "служба-сторож: " + ex.Message, 0);
        }
    }

    /// <summary>Синхронный запрос (для персонализации/перезапуска).</summary>
    public static bool Request(string action, string? path, IEnumerable<int>? pids, int timeoutMs, out string message)
    {
        var (ok, msg, _) = RequestAsync(action, path, pids, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        message = msg;
        return ok;
    }
}
