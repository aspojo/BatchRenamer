using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace BatchRenamer
{
    public sealed class FileEntry : INotifyPropertyChanged
    {
        private bool included = true;
        private string source;
        private string preview;
        private string problem = "";
        public FileEntry(string path) { source = path; preview = Original; }
        public string Source
        {
            get { return source; }
            set { source = value; Notify("Source"); Notify("Original"); Notify("Changed"); }
        }
        public string Original { get { return IOPath.GetFileName(source); } }
        public string ManualName { get; set; }
        public string Preview
        {
            get { return preview; }
            set { if (value != preview) { preview = value; Notify("Preview"); Notify("Changed"); } }
        }
        public bool Included
        {
            get { return included; }
            set { if (included != value) { included = value; Notify("Included"); Notify("Changed"); } }
        }
        public bool Changed { get { return Included && Original != Preview; } }
        public string Problem
        {
            get { return problem; }
            set { if (problem != value) { problem = value; Notify("Problem"); } }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class MainWindow : Window
    {
        private readonly Grid view;
        private readonly RulesPanel rules = new RulesPanel();
        private readonly ObservableCollection<FileEntry> rows = new ObservableCollection<FileEntry>();
        private readonly DispatcherTimer previewTimer = new DispatcherTimer();
        private readonly ListBox list;
        private readonly Button apply, undo;
        private readonly CheckBox selectAll;
        private readonly RadioButton copyMode;
        private int revision;
        private bool bulkEdit, busy, sortingDescending;
        private BatchResult lastBatch;
        // Maps names produced by the last in-place rename to their original paths.
        private Dictionary<string, string> restorePaths;
        private HashSet<string> createdCopies;

        public MainWindow(IEnumerable<string> initialPaths)
        {
            Title = "批量重命名";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MinWidth = Math.Min(840, SystemParameters.WorkArea.Width);
            MinHeight = Math.Min(535, SystemParameters.WorkArea.Height);
            Width = Math.Min(1080, SystemParameters.WorkArea.Width);
            Height = Math.Min(655, SystemParameters.WorkArea.Height);
            UseLayoutRounding = true;
            var assembly = Assembly.GetExecutingAssembly();
            using (var icon = assembly.GetManifestResourceStream("App.ico"))
                Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            using (var stream = assembly.GetManifestResourceStream("MainView.xaml"))
                view = (Grid)XamlReader.Load(stream);
            Content = view;
            list = Find<ListBox>("Files");
            apply = Find<Button>("Apply");
            undo = Find<Button>("Undo");
            selectAll = Find<CheckBox>("SelectAll");
            copyMode = Find<RadioButton>("CopyMode");
            list.ItemsSource = rows;
            Find<ContentControl>("RuleHost").Content = rules;
            previewTimer.Interval = TimeSpan.FromMilliseconds(95);
            previewTimer.Tick += RefreshPreview;
            rules.RulesChanged += delegate { SchedulePreview(); };
            copyMode.Checked += delegate { SchedulePreview(); };
            copyMode.Unchecked += delegate { SchedulePreview(); };
            selectAll.Click += delegate
            {
                bool include = rows.Any(r => !r.Included);
                bulkEdit = true;
                foreach (var row in rows) row.Included = include;
                bulkEdit = false;
                SchedulePreview();
            };
            Find<Button>("AddFiles").Click += delegate { ChooseFiles(); };
            Find<Button>("Clear").Click += delegate { rows.Clear(); SchedulePreview(); };
            Find<Button>("Sort").Click += delegate { SortFiles(); };
            apply.Click += delegate { ApplyChanges(); };
            undo.Click += delegate { UndoChanges(); };
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(EditButtonClick));
            AllowDrop = true;
            PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += delegate(object sender, DragEventArgs e)
            {
                if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
                e.Handled = true;
            };
            PreviewKeyDown += HandleKey;
            Closing += delegate(object sender, CancelEventArgs e) { if (busy) e.Cancel = true; };
            Closed += delegate { ++revision; previewTimer.Stop(); };
            AddPaths(initialPaths);
        }

        private T Find<T>(string name) where T : FrameworkElement { return (T)view.FindName(name); }
        private void Status(string message)
        {
            var label = Find<TextBlock>("Notice");
            label.Text = message;
            label.ToolTip = message;
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var known = new HashSet<string>(rows.Select(r => r.Source), StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            foreach (var path in paths)
            {
                if (!File.Exists(path)) { skipped++; continue; }
                string full = IOPath.GetFullPath(path);
                if (!known.Add(full)) continue;
                var entry = new FileEntry(full);
                entry.PropertyChanged += delegate(object sender, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == "Included" && !bulkEdit) SchedulePreview();
                };
                rows.Add(entry);
            }
            SchedulePreview();
            if (skipped > 0) Status("已跳过 " + skipped + " 个文件夹或不可用项");
        }

        private void ChooseFiles()
        {
            var picker = new OpenFileDialog { Title = "添加待重命名的文件", Multiselect = true, Filter = "所有文件 (*.*)|*.*" };
            if (picker.ShowDialog(this) == true) AddPaths(picker.FileNames);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string left, string right);
        private void SortFiles()
        {
            var sorted = rows.ToList();
            sorted.Sort(delegate(FileEntry a, FileEntry b)
            {
                return sortingDescending ? StrCmpLogicalW(b.Original, a.Original) : StrCmpLogicalW(a.Original, b.Original);
            });
            Find<Button>("Sort").Content = sortingDescending ? "原文件名 ↓" : "原文件名 ↑";
            sortingDescending = !sortingDescending;
            rows.Clear();
            foreach (var row in sorted) rows.Add(row);
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            ++revision;
            apply.IsEnabled = false;
            undo.IsEnabled = lastBatch != null && !busy;
            Find<FrameworkElement>("Empty").Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("Total").Text = rows.Count.ToString();
            Find<Button>("Clear").IsEnabled = selectAll.IsEnabled = rows.Count > 0;
            previewTimer.Stop();
            previewTimer.Start();
        }

        private async void RefreshPreview(object sender, EventArgs e)
        {
            previewTimer.Stop();
            if (busy) return;
            int version = revision;
            bool copy = copyMode.IsChecked == true;
            var selected = new List<FileEntry>();
            apply.Content = copy ? "创建副本" : "重命名";
            try
            {
                foreach (var row in rows)
                {
                    row.Problem = "";
                    if (row.Included) selected.Add(row);
                    row.Preview = row.Included ? row.ManualName ?? rules.Transform(row.Original, selected.Count) : row.Original;
                }
                selectAll.IsChecked = selected.Count == 0 ? false : selected.Count == rows.Count ? true : (bool?)null;
                int changed = selected.Count(r => r.Changed);
                string[] errors = new string[selected.Count];
                if (changed > 0)
                {
                    var items = ItemsFor(selected);
                    errors = await Task.Run(() => RenameEngine.Validate(items, copy));
                    if (version != revision) return;
                }
                int conflicts = 0;
                for (int i = 0; i < selected.Count; ++i)
                {
                    if (string.IsNullOrEmpty(errors[i])) continue;
                    selected[i].Problem = errors[i];
                    conflicts++;
                }
                string summary = "已勾选 " + selected.Count + " 项";
                if (changed > 0) summary += "   ·   " + changed + " 项将" + (copy ? "创建副本" : "重命名");
                if (conflicts > 0) summary += "   ·   " + conflicts + " 项需处理";
                Find<TextBlock>("Counts").Text = summary;
                apply.IsEnabled = changed > 0 && conflicts == 0;
            }
            catch (Exception ex)
            {
                if (version == revision) Find<TextBlock>("Counts").Text = ex.Message;
            }
        }

        private static List<RenameItem> ItemsFor(IList<FileEntry> entries)
        {
            return entries.Select(r => new RenameItem(r.Source, r.Preview)).ToList();
        }

        private void SetBusy(bool value)
        {
            busy = value;
            ++revision;
            view.IsEnabled = !value;
            Cursor = value ? Cursors.Wait : null;
            if (!value) SchedulePreview();
        }
        private async void ApplyChanges()
        {
            if (busy || !apply.IsEnabled) return;
            bool copy = copyMode.IsChecked == true;
            var selected = rows.Where(r => r.Included).ToList();
            var items = ItemsFor(selected);
            SetBusy(true);
            Status(copy ? "正在创建副本…" : "正在重命名…");
            try
            {
                var result = await Task.Run(() => RenameEngine.Apply(items, copy));
                var restore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var copies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < items.Count; i++)
                {
                    string original = items[i].SourcePath;
                    if (IOPath.GetFileName(original) != items[i].NewName)
                    {
                        string current = IOPath.Combine(IOPath.GetDirectoryName(original), items[i].NewName);
                        if (copy) copies.Add(current);
                        else
                        {
                            restore.Add(current, original);
                            selected[i].Source = current;
                        }
                    }
                    selected[i].ManualName = null;
                }
                restorePaths = restore;
                createdCopies = copies;
                lastBatch = result;
                rules.Reset();
                Status((copy ? "已创建副本 " : "已重命名 ") + result.Count + " 个文件");
            }
            catch (Exception ex) { OperationError(ex); }
            finally { SetBusy(false); }
        }

        private async void UndoChanges()
        {
            if (busy || lastBatch == null) return;
            SetBusy(true);
            Status("正在撤销…");
            try
            {
                await Task.Run(() => lastBatch.Undo());
                foreach (var row in rows.ToList())
                {
                    string original;
                    if (restorePaths.TryGetValue(row.Source, out original)) row.Source = original;
                    else if (createdCopies.Contains(row.Source)) rows.Remove(row);
                }
                Status("已撤销 " + lastBatch.Count + " 个文件的操作");
                lastBatch = null;
                restorePaths = null;
                createdCopies = null;
            }
            catch (Exception ex) { OperationError(ex); }
            finally { SetBusy(false); }
        }
        private void OperationError(Exception ex)
        {
            Status("操作未完成");
            MessageBox.Show(this, ex.Message, "批量重命名", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void EditButtonClick(object sender, RoutedEventArgs e)
        {
            var button = e.OriginalSource as Button;
            if (button != null && button.Name == "EditName") Edit((FileEntry)button.DataContext);
        }
        private void Edit(FileEntry row)
        {
            if (row == null || busy) return;
            string suffix = row.Original.LastIndexOf('.') > 0 ? IOPath.GetExtension(row.Original) : "";
            var dialog = new Window {
                Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = "编辑文件名",
                SizeToContent = SizeToContent.Height, Width = 450, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, Icon = Icon, FontFamily = view.GetValue(TextBlock.FontFamilyProperty) as FontFamily
            };
            var box = new StackPanel { Margin = new Thickness(20) };
            dialog.Content = box;
            box.Children.Add(new TextBlock { Text = "原文件名", Foreground = Brushes.SlateGray });
            box.Children.Add(new TextBlock { Text = row.Original, Margin = new Thickness(0, 3, 0, 15), TextWrapping = TextWrapping.Wrap });
            box.Children.Add(new TextBlock { Text = "新文件名" + (suffix.Length == 0 ? "" : "（保留 " + suffix + "）") });
            string current = row.Included ? row.Preview : row.ManualName ?? row.Original;
            string stem = current.EndsWith(suffix, StringComparison.Ordinal) ? current.Substring(0, current.Length - suffix.Length) : current;
            var input = new TextBox { Text = stem, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 6, 0, 16) };
            box.Children.Add(input);
            var buttons = new DockPanel();
            box.Children.Add(buttons);
            var reset = new Button { Content = "使用规则生成", Style = (Style)view.FindResource(typeof(Button)) };
            DockPanel.SetDock(reset, Dock.Left);
            buttons.Children.Add(reset);
            var confirm = new Button { Content = "确定", IsDefault = true, Width = 76, HorizontalAlignment = HorizontalAlignment.Right, Style = reset.Style };
            buttons.Children.Add(confirm);
            bool useRule = false;
            reset.Click += delegate { useRule = true; dialog.DialogResult = true; };
            confirm.Click += delegate { dialog.DialogResult = true; };
            dialog.ContentRendered += delegate { input.Focus(); input.SelectAll(); };
            dialog.PreviewKeyDown += delegate(object s, KeyEventArgs key) { if (key.Key == Key.Escape) dialog.Close(); };
            if (dialog.ShowDialog() == true)
            {
                row.ManualName = useRule ? null : input.Text.Length == 0 ? "" : input.Text + suffix;
                row.Included = true;
                SchedulePreview();
            }
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (busy) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) ChooseFiles();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z && !(Keyboard.FocusedElement is TextBox)) UndoChanges();
            else if (e.Key == Key.F2 && list.IsKeyboardFocusWithin) Edit(list.SelectedItem as FileEntry);
            else if (e.Key == Key.Delete && list.IsKeyboardFocusWithin)
            {
                foreach (var row in list.SelectedItems.Cast<FileEntry>().ToArray()) rows.Remove(row);
                SchedulePreview();
            }
            else return;
            e.Handled = true;
        }
    }
}
