using NetcShared;

namespace NetcServer.Agent;

/// <summary>UI-хост агента: трансляция и блокировка экрана в сессии ученика.</summary>
public static class AgentUi
{
    private static Form? _host;
    private static BroadcastForm? _broadcast;
    private static LockForm? _lock;
    private static TestForm? _test;

    public static void Initialize(Form host) => _host = host;

    private static void OnUi(Action action)
    {
        Form? host = _host;
        if (host is null || host.IsDisposed) return;
        try
        {
            if (host.InvokeRequired) host.BeginInvoke(action);
            else action();
        }
        catch { }
    }

    public static void ShowBroadcast() => OnUi(() =>
    {
        if (_broadcast is null || _broadcast.IsDisposed) _broadcast = new BroadcastForm();
        if (!_broadcast.Visible) _broadcast.Show();
        _broadcast.BringToFront();
    });

    public static void SetBroadcastFrame(byte[] jpeg) => OnUi(() => _broadcast?.SetFrame(jpeg));

    public static void HideBroadcast() => OnUi(() =>
    {
        _broadcast?.Close();
        _broadcast?.Dispose();
        _broadcast = null;
    });

    public static void ShowLock(byte[] payload) => OnUi(() =>
    {
        LockMessage msg = payload.Length > 0
            ? (Json.FromBytes<LockMessage>(payload) ?? new LockMessage())
            : new LockMessage();
        if (_lock is null || _lock.IsDisposed) _lock = new LockForm(msg.Blank, msg.Text);
        if (!_lock.Visible) _lock.Show();
        _lock.BringToFront();
    });

    public static void HideLock() => OnUi(() =>
    {
        _lock?.Close();
        _lock?.Dispose();
        _lock = null;
    });

    /// <summary>Показать всплывающее сообщение учителя (опционально с ответом).</summary>
    public static void ShowMessage(ChatMessage msg, Func<ChatMessage, Task>? reply) => OnUi(() =>
    {
        var form = new MessageForm(msg, reply);
        form.Show();
        form.BringToFront();
    });

    /// <summary>Есть ли сейчас активный (не закрытый) тест.</summary>
    public static bool HasTest
    {
        get
        {
            var t = _test;
            return t is not null && !t.IsDisposed;
        }
    }

    /// <summary>Показать ученику тест в полноэкранном киоск-режиме.</summary>
    public static void ShowTest(TestDefinition test, Func<TestResult, Task>? submit) => OnUi(() =>
    {
        // Предыдущий тест (если был) — досрочно закрываем.
        if (_test is not null && !_test.IsDisposed) _test.ForceFinish("replaced");

        TestForm? form = null;
        form = new TestForm(test, submit, () =>
        {
            // Вытесненный тест закрывается позже нового; снимаем киоск и обнуляем ссылку,
            // только если закрывается именно актуальный экземпляр (иначе «убили» бы новый тест).
            if (!ReferenceEquals(_test, form)) return;
            KioskGuard.Disable();
            _test = null;
        });
        _test = form;
        KioskGuard.Enable(form!);
        form!.Show();
        form.BringToFront();
        form.Activate();
    });

    /// <summary>Досрочно завершить активный тест (учитель). Вызывать из UI-потока.</summary>
    public static void StopTest(string reason) => OnUi(() => _test?.ForceFinish(reason));
}
