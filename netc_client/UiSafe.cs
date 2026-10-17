namespace NetcClient;

/// <summary>
/// Безопасные обращения к элементам интерфейса из асинхронных обработчиков:
/// форма могла быть закрыта, пока операция ещё выполнялась. Такие обращения
/// молча игнорируются вместо падения с <see cref="ObjectDisposedException"/>.
/// </summary>
public static class UiSafe
{
    /// <summary>Добавляет текст в TextBox, если он ещё существует и не уничтожен.</summary>
    public static void Append(TextBox? box, string text)
    {
        if (box is null || box.IsDisposed || box.Disposing) return;
        try
        {
            if (box.IsHandleCreated && box.InvokeRequired)
            {
                box.BeginInvoke((Action)(() => Append(box, text)));
                return;
            }
            box.AppendText(text);
        }
        catch { /* форма закрыта во время операции */ }
    }

    /// <summary>Меняет Enabled у контрола, если он ещё существует и не уничтожен.</summary>
    public static void SetEnabled(Control? control, bool value)
    {
        if (control is null || control.IsDisposed || control.Disposing) return;
        try { control.Enabled = value; }
        catch { /* форма закрыта во время операции */ }
    }

    /// <summary>Добавляет текст в лог и обрезает его, чтобы длительная работа не «съедала» память.</summary>
    public static void AppendBounded(TextBox? box, string text, int maxChars = 200_000)
    {
        if (box is null || box.IsDisposed || box.Disposing) return;
        try
        {
            if (box.IsHandleCreated && box.InvokeRequired)
            {
                box.BeginInvoke((Action)(() => AppendBounded(box, text, maxChars)));
                return;
            }
            box.AppendText(text);
            if (box.TextLength > maxChars)
            {
                box.Text = box.Text[(box.TextLength - maxChars / 2)..];
                box.SelectionStart = box.TextLength;
                box.ScrollToCaret();
            }
        }
        catch { /* форма закрыта во время операции */ }
    }

    /// <summary>Выполняет действие над контролом, если он ещё существует и не уничтожен.</summary>
    public static void IfAlive(Control? control, Action action)
    {
        if (control is null || control.IsDisposed || control.Disposing) return;
        try { action(); }
        catch { /* форма закрыта во время операции */ }
    }
}
