using Android.App;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>Отправка сообщения ученикам с возможностью ждать ответ.</summary>
[Activity(Label = "Сообщение", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class MessageActivity : ActionActivity
{
    private EditText _text = null!;
    private CheckBox _expectReply = null!;
    private readonly Dictionary<string, ScreenClient> _conns = new(StringComparer.OrdinalIgnoreCase);

    protected override void BuildContent(LinearLayout content)
    {
        AddLabel(content, "Сообщение ученику:");
        _text = AddEdit(content, "", "текст сообщения");
        _expectReply = new CheckBox(this) { Text = "Ждать ответ" };
        content.AddView(_expectReply);
        AddButton(content, "Отправить", Send);
    }

    private async void Send()
    {
        string text = _text.Text?.Trim() ?? "";
        if (text.Length == 0) { Log("Введите текст сообщения."); return; }

        var targets = Targets.SelectedAddresses;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        Log($"Отправка на {targets.Count} ПК…");
        using var sem = new SemaphoreSlim(8);
        var tasks = targets.Select(async addr =>
        {
            await sem.WaitAsync();
            try
            {
                var client = await EnsureAsync(addr);
                await client.SendMessageAsync(new ChatMessage
                {
                    From = "Учитель",
                    Text = text,
                    ExpectReply = _expectReply.Checked
                });
                Log(addr + ": отправлено");
            }
            catch (Exception ex) { Log(addr + ": ошибка — " + ex.Message); }
            finally { sem.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private async Task<ScreenClient> EnsureAsync(string addr)
    {
        lock (_conns)
        {
            if (_conns.TryGetValue(addr, out var existing) && existing.IsConnected) return existing;
        }

        var client = new ScreenClient(addr, Settings.ControlPort, Key);
        await client.ConnectAsync();
        client.MessageReceived += m => Log($"{addr} (ответ): {m.Text}");
        lock (_conns) { _conns[addr] = client; }
        return client;
    }

    protected override void OnDestroy()
    {
        lock (_conns)
        {
            foreach (var c in _conns.Values) { try { c.Dispose(); } catch { } }
            _conns.Clear();
        }
        base.OnDestroy();
    }
}
