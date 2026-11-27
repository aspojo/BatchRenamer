using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace BatchRenamer
{
    public class RulesPanel : UserControl
    {
        private const string View = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' UseLayoutRounding='True'>
    <Grid.Resources>
        <Style x:Key='Tab' TargetType='RadioButton'>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Foreground' Value='#546273'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='RadioButton'>
                        <Border x:Name='Surface' Background='Transparent' Margin='2' CornerRadius='5'
                                BorderThickness='1' BorderBrush='Transparent' Padding='2,5'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Surface' Property='Background' Value='#E8EDF4'/>
                            </Trigger>
                            <Trigger Property='IsChecked' Value='True'>
                                <Setter TargetName='Surface' Property='Background' Value='White'/>
                                <Setter TargetName='Surface' Property='BorderBrush' Value='#DCE3EB'/>
                                <Setter Property='Foreground' Value='#265CDB'/>
                                <Setter Property='FontWeight' Value='SemiBold'/>
                            </Trigger>
                            <Trigger Property='IsKeyboardFocused' Value='True'>
                                <Setter TargetName='Surface' Property='BorderBrush' Value='#265CDB'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key='FieldLabel' TargetType='TextBlock'>
            <Setter Property='Foreground' Value='#26374A'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='13'/>
            <Setter Property='Margin' Value='0,0,0,9'/>
        </Style>
        <Style x:Key='Choice' TargetType='CheckBox'>
            <Setter Property='MinHeight' Value='35'/>
            <Setter Property='VerticalContentAlignment' Value='Center'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='4,0,0,0'/>
        </Style>
    </Grid.Resources>
    <Grid.RowDefinitions>
        <RowDefinition Height='Auto'/>
        <RowDefinition Height='*'/>
        <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <Border Grid.Row='0' Background='#F0F3F8' CornerRadius='7' Padding='2'>
        <UniformGrid Rows='1' Height='34'>
            <RadioButton x:Name='DeleteTab' GroupName='Mode' Style='{StaticResource Tab}' Content='删除' IsChecked='True'/>
            <RadioButton x:Name='ReplaceTab' GroupName='Mode' Style='{StaticResource Tab}' Content='替换'/>
            <RadioButton x:Name='InsertTab' GroupName='Mode' Style='{StaticResource Tab}' Content='插入'/>
            <RadioButton x:Name='NumberTab' GroupName='Mode' Style='{StaticResource Tab}' Content='编号'/>
        </UniformGrid>
    </Border>
    <ScrollViewer Grid.Row='1' Margin='0,18,0,0' VerticalScrollBarVisibility='Auto'
                  HorizontalScrollBarVisibility='Disabled'>
        <Grid>
            <StackPanel x:Name='DeleteFields'>
                <DockPanel LastChildFill='True' Margin='0,0,0,7'>
                    <Button x:Name='HanPreset' DockPanel.Dock='Right' Content='仅留汉字'
                            ToolTip='一键选中需要移除的字符类型，保留汉字及文件扩展名'
                            FontSize='12' MinHeight='27' Padding='9,2'
                            Foreground='#265CDB' Background='#EDF3FF' BorderBrush='#D7E3FD'/>
                    <TextBlock Style='{StaticResource FieldLabel}' VerticalAlignment='Center' Margin='0'
                               Text='删除字符类型'/>
                </DockPanel>
                <UniformGrid Columns='2'>
                    <CheckBox x:Name='Latin' Style='{StaticResource Choice}' Content='英文字母'/>
                    <CheckBox x:Name='Digits' Style='{StaticResource Choice}' Content='数字'/>
                    <CheckBox x:Name='Symbols' Style='{StaticResource Choice}' Content='标点和符号'/>
                    <CheckBox x:Name='Space' Style='{StaticResource Choice}' Content='空白字符'/>
                    <CheckBox x:Name='Han' Style='{StaticResource Choice}' Content='汉字'/>
                    <CheckBox x:Name='Other' Style='{StaticResource Choice}' Content='其他字符'
                              ToolTip='包括假名、韩文、非英语字母和组合标记'/>
                </UniformGrid>
                <Border Height='1' Background='#E7EBF0' Margin='0,15,0,17'/>
                <TextBlock Style='{StaticResource FieldLabel}' Text='指定字符'/>
                <TextBox x:Name='RemoveText' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='指定删除字符'/>
                <TextBlock Foreground='#637385' FontSize='12' Margin='0,7,0,0' TextWrapping='Wrap'
                           Text='输入的每个字符都会被删除。'/>
            </StackPanel>
            <StackPanel x:Name='ReplaceFields' Visibility='Collapsed'>
                <TextBlock Style='{StaticResource FieldLabel}' Text='查找文字'/>
                <TextBox x:Name='FindText' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='查找文字'/>
                <TextBlock Style='{StaticResource FieldLabel}' Margin='0,18,0,9' Text='替换为'/>
                <TextBox x:Name='ReplaceText' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='替换为'/>
                <TextBlock Foreground='#637385' FontSize='12' Margin='0,7,0,10' Text='留空可删除找到的文字。'/>
                <CheckBox x:Name='MatchCase' Style='{StaticResource Choice}' Content='区分大小写' IsChecked='True'/>
            </StackPanel>
            <StackPanel x:Name='InsertFields' Visibility='Collapsed'>
                <TextBlock Style='{StaticResource FieldLabel}' Text='文件名前缀'/>
                <TextBox x:Name='Prefix' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='文件名前缀'/>
                <TextBlock Style='{StaticResource FieldLabel}' Margin='0,18,0,9' Text='文件名后缀'/>
                <TextBox x:Name='Suffix' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='文件名后缀'/>
                <TextBlock Foreground='#637385' FontSize='12' Margin='0,7,0,0' Text='后缀会插入到扩展名之前。'/>
            </StackPanel>
            <StackPanel x:Name='NumberFields' Visibility='Collapsed'>
                <TextBlock Style='{StaticResource FieldLabel}' Text='编号位置'/>
                <StackPanel Orientation='Horizontal' Margin='0,4,0,18'>
                    <RadioButton x:Name='Before' GroupName='Position' Content='文件名前' IsChecked='True' Margin='0,0,18,0'/>
                    <RadioButton x:Name='After' GroupName='Position' Content='文件名后'/>
                </StackPanel>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='*'/>
                    </Grid.ColumnDefinitions>
                    <StackPanel>
                        <TextBlock Style='{StaticResource FieldLabel}' Text='起始编号'/>
                        <TextBox x:Name='Start' Text='1' MaxLength='10' MinHeight='34' VerticalContentAlignment='Center'
                                 AutomationProperties.Name='起始编号'/>
                    </StackPanel>
                    <StackPanel Grid.Column='2'>
                        <TextBlock Style='{StaticResource FieldLabel}' Text='最少位数'/>
                        <ComboBox x:Name='NumberWidth' MinHeight='34' VerticalContentAlignment='Center'
                                  AutomationProperties.Name='编号的最少位数'/>
                    </StackPanel>
                </Grid>
                <TextBlock x:Name='NumberError' Foreground='#B73A38' FontSize='12' Margin='0,5,0,0' Visibility='Collapsed'
                           TextWrapping='Wrap' Text='请输入 0 至 2147483647 之间的整数。'/>
                <TextBlock Style='{StaticResource FieldLabel}' Margin='0,18,0,9' Text='编号与文件名之间的分隔符'/>
                <TextBox x:Name='NumberSeparator' MinHeight='34' VerticalContentAlignment='Center' AutomationProperties.Name='编号分隔符'/>
                <TextBlock Foreground='#637385' FontSize='12' Margin='0,7,0,0' Text='按列表中勾选文件的顺序编号。'/>
            </StackPanel>
        </Grid>
    </ScrollViewer>
    <TextBlock Grid.Row='2' Text='仅应用当前选项卡的规则 · 保留扩展名' FontSize='12'
               Foreground='#637385' TextWrapping='Wrap' Margin='0,16,0,0'/>
</Grid>";

        private readonly Grid view;
        private readonly string[] tabs = { "Delete", "Replace", "Insert", "Number" };
        private readonly string[] characterTypes = { "Latin", "Digits", "Symbols", "Space", "Han", "Other" };
        private bool updating;
        private Func<string, int, string> applyToStem;

        public event EventHandler RulesChanged;

        public RulesPanel()
        {
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            Foreground = new SolidColorBrush(Color.FromRgb(0x18, 0x24, 0x34));
            view = (Grid)XamlReader.Parse(View);
            Content = view;

            ComboBox width = Find<ComboBox>("NumberWidth");
            for (int i = 1; i <= 8; i++) width.Items.Add(i + " 位");
            foreach (string tab in tabs) Find<RadioButton>(tab + "Tab").Checked += OnChanged;
            foreach (string name in characterTypes) HookChoice(name);
            HookChoice("MatchCase");
            Find<RadioButton>("Before").Checked += OnChanged;
            Find<RadioButton>("After").Checked += OnChanged;
            foreach (string name in new[] { "RemoveText", "FindText", "ReplaceText", "Prefix", "Suffix", "Start", "NumberSeparator" })
                Find<TextBox>(name).TextChanged += OnChanged;
            width.SelectionChanged += OnChanged;
            Find<Button>("HanPreset").Click += delegate
            {
                updating = true;
                foreach (string name in characterTypes) Find<CheckBox>(name).IsChecked = name != "Han";
                Find<TextBox>("RemoveText").Clear();
                updating = false;
                Refresh();
            };
            Reset();
        }

        public void Reset()
        {
            updating = true;
            Find<RadioButton>("DeleteTab").IsChecked = true;
            foreach (string name in characterTypes) Find<CheckBox>(name).IsChecked = false;
            foreach (string name in new[] { "RemoveText", "FindText", "ReplaceText", "Prefix", "Suffix" })
                Find<TextBox>(name).Clear();
            Find<CheckBox>("MatchCase").IsChecked = true;
            Find<RadioButton>("Before").IsChecked = true;
            Find<TextBox>("Start").Text = "1";
            Find<TextBox>("NumberSeparator").Text = " - ";
            Find<ComboBox>("NumberWidth").SelectedIndex = 1;
            updating = false;
            Refresh();
        }

        public string Transform(string originalFileName, int sequence)
        {
            if (string.IsNullOrEmpty(originalFileName)) return string.Empty;
            int dot = originalFileName.LastIndexOf('.');
            // A leading dot belongs to the basename of files such as .gitignore.
            string stem = dot > 0 ? originalFileName.Substring(0, dot) : originalFileName;
            string extension = dot > 0 ? originalFileName.Substring(dot) : string.Empty;
            string changed = applyToStem(stem, sequence);
            return changed.Length == 0 ? string.Empty : changed + extension;
        }

        private T Find<T>(string name) where T : FrameworkElement
        {
            return (T)view.FindName(name);
        }

        private void HookChoice(string name)
        {
            ToggleButton choice = Find<ToggleButton>(name);
            choice.Checked += OnChanged;
            choice.Unchecked += OnChanged;
        }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (!updating) Refresh();
        }

        private void Refresh()
        {
            string mode = "Delete";
            foreach (string tab in tabs)
            {
                bool active = Find<RadioButton>(tab + "Tab").IsChecked == true;
                Find<StackPanel>(tab + "Fields").Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                if (active) mode = tab;
            }
            switch (mode)
            {
                case "Replace":
                    string match = Find<TextBox>("FindText").Text;
                    string replacement = Find<TextBox>("ReplaceText").Text;
                    StringComparison comparison = Find<CheckBox>("MatchCase").IsChecked == true ?
                        StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    applyToStem = (stem, seq) => ReplaceLiteral(stem, match, replacement, comparison);
                    break;
                case "Insert":
                    string prefix = Find<TextBox>("Prefix").Text;
                    string suffix = Find<TextBox>("Suffix").Text;
                    applyToStem = (stem, seq) => prefix + stem + suffix;
                    break;
                case "Number":
                    int start;
                    bool valid = int.TryParse(Find<TextBox>("Start").Text, NumberStyles.None, CultureInfo.InvariantCulture, out start);
                    Find<TextBlock>("NumberError").Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
                    bool before = Find<RadioButton>("Before").IsChecked == true;
                    string numberFormat = "D" + (1 + Find<ComboBox>("NumberWidth").SelectedIndex);
                    string separator = Find<TextBox>("NumberSeparator").Text;
                    applyToStem = delegate(string stem, int seq)
                    {
                        if (!valid) return string.Empty;
                        string number = ((long)start + seq - 1).ToString(numberFormat, CultureInfo.InvariantCulture);
                        return before ? number + separator + stem : stem + separator + number;
                    };
                    break;
                default:
                    bool[] remove = Array.ConvertAll(characterTypes, name => Find<CheckBox>(name).IsChecked == true);
                    string literal = Find<TextBox>("RemoveText").Text;
                    var points = new HashSet<int>();
                    for (int i = 0; i < literal.Length; )
                    {
                        int point = CodePointAt(literal, i);
                        points.Add(point);
                        i += point > 0xFFFF ? 2 : 1;
                    }
                    applyToStem = (stem, seq) => Filter(stem, remove, points);
                    break;
            }
            EventHandler changed = RulesChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        private static string ReplaceLiteral(string value, string match, string replacement, StringComparison comparison)
        {
            if (match.Length == 0) return value;
            int found = value.IndexOf(match, comparison);
            if (found < 0) return value;
            var output = new StringBuilder(value.Length);
            int from = 0;
            do
            {
                output.Append(value, from, found - from).Append(replacement);
                from = found + match.Length;
                found = value.IndexOf(match, from, comparison);
            } while (found >= 0);
            return output.Append(value, from, value.Length - from).ToString();
        }

        private static int CodePointAt(string value, int index)
        {
            return char.IsSurrogatePair(value, index) ? char.ConvertToUtf32(value, index) : value[index];
        }

        private enum CharacterType { Latin, Digits, Symbols, Space, Han, Other }

        private static CharacterType Classify(string value, int index, int point)
        {
            if ((point >= 'A' && point <= 'Z') || (point >= 'a' && point <= 'z') ||
                (point >= 0xFF21 && point <= 0xFF3A) || (point >= 0xFF41 && point <= 0xFF5A))
                return CharacterType.Latin;
            if (point == 0x3007 || (point >= 0x3400 && point <= 0x4DBF) ||
                (point >= 0x4E00 && point <= 0x9FFF) || (point >= 0xF900 && point <= 0xFAFF) ||
                (point >= 0x20000 && point <= 0x2EE5F) || (point >= 0x2F800 && point <= 0x2FA1F) ||
                (point >= 0x30000 && point <= 0x3347F)) return CharacterType.Han;
            if (char.IsWhiteSpace(value, index)) return CharacterType.Space;
            if (char.IsDigit(value, index)) return CharacterType.Digits;
            if (char.IsPunctuation(value, index) || char.IsSymbol(value, index)) return CharacterType.Symbols;
            return CharacterType.Other;
        }

        private static string Filter(string value, bool[] remove, HashSet<int> literal)
        {
            var output = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; )
            {
                int point = CodePointAt(value, i);
                int length = point > 0xFFFF ? 2 : 1;
                if (!literal.Contains(point) && !remove[(int)Classify(value, i, point)])
                    output.Append(value, i, length);
                i += length;
            }
            return output.ToString();
        }
    }
}
