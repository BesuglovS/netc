using Android.App;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>Питание: выключить / перезагрузить / завершить сеанс на выбранных ПК.</summary>
[Activity(Label = "Питание", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class PowerActivity : ActionActivity
{
    private RadioButton _rbShutdown = null!;
    private RadioButton _rbRestart = null!;
    private RadioButton _rbLogoff = null!;
    private EditText _delay = null!;

    protected override void BuildContent(LinearLayout content)
    {
        var group = new RadioGroup(this) { Orientation = Orientation.Vertical };
        _rbShutdown = new RadioButton(this) { Text = "Выключить", Checked = true };
        _rbRestart = new RadioButton(this) { Text = "Перезагрузить" };
        _rbLogoff = new RadioButton(this) { Text = "Завершить сеанс" };
        group.AddView(_rbShutdown);
        group.AddView(_rbRestart);
        group.AddView(_rbLogoff);
        content.AddView(group);

        AddLabel(content, "Задержка перед выполнением, сек (для выключения/перезагрузки):");
        _delay = AddEdit(content, "5");
        _delay.InputType = Android.Text.InputTypes.ClassNumber;

        AddButton(content, "Выполнить", Confirm);
    }

    private void Confirm()
    {
        string what = _rbRestart.Checked ? "перезагрузить"
            : _rbLogoff.Checked ? "завершить сеанс" : "выключить";
        int count = Targets.SelectedAddresses.Count;

        new AlertDialog.Builder(this)
            .SetTitle("Подтверждение")
            ?.SetMessage($"Выполнить «{what}» на {count} ПК?")
            ?.SetPositiveButton("Выполнить", (_, _) => Execute(what))
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }

    private void Execute(string what)
    {
        var action = _rbRestart.Checked ? PowerAction.Restart
            : _rbLogoff.Checked ? PowerAction.Logoff
            : PowerAction.Shutdown;
        int delay = int.TryParse(_delay.Text, out var d) ? d : 5;

        RunAction(async client =>
        {
            await client.SendPowerAsync(action, delay);
            return what + " (задержка " + delay + " с)";
        });
    }
}
