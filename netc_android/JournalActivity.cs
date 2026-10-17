using Android.App;
using Android.Views;
using Android.Widget;

namespace NetcAndroid;

/// <summary>Журнал событий агентов: запросить и показать записи.</summary>
[Activity(Label = "Журнал", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class JournalActivity : ActionActivity
{
    protected override void BuildContent(LinearLayout content)
    {
        AddButton(content, "Запросить журнал", () => RunAction(async client =>
        {
            var entries = await client.RequestJournalAsync(10000, CancellationToken.None);
            if (entries is null) return "нет ответа";
            if (entries.Count == 0) return "журнал пуст";

            foreach (var e in entries.OrderBy(x => x.TimeUtc))
                Log($"    {e.TimeUtc.ToLocalTime():HH:mm:ss}  [{e.Category}] {e.Message}");
            return $"записей {entries.Count}";
        }));
    }
}
