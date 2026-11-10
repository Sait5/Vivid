using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Vivid
{
    // Раньше программа называлась Backdrop: переносим настройки, коллекцию, папку обоев и автозапуск под новое имя.
    static class Migrate
    {
        const string Old = "Backdrop";

        public static void FromOldName()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string oldDir = Path.Combine(appData, Old);
                if (!Directory.Exists(Store.Dir) && Directory.Exists(oldDir))
                {
                    try { Directory.Move(oldDir, Store.Dir); }
                    catch { CopyDir(oldDir, Store.Dir); }
                }

                // папка с сохранёнными обоями в «Документах»
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string oldWalls = Path.Combine(docs, Old), newWalls = Path.Combine(docs, "Vivid");
                if (Directory.Exists(oldWalls) && !Directory.Exists(newWalls))
                {
                    try { Directory.Move(oldWalls, newWalls); }
                    catch { CopyDir(oldWalls, newWalls); }
                }

                // пути внутри коллекции
                string lib = Path.Combine(Store.Dir, "library.json");
                if (File.Exists(lib))
                {
                    string text = File.ReadAllText(lib, Encoding.UTF8);
                    string a = oldWalls.Replace("\\", "\\\\") + "\\\\", b = newWalls.Replace("\\", "\\\\") + "\\\\";
                    if (text.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 && Directory.Exists(newWalls))
                        File.WriteAllText(lib, text.Replace(a, b), new UTF8Encoding(false));
                }

                // автозапуск: старая запись → новая
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k != null && k.GetValue(Old) != null)
                    {
                        k.DeleteValue(Old, false);
                        k.SetValue("Vivid", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" /tray");
                    }
                }
            }
            catch (Exception e) { Log.Write("Перенос со старого имени: " + e.Message); }
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(to, f.Substring(from.Length).TrimStart('\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }
    }
}
