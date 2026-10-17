namespace NetcServer.Agent;

/// <summary>Пути для приёма файлов от учителя в сессии ученика.</summary>
public static class NetcPaths
{
    /// <summary>Папка для принятых файлов: <профиль>\Downloads\netc.</summary>
    public static string IncomingDirectory
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();
            return Path.Combine(home, "Downloads", "netc");
        }
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        name = name.Trim();
        return name.Length == 0 ? "file.bin" : name;
    }

    /// <summary>Подбирает свободное имя файла, добавляя суффикс (1), (2)… при совпадении.</summary>
    public static string UniquePath(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
