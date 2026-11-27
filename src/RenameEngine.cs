using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace BatchRenamer
{
    public sealed class RenameItem
    {
        public string SourcePath { get; set; }
        public string NewName { get; set; }

        public RenameItem(string sourcePath, string newName)
        {
            SourcePath = sourcePath;
            NewName = newName;
        }
    }

    internal sealed class Change
    {
        internal string Source, Target;
        internal FileStamp Stamp;
    }

    public sealed class BatchResult
    {
        private readonly List<Change> changes;
        private readonly bool copy;
        private bool undone;

        internal BatchResult(List<Change> changes, bool copy)
        {
            this.changes = changes;
            this.copy = copy;
        }

        public int Count { get { return changes.Count; } }

        public void Undo()
        {
            lock (changes)
            {
                if (undone) throw new InvalidOperationException("这批操作已经撤销。");
                RenameEngine.Execute(changes, copy, true);
                undone = true;
            }
        }
    }

    public static class RenameEngine
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex DeviceName = new Regex(
            @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string NameError(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "新文件名不能为空。";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "新文件名含有 Windows 不允许的字符。";
            if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
                return "文件名末尾不能是空格或英文句点。";
            if (name.Length > 255) return "文件名超过 255 个字符。";
            if (DeviceName.IsMatch(name.Split('.')[0].TrimEnd(' '))) return "这是 Windows 保留的设备名。";
            return null;
        }

        public static string[] Validate(IList<RenameItem> items, bool copy)
        {
            if (items == null) throw new ArgumentNullException("items");
            string[] errors = new string[items.Count];
            string[] sources = new string[items.Count];
            string[] targets = new string[items.Count];
            Dictionary<string, int> sourceRows = new Dictionary<string, int>(PathComparer);
            Dictionary<string, int> targetRows = new Dictionary<string, int>(PathComparer);
            for (int i = 0; i < items.Count; i++)
            {
                RenameItem item = items[i];
                if (item == null || String.IsNullOrWhiteSpace(item.SourcePath))
                {
                    errors[i] = "缺少原文件路径。";
                    continue;
                }
                try
                {
                    string source = Path.GetFullPath(item.SourcePath);
                    if (!File.Exists(source))
                    {
                        errors[i] = "找不到原文件或无法访问。";
                        continue;
                    }
                    sources[i] = source;
                    errors[i] = NameError(item.NewName);
                    if (errors[i] != null) continue;
                    string target = Path.Combine(Path.GetDirectoryName(source), item.NewName);
                    if (target.Length >= 260 && !target.StartsWith(@"\\?\", StringComparison.Ordinal))
                    {
                        errors[i] = "新文件的完整路径过长。";
                        continue;
                    }
                    targets[i] = target;
                    Register(sourceRows, source, i, errors, "原文件被重复添加。");
                    Register(targetRows, target, i, errors, "多个文件使用了相同的新文件名。");
                }
                catch (Exception e) { errors[i] = "路径无效：" + e.Message; }
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (errors[i] != null || sources[i] == targets[i]) continue;
                bool vacated = !copy && sourceRows.ContainsKey(targets[i]);
                if (!vacated && (File.Exists(targets[i]) || Directory.Exists(targets[i])))
                    errors[i] = "目标名称已被占用。";
            }
            return errors;
        }

        private static void Register(Dictionary<string, int> rows, string key, int row, string[] errors, string message)
        {
            int first;
            if (rows.TryGetValue(key, out first)) errors[first] = errors[row] = message;
            else rows.Add(key, row);
        }

        public static BatchResult Apply(IList<RenameItem> items, bool copy)
        {
            string[] errors = Validate(items, copy);
            List<Change> changes = new List<Change>();
            for (int i = 0; i < items.Count; i++)
            {
                if (errors[i] != null) throw new IOException("第 " + (i + 1) + " 项：" + errors[i]);
                string source = Path.GetFullPath(items[i].SourcePath);
                string target = Path.Combine(Path.GetDirectoryName(source), items[i].NewName);
                if (source != target) changes.Add(new Change { Source = source, Target = target });
            }
            Execute(changes, copy, false);
            return new BatchResult(changes, copy);
        }

        // Keeping the handles through commit and rollback prevents replacement of an
        // item in between verification and modification. Windows will reject occupied targets.
        internal static void Execute(List<Change> changes, bool copy, bool undo)
        {
            List<HeldFile> files = new List<HeldFile>();
            Stack<Action> rollback = new Stack<Action>();
            bool verified = false;
            try
            {
                foreach (Change change in changes)
                {
                    string path = undo ? change.Target : change.Source;
                    if (copy && !undo)
                    {
                        path = SpareName(change.Target);
                        File.Copy(change.Source, path, false);
                    }
                    HeldFile file;
                    try { file = new HeldFile(path, copy); }
                    catch (Exception e)
                    {
                        if (copy && !undo)
                            throw new IOException("无法打开刚生成的临时副本，可在此路径检查：" + path, e);
                        throw;
                    }
                    files.Add(file);
                    if (copy && !undo) rollback.Push(delegate { file.SetDelete(true); });
                    if (undo && !change.Stamp.Matches(file.Stamp(copy)))
                        throw new IOException("文件已被修改或替换，无法撤销：" + path);
                }

                verified = true;
                if (!copy)
                {
                    foreach (HeldFile file in files) Move(file, SpareName(file.Path), rollback);
                }
                for (int i = 0; i < files.Count; i++)
                {
                    HeldFile file = files[i];
                    Change change = changes[i];
                    if (copy && undo)
                    {
                        file.SetDelete(true);
                        rollback.Push(delegate { file.SetDelete(false); });
                    }
                    else
                    {
                        Move(file, undo ? change.Source : change.Target, rollback);
                        if (!undo) change.Stamp = file.Stamp(copy);
                    }
                }
            }
            catch (Exception cause)
            {
                List<string> problems = new List<string>();
                while (rollback.Count != 0)
                {
                    try { rollback.Pop()(); }
                    catch (Exception error) { problems.Add(error.Message); }
                }
                // Returning an item to its previous name updates the NTFS change time.
                // Keep a failed undo retryable only after all verified files were restored.
                if (undo && verified && problems.Count == 0)
                    for (int i = 0; i < files.Count; i++)
                        changes[i].Stamp.Changed = files[i].ChangeTime();
                string message = cause.Message;
                if (problems.Count > 0)
                    message += Environment.NewLine + "部分文件未能恢复，请按路径检查：" +
                        Environment.NewLine + String.Join(Environment.NewLine, problems.ToArray());
                throw new IOException(message, cause);
            }
            finally
            {
                foreach (HeldFile file in files) file.Dispose();
            }
        }

        private static string SpareName(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetRandomFileName());
        }

        private static void Move(HeldFile file, string destination, Stack<Action> rollback)
        {
            string previous = file.Path;
            file.Move(destination);
            rollback.Push(delegate { file.Move(previous); });
        }
    }

    internal sealed class FileStamp
    {
        internal NativeFile.Info Info;
        internal long Changed;
        internal string Digest;

        internal bool Matches(FileStamp other)
        {
            return Info.Volume == other.Info.Volume && Info.IndexHigh == other.Info.IndexHigh &&
                Info.IndexLow == other.Info.IndexLow && Info.SizeHigh == other.Info.SizeHigh &&
                Info.SizeLow == other.Info.SizeLow && Info.Created == other.Info.Created &&
                Info.Written == other.Info.Written && Info.Attributes == other.Info.Attributes &&
                Changed == other.Changed && Digest == other.Digest;
        }
    }

    internal sealed class HeldFile : IDisposable
    {
        private readonly SafeFileHandle handle;
        private FileAttributes restoredAttributes;
        internal string Path { get; private set; }

        internal HeldFile(string path, bool copy)
        {
            Path = path;
            // DELETE and FILE_READ_ATTRIBUTES, plus FILE_WRITE_ATTRIBUTES for read-only copies.
            uint access = 0x10000 | 0x80;
            if (copy && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) access |= 0x100;
            handle = NativeFile.CreateFile(path, access, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) throw NativeFile.Error("无法打开文件", path);
        }

        internal FileStamp Stamp(bool hash)
        {
            FileStamp stamp = new FileStamp();
            if (!NativeFile.GetFileInformationByHandle(handle, out stamp.Info))
                throw NativeFile.Error("无法核对文件信息", Path);
            stamp.Changed = ChangeTime();
            if (hash)
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    stamp.Digest = Convert.ToBase64String(sha.ComputeHash(stream));
            }
            return stamp;
        }

        internal long ChangeTime()
        {
            NativeFile.Basic info;
            return NativeFile.GetFileInformationByHandleEx(handle, 0, out info, Marshal.SizeOf(typeof(NativeFile.Basic)))
                ? info.Changed : 0;
        }

        internal void Move(string destination)
        {
            byte[] name = Encoding.Unicode.GetBytes(destination);
            int lengthOffset = 2 * IntPtr.Size;
            int nameOffset = lengthOffset + 4;
            int bytes = nameOffset + name.Length + 2;
            IntPtr info = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.WriteIntPtr(info, IntPtr.Zero); // ReplaceIfExists = FALSE
                Marshal.WriteIntPtr(info, IntPtr.Size, IntPtr.Zero); // RootDirectory
                Marshal.WriteInt32(info, lengthOffset, name.Length);
                Marshal.Copy(name, 0, IntPtr.Add(info, nameOffset), name.Length);
                Marshal.WriteInt16(info, nameOffset + name.Length, 0);
                if (!NativeFile.SetFileInformationByHandle(handle, 3, info, bytes))
                    throw NativeFile.Error("无法移动文件", Path + " → " + destination);
                Path = destination;
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        internal void SetDelete(bool value)
        {
            if (value)
            {
                NativeFile.Info info;
                if (!NativeFile.GetFileInformationByHandle(handle, out info))
                    throw NativeFile.Error("无法读取副本属性", Path);
                FileAttributes attributes = (FileAttributes)info.Attributes;
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    SetAttributes(attributes & ~FileAttributes.ReadOnly);
                    restoredAttributes = attributes;
                }
            }
            byte flag = value ? (byte)1 : (byte)0;
            if (!NativeFile.SetDisposition(handle, 4, ref flag, 1))
            {
                Exception cause = NativeFile.Error("无法更新副本的删除状态", Path);
                RestoreAttributes();
                throw cause;
            }
            if (!value) RestoreAttributes();
        }

        private void RestoreAttributes()
        {
            if (restoredAttributes == 0) return;
            SetAttributes(restoredAttributes);
            restoredAttributes = 0;
        }

        private void SetAttributes(FileAttributes attributes)
        {
            NativeFile.Basic info = new NativeFile.Basic();
            info.Attributes = (uint)(attributes == 0 ? FileAttributes.Normal : attributes);
            if (!NativeFile.SetBasic(handle, 0, ref info, Marshal.SizeOf(typeof(NativeFile.Basic))))
                throw NativeFile.Error("无法更新副本属性", Path);
        }

        public void Dispose() { handle.Dispose(); }
    }

    internal static class NativeFile
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct Info
        {
            internal uint Attributes;
            internal long Created, Accessed, Written;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Basic
        {
            internal long Created, Accessed, Written, Changed;
            internal uint Attributes;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
            IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out Basic info, int bytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr buffer, int bytes);
        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        internal static extern bool SetDisposition(SafeFileHandle handle, int kind, ref byte flag, int bytes);
        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        internal static extern bool SetBasic(SafeFileHandle handle, int kind, ref Basic info, int bytes);

        internal static IOException Error(string action, string path)
        {
            Win32Exception reason = new Win32Exception(Marshal.GetLastWin32Error());
            return new IOException(action + "：" + path + Environment.NewLine + reason.Message, reason);
        }
    }
}
