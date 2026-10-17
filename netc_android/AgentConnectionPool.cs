using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Пул постоянных соединений с агентами. Нужен для блокировки/трансляции: агент снимает оверлей,
/// когда отключается соединение-владелец, поэтому соединение нельзя закрывать сразу после команды.
/// </summary>
public sealed class AgentConnectionPool : IDisposable
{
    private readonly AppSettings _settings;
    private readonly byte[] _key;
    private readonly Dictionary<string, ScreenClient> _conns = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public AgentConnectionPool(AppSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;
    }

    public async Task<ScreenClient> EnsureAsync(string address)
    {
        lock (_gate)
        {
            if (_conns.TryGetValue(address, out var existing) && existing.IsConnected)
                return existing;
        }

        var client = new ScreenClient(address, _settings.ControlPort, _key);
        await client.ConnectAsync();
        lock (_gate) { _conns[address] = client; }
        return client;
    }

    /// <summary>Закрыть соединения с ПК, которых нет в текущем выборе.</summary>
    public void Retain(IEnumerable<string> selected)
    {
        var set = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var addr in _conns.Keys.ToList())
            {
                if (set.Contains(addr)) continue;
                try { _conns[addr].Dispose(); } catch { }
                _conns.Remove(addr);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var c in _conns.Values) { try { c.Dispose(); } catch { } }
            _conns.Clear();
        }
    }
}
