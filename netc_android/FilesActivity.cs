using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>Передача файлов: отправить файл планшета на учеников и забрать файл с ученика.</summary>
[Activity(Label = "Файлы", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class FilesActivity : ActionActivity
{
    private const int RequestPickFile = 1001;

    private EditText _remoteEdit = null!;

    protected override void BuildContent(LinearLayout content)
    {
        AddButton(content, "Отправить файл на ПК…", PickFile);

        AddLabel(content, "Забрать файл с ПК — полный путь на ученическом ПК:");
        _remoteEdit = AddEdit(content, @"C:\Users\student\Desktop\file.txt");
        AddButton(content, "Забрать файл", RequestFile);
    }

    private void PickFile()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        StartActivityForResult(intent, RequestPickFile);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != RequestPickFile || resultCode != Result.Ok || data?.Data is null) return;

        var uri = data.Data;
        string name = uri.LastPathSegment ?? "file.bin";
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name.Substring(slash + 1);
        if (name.Length == 0) name = "file.bin";

        string path;
        try
        {
            var dir = Path.Combine(CacheDir!.AbsolutePath, "send");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, name);
            using var input = ContentResolver!.OpenInputStream(uri)
                              ?? throw new IOException("нет доступа к файлу");
            using var output = File.Create(path);
            input.CopyTo(output);
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать файл: " + ex.Message);
            return;
        }

        long size = new FileInfo(path).Length;
        Log($"Файл «{name}» ({size} байт) → отправка…");
        RunAction(async client =>
        {
            var res = await client.SendFileAsync(path, 180000, CancellationToken.None);
            return res.Ok ? "отправлено" : "не отправлено: " + res.Message;
        });
    }

    private void RequestFile()
    {
        string remote = _remoteEdit.Text?.Trim() ?? "";
        if (remote.Length == 0) { Log("Укажите путь к файлу на ученическом ПК."); return; }

        string dir = GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                     ?? CacheDir!.AbsolutePath;
        Directory.CreateDirectory(dir);
        Log("Файлы сохраняются в: " + dir);

        RunAction(async client =>
        {
            var res = await client.RequestFileAsync(remote, dir, 180000, CancellationToken.None);
            return res.Ok ? "сохранено: " + res.Path : "не получено: " + res.Message;
        });
    }
}
