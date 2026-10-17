using System;
using System.IO;
using System.Text;

namespace ControlWorksCollector.Services
{
    /// <summary>Простое потокобезопасное логирование в файл.</summary>
    public static class Logger
    {
        private static readonly object Gate = new object();

        public static string LogDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ControlWorksCollector",
                    "logs");
            }
        }

        public static string CurrentLogFile
        {
            get { return Path.Combine(LogDirectory, "collect-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                    File.AppendAllText(CurrentLogFile, line, Encoding.UTF8);
                }
            }
            catch
            {
                // логирование не должно ронять приложение
            }
        }
    }
}
