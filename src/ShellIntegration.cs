using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BatchRenamer
{
    public static class ShellIntegration
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(IntPtr handle);

        public static string[] ReadArguments(string[] args)
        {
            if (args == null) return new string[0];
            if (args.Length == 0 || args[0] != "--selection-handle") return args;

            long number;
            if (args.Length != 2 || !long.TryParse(args[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out number) || number <= 0 ||
                (IntPtr.Size == 4 && number > Int32.MaxValue))
                throw new ArgumentException("资源管理器传入的文件列表句柄无效。");

            IntPtr raw = new IntPtr(number);
            uint flags;
            const uint HandleFlagInherit = 1, FileTypeDisk = 1;
            if (!GetHandleInformation(raw, out flags) || (flags & HandleFlagInherit) == 0 ||
                GetFileType(raw) != FileTypeDisk)
                throw new IOException("无法读取资源管理器传入的文件列表。");

            // The shell passes a delete-on-close file containing one UTF-16LE path per line.
            using (var handle = new SafeFileHandle(raw, true))
            using (var stream = new FileStream(handle, FileAccess.Read))
            using (var reader = new StreamReader(stream, new UnicodeEncoding(false, false, true), false))
            {
                var paths = new List<string>();
                string path;
                while ((path = reader.ReadLine()) != null)
                {
                    if (path.Length == 0 || path.Length > 32767 || path.IndexOf('\0') >= 0 ||
                        !Path.IsPathRooted(path))
                        throw new InvalidDataException("资源管理器传入的文件列表包含无效路径。");
                    paths.Add(path);
                }
                if (paths.Count == 0) throw new InvalidDataException("资源管理器传入的文件列表为空。");
                return paths.ToArray();
            }
        }
    }
}
