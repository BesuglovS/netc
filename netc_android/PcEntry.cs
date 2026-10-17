namespace NetcAndroid;

/// <summary>Состояние ПК в списке.</summary>
public enum PcStatus
{
    /// <summary>Ещё не опрашивался.</summary>
    Unknown,

    /// <summary>Опрос идёт.</summary>
    Checking,

    /// <summary>Ответил (сведения получены).</summary>
    Ok,

    /// <summary>Нет ответа.</summary>
    NoReply
}

/// <summary>Строка списка ПК (адрес, имя, пользователь, модель, ОС, MAC, статус).</summary>
public sealed class PcEntry
{
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
    public string User { get; set; } = "";
    public string Model { get; set; } = "";
    public string Os { get; set; } = "";
    public string Mac { get; set; } = "";
    public PcStatus Status { get; set; } = PcStatus.Unknown;
    public bool Selected { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Address : Name + "  " + Address;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(User)) parts.Add(User);
            if (!string.IsNullOrWhiteSpace(Model)) parts.Add(Model);
            if (!string.IsNullOrWhiteSpace(Os)) parts.Add(Os);
            return string.Join(" · ", parts);
        }
    }
}
