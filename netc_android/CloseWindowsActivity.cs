using Android.App;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>Закрыть все окна и приложения на выбранных ПК.</summary>
[Activity(Label = "Закрыть окна", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class CloseWindowsActivity : ActionActivity
{
    private CheckBox _explorer = null!;
    private CheckBox _force = null!;

    protected override void BuildContent(LinearLayout content)
    {
        _explorer = new CheckBox(this) { Text = "Закрывать окна Проводника", Checked = true };
        _force = new CheckBox(this) { Text = "Принудительно завершать незакрывшиеся процессы" };
        content.AddView(_explorer);
        content.AddView(_force);

        AddButton(content, "Закрыть окна", Confirm);
    }

    private void Confirm()
    {
        var req = new CloseAppsMessage
        {
            IncludeExplorer = _explorer.Checked,
            Force = _force.Checked,
            GraceMs = 2500
        };

        new AlertDialog.Builder(this)
            .SetTitle("Подтверждение")
            ?.SetMessage($"Закрыть окна на {Targets.SelectedAddresses.Count} ПК?" +
                         (_force.Checked ? "\nПроцессы будут завершены принудительно." : ""))
            ?.SetPositiveButton("Закрыть", (_, _) => RunAction(async client =>
            {
                var res = await client.CloseAppsAsync(req, 20000, CancellationToken.None);
                if (res is null) return "нет ответа";
                return $"окон {res.Windows}, принудительно {res.Forced}";
            }))
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }
}
