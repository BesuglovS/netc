using Android.App;
using Android.Views;
using Android.Widget;

namespace NetcAndroid;

/// <summary>
/// Блокировка экранов учеников. Соединения с ПК удерживаются открытыми: агент снимает блокировку,
/// когда отключается соединение-владелец (см. TcpControlServer в агенте).
/// Трансляция экрана планшета не выполняется.
/// </summary>
[Activity(Label = "Блокировка экранов", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class LockActivity : ActionActivity
{
    private CheckBox _blank = null!;
    private AgentConnectionPool? _pool;

    private AgentConnectionPool Pool => _pool ??= new AgentConnectionPool(Settings, Key);

    protected override void BuildContent(LinearLayout content)
    {
        _blank = new CheckBox(this) { Text = "Чёрный экран (иначе — затемнение с надписью)" };
        content.AddView(_blank);

        AddButton(content, "Заблокировать экраны", () => Apply(true));
        AddButton(content, "Разблокировать", () => Apply(false));

        AddLabel(content, "Чтобы блокировка держалась, держите этот экран открытым. " +
                          "Кнопка «Назад» снимает блокировку.");
    }

    private async void Apply(bool lockIt)
    {
        var targets = Targets.SelectedAddresses;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        Pool.Retain(targets);
        Log((lockIt ? "Блокировка" : "Разблокировка") + $" на {targets.Count} ПК…");

        using var sem = new SemaphoreSlim(8);
        var tasks = targets.Select(async addr =>
        {
            await sem.WaitAsync();
            try
            {
                var client = await Pool.EnsureAsync(addr);
                if (lockIt) await client.SendLockAsync(_blank.Checked, "Экран заблокирован учителем");
                else await client.SendUnlockAsync();
                Log(addr + (lockIt ? ": заблокировано" : ": разблокировано"));
            }
            catch (Exception ex) { Log(addr + ": ошибка — " + ex.Message); }
            finally { sem.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    protected override void OnDestroy()
    {
        // Соединения закрываются — агенты снимают блокировку.
        _pool?.Dispose();
        _pool = null;
        base.OnDestroy();
    }
}
