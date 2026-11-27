using System;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("批量重命名")]
[assembly: AssemblyDescription("在资源管理器中预览并批量更改文件名")]
[assembly: AssemblyProduct("批量重命名")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace BatchRenamer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            try
            {
                var paths = ShellIntegration.ReadArguments(args);
                var application = new Application();
                return application.Run(new MainWindow(paths));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetBaseException().Message, "批量重命名", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
