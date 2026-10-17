using Android.App;
using Android.Views;
using Android.Widget;

namespace NetcAndroid;

/// <summary>Мониторы: выключить/включить подсветку на выбранных ПК, не выключая сами ПК.</summary>
[Activity(Label = "Мониторы", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class MonitorActivity : ActionActivity
{
    protected override void BuildContent(LinearLayout content)
    {
        AddLabel(content,
            "Гасит/включает подсветку мониторов штатной командой Windows. ПК продолжает работать;\n" +
            "любое движение мыши или нажатие клавиши у ученика снова включает экран.");
        AddButton(content, "Выключить мониторы", () => Send(true));
        AddButton(content, "Включить мониторы", () => Send(false));
    }

    private void Send(bool off)
    {
        int count = Targets.SelectedAddresses.Count;
        new AlertDialog.Builder(this)
            .SetTitle("Подтверждение")
            ?.SetMessage($"{(off ? "Выключить" : "Включить")} мониторы на {count} ПК?")
            ?.SetPositiveButton("Выполнить", (_, _) => RunAction(async client =>
            {
                await client.SendMonitorAsync(off);
                return off ? "мониторы выключены" : "мониторы включены";
            }))
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }
}
