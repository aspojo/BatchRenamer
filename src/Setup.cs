using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("批量重命名安装程序")]
[assembly: AssemblyProduct("批量重命名")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace BatchRenamerSetup
{
    internal static class Setup
    {
        private const string Title = "批量重命名";
        private const string MenuTitle = "批量重命名（预览）";
        private const string Clsid = "{E23FBC65-A90B-4468-8E6F-2F3B522FD91B}";
        private const string VerbKey = @"Software\Classes\*\shell\BatchRenamer.Files";
        private const string ClassKey = @"Software\Classes\CLSID\" + Clsid;
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BatchRenamer";
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\BatchRenamer");
        private static readonly string AppPath = Path.Combine(Folder, "BatchRenamer.exe");
        private static readonly string SetupPath = Path.Combine(Folder, "Uninstall.exe");
        private static readonly string MenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "批量重命名.lnk");
        private static readonly string Self = Assembly.GetExecutingAssembly().Location;

        [STAThread]
        private static int Main(string[] args)
        {
            bool quiet = Has(args, "/quiet");
            bool uninstall = Has(args, "/uninstall") || PathEquals(Self, SetupPath);
            try
            {
                bool created;
                using (var mutex = new Mutex(true, @"Local\BatchRenamerSetup-E23FBC65", out created))
                {
                    if (!created) throw new IOException("另一个安装或卸载操作正在进行。");
                    if (uninstall)
                    {
                        if (!quiet && MessageBox.Show("卸载批量重命名并移除文件右键菜单？", Title,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                        Uninstall();
                    }
                    else Install();
                }
                if (!quiet) MessageBox.Show(uninstall ? "卸载完成。" :
                    "安装完成。在资源管理器中选中文件，右键选择“" + MenuTitle + "”。\n\nWindows 11 可在“显示更多选项”中找到它。",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show(ex.GetBaseException().Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool Has(string[] args, string value)
        { return Array.Exists(args, a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase)); }

        private static bool PathEquals(string a, string b)
        { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }

        private static RegistryKey UserHive()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        }

        private static void Install()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Path.GetDirectoryName(MenuLink));
            var changes = new List<FileChange>();
            bool existing;
            using (RegistryKey hive = UserHive())
            using (RegistryKey entry = hive.OpenSubKey(UninstallKey)) existing = entry != null;
            try
            {
                changes.Add(InstallPayload("BatchRenamer.exe"));
                changes.Add(InstallPayload("SelectionDrop32.dll"));
                if (Environment.Is64BitOperatingSystem) changes.Add(InstallPayload("SelectionDrop64.dll"));
                changes.Add(new FileChange(SetupPath, p => File.Copy(Self, p)));
                changes.Add(new FileChange(MenuLink, CreateShortcut));
                Register();
            }
            catch
            {
                if (!existing) Unregister();
                for (int i = changes.Count - 1; i >= 0; --i) changes[i].Rollback();
                RemoveFolderIfEmpty();
                throw;
            }
            foreach (var change in changes) change.Commit();
            NotifyExplorer();
        }

        private static FileChange InstallPayload(string name)
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (payload == null) throw new InvalidDataException("安装包缺少文件：" + name);
                return new FileChange(Path.Combine(Folder, name), p =>
                {
                    using (FileStream output = File.Create(p)) payload.CopyTo(output);
                });
            }
        }

        private sealed class FileChange
        {
            private readonly string path;
            private readonly string backup;
            private readonly bool changed;

            internal FileChange(string path, Action<string> write)
            {
                this.path = path;
                string temp = Path.Combine(Path.GetDirectoryName(path), ".BatchRenamer-" + Guid.NewGuid().ToString("N"));
                string staging = temp + Path.GetExtension(path);
                try
                {
                    write(staging);
                    if (File.Exists(path))
                    {
                        if (SameContents(path, staging)) return;
                        backup = temp + ".bak";
                        File.Replace(staging, path, backup);
                    }
                    else File.Move(staging, path);
                    changed = true;
                }
                finally { File.Delete(staging); }
            }

            internal void Rollback()
            {
                if (!changed) return;
                if (backup != null) File.Replace(backup, path, null);
                else File.Delete(path);
            }

            internal void Commit() { if (backup != null) RemoveInstalledFile(backup); }

            private static bool SameContents(string first, string second)
            {
                if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
                using (var hash = SHA256.Create())
                using (var a = File.OpenRead(first))
                using (var b = File.OpenRead(second))
                    return Convert.ToBase64String(hash.ComputeHash(a)) == Convert.ToBase64String(hash.ComputeHash(b));
            }
        }

        private static void CreateShortcut(string path)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(type);
            object shortcut = null;
            try
            {
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type link = shortcut.GetType();
                link.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { AppPath });
                link.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Folder });
                link.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { Title });
                link.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void Register()
        {
            SetHandler(RegistryView.Registry32, "SelectionDrop32.dll");
            if (Environment.Is64BitOperatingSystem) SetHandler(RegistryView.Registry64, "SelectionDrop64.dll");
            using (RegistryKey hive = UserHive())
            {
                using (RegistryKey verb = hive.CreateSubKey(VerbKey))
                {
                    verb.SetValue("", MenuTitle);
                    verb.SetValue("MUIVerb", MenuTitle);
                    verb.SetValue("Icon", Quote(AppPath) + ",0");
                    verb.SetValue("MultiSelectModel", "Player");
                    using (RegistryKey drop = verb.CreateSubKey("DropTarget")) drop.SetValue("CLSID", Clsid);
                }
                using (RegistryKey installed = hive.CreateSubKey(UninstallKey))
                {
                    installed.SetValue("DisplayName", Title);
                    installed.SetValue("DisplayVersion", "1.0.0");
                    installed.SetValue("InstallLocation", Folder);
                    installed.SetValue("DisplayIcon", Quote(AppPath) + ",0");
                    installed.SetValue("UninstallString", Quote(SetupPath) + " /uninstall");
                    installed.SetValue("QuietUninstallString", Quote(SetupPath) + " /uninstall /quiet");
                    installed.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    installed.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
        }

        private static void SetHandler(RegistryView view, string library)
        {
            using (RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            using (RegistryKey cls = hive.CreateSubKey(ClassKey))
            {
                cls.DeleteSubKeyTree("LocalServer32", false);
                using (RegistryKey server = cls.CreateSubKey("InprocServer32"))
                {
                    server.SetValue("", Path.Combine(Folder, library));
                    server.SetValue("ThreadingModel", "Apartment");
                }
            }
        }

        private static string Quote(string s) { return "\"" + s + "\""; }

        private static void Uninstall()
        {
            if (Directory.Exists(Folder))
            {
                RemoveInstalledFile(AppPath);
                RemoveInstalledFile(Path.Combine(Folder, "SelectionDrop32.dll"));
                RemoveInstalledFile(Path.Combine(Folder, "SelectionDrop64.dll"));
                if (!PathEquals(Self, SetupPath)) RemoveInstalledFile(SetupPath);
            }
            if (File.Exists(MenuLink)) File.Delete(MenuLink);
            Unregister();
            NotifyExplorer();
            if (PathEquals(Self, SetupPath)) CleanUpAfterExit();
            else RemoveFolderIfEmpty();
        }

        private static void RemoveInstalledFile(string path)
        {
            try { File.Delete(path); return; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            // A mapped DLL (or a running app) can be renamed while Windows denies
            // deletion. Give this exact retired file a unique name outside the installation
            // so an immediate reinstall is safe. A per-user RunOnce removes it after logoff.
            string retiredName = "BatchRenamer-" + Guid.NewGuid().ToString("N") + ".old";
            string temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            Directory.CreateDirectory(temp);
            string retiredPath = Path.Combine(temp, retiredName);
            File.Move(path, retiredPath);
            try { File.Delete(retiredPath); return; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            using (RegistryKey hive = UserHive())
            using (RegistryKey pending = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce"))
                pending.SetValue(retiredName, Quote(powershell) + " -NoProfile -NonInteractive -WindowStyle Hidden -Command " +
                    Quote("[IO.File]::Delete((Join-Path $env:LOCALAPPDATA 'Temp\\" + retiredName + "'))"));
        }

        private static void Unregister()
        {
            using (RegistryKey hive32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
                hive32.DeleteSubKeyTree(ClassKey, false);
            using (RegistryKey hive = UserHive())
            {
                hive.DeleteSubKeyTree(VerbKey, false);
                hive.DeleteSubKeyTree(ClassKey, false);
                hive.DeleteSubKeyTree(UninstallKey, false);
            }
        }

        private static void RemoveFolderIfEmpty()
        {
            if (Directory.Exists(Folder) && Directory.GetFileSystemEntries(Folder).Length == 0)
                Directory.Delete(Folder, false);
        }

        private static void CleanUpAfterExit()
        {
            // The executable stays mapped until this process exits. Use a hidden system
            // PowerShell process to remove just this file, then the empty install directory.
            string file = "'" + SetupPath.Replace("'", "''") + "'";
            string folder = "'" + Folder.Replace("'", "''") + "'";
            string program = "Wait-Process -Id " + Process.GetCurrentProcess().Id +
                " -ErrorAction SilentlyContinue; if (-not [IO.File]::Exists(" +
                "'" + AppPath.Replace("'", "''") + "')) { [IO.File]::Delete(" + file +
                "); if ([IO.Directory]::Exists(" + folder + ") -and [IO.Directory]::GetFileSystemEntries(" +
                folder + ").Length -eq 0) { [IO.Directory]::Delete(" + folder + ", $false) } }";
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            using (Process.Start(new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(program))) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
            })) { }
        }

        private static void NotifyExplorer() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
