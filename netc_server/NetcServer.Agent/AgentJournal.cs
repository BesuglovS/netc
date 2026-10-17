using NetcShared;

namespace NetcServer.Agent;

/// <summary>Кольцевой журнал событий агента (для запроса учителем).</summary>
public static class AgentJournal
{
    private const int Capacity = 500;
    private static readonly object Gate = new();
    private static readonly LinkedList<JournalEntry> Entries = new();

    public static void Add(string category, string message)
    {
        var entry = new JournalEntry
        {
            TimeUtc = DateTime.UtcNow,
            Category = category ?? "",
            Message = message ?? ""
        };
        lock (Gate)
        {
            Entries.AddLast(entry);
            while (Entries.Count > Capacity) Entries.RemoveFirst();
        }
    }

    public static List<JournalEntry> Snapshot()
    {
        lock (Gate) return new List<JournalEntry>(Entries);
    }
}
