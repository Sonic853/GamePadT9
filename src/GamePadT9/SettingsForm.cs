using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;

namespace GamePadT9;

internal sealed partial class SettingsForm : PanelWindow
{
    internal event Action? ProgramsRequested;
    private sealed record Choice(string? Id, string Name, GamepadFamily Family)
    { public override string ToString() => Name; }
    private readonly Func<IReadOnlyList<GamepadDevice>> connected;
    private readonly Func<string?> deviceError;
    private readonly Action<UserSettings> save;
    private readonly IntegrationInstaller? integration;
    private readonly List<SteamGlyph> icons = [];
    private IReadOnlyList<GamepadDevice>? lastDevices;
    private ComponentState? componentState;
    private bool updating, operating;
    private Choice Selection => ControllerSelector.SelectedItem as Choice ?? new(null, "自动选择", GamepadFamily.Xbox);
    private GamepadFamily Family => Selection.Id == null ? connected().FirstOrDefault()?.Family ?? GamepadFamily.Xbox : Selection.Family;
    internal bool IsOperating => operating;
    internal UserSettings Draft => new()
    {
        ControllerId = Selection.Id, ControllerName = Selection.Id == null ? null : Selection.Name.Replace("（未连接）", ""), ControllerFamily = Selection.Id == null ? GamepadFamily.Xbox : Selection.Family,
        Stick = (ControlSide)StickSelector.SelectedIndex, Trigger = (ControlSide)TriggerSelector.SelectedIndex,
        EnglishCaseShoulder = (ControlSide)EnglishCaseSelector.SelectedIndex,
        EnglishKeepGroup = KeepEnglishGroup.IsChecked == true,
        PinyinLayout = PinyinLayoutPicker.SelectedLayout, PinyinCustomOrder = PinyinLayoutPicker.CustomOrder,
        EnglishLayout = EnglishLayoutPicker.SelectedLayout, EnglishCustomOrder = EnglishLayoutPicker.CustomOrder,
        EnglishUsePinyinLayout = ReusePinyinLayout.IsChecked == true,
        PanelOpacity = Percent(OpacityValue0), GridOpacity = Percent(OpacityValue1), HighlightOpacity = Percent(OpacityValue2),
        PanelBlur = Percent(BlurValue)
    };
    internal SettingsForm(UserSettings settings, Action<UserSettings> save, Func<IReadOnlyList<GamepadDevice>>? connected = null, Func<string?>? deviceError = null, IntegrationInstaller? integration = null)
    {
        this.save = save; this.connected = connected ?? (() => []); this.deviceError = deviceError ?? (() => null); this.integration = integration;
        InitializeComponent();
        BundledRuntimeStatus.Visibility = integration?.UsesBundledEngine == true ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (token, text) in new[] { ("LS", "左摇杆"), ("RS", "右摇杆") }) StickSelector.Items.Add(BindingChoice(token, text));
        foreach (var (token, text) in new[] { ("LT", "左扳机"), ("RT", "右扳机") }) TriggerSelector.Items.Add(BindingChoice(token, text));
        foreach (var (token, text) in new[] { ("LB", "左肩键"), ("RB", "右肩键") }) EnglishCaseSelector.Items.Add(BindingChoice(token, text));
        ActivationKeys.Children.Add(CreateGlyph("View")); ActivationKeys.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) }); ActivationKeys.Children.Add(CreateGlyph("Menu"));
        var sliders = new[] { OpacitySlider0, OpacitySlider1, OpacitySlider2, BlurSlider };
        var numbers = new[] { OpacityValue0, OpacityValue1, OpacityValue2, BlurValue };
        for (var i = 0; i < sliders.Length; i++)
        {
            var index = i;
            sliders[i].ValueChanged += (_, _) =>
            {
                if (updating) return; updating = true;
                try { numbers[index].Value = Math.Round(sliders[index].Value); } finally { updating = false; }
                Preview();
            };
            numbers[i].ValueChanged += (_, _) =>
            {
                if (updating || numbers[index].Value is not double value || !double.IsFinite(value)) return;
                updating = true; try { sliders[index].Value = value; } finally { updating = false; } Preview();
            };
        }
        StickSelector.SelectionChanged += (_, _) => Preview(); TriggerSelector.SelectionChanged += (_, _) => Preview();
        EnglishCaseSelector.SelectionChanged += (_, _) => Preview();
        PinyinLayoutPicker.Changed += RefreshLayouts; EnglishLayoutPicker.Changed += Preview;
        ControllerSelector.SelectionChanged += (_, _) => { Preview(); UpdateDeviceStatus(); };
        SetDraft(settings); RefreshComponents(); ShowPage(0);
        Closing += (_, e) => { if (operating) e.Cancel = true; };
    }
    private ComboBoxItem BindingChoice(string token, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateGlyph(token)); row.Children.Add(new TextBlock { Text = text, Margin = new(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = row, Tag = text };
    }
    private SteamGlyph CreateGlyph(string token) { var icon = new SteamGlyph { Token = token, Family = Family }; icons.Add(icon); return icon; }
    private static int Percent(NumberBox box) => box.Value is double value && double.IsFinite(value) && value is >= 0 and <= 100
        ? (int)Math.Round(value) : throw new InvalidDataException("请为可见度和背景模糊程度输入 0 到 100 之间的数字。");
    internal void SetDraft(UserSettings value)
    {
        updating = true;
        try
        {
            RebuildDevices(value.ControllerId, value.ControllerName, value.ControllerFamily);
            StickSelector.SelectedIndex = (int)value.Stick; TriggerSelector.SelectedIndex = (int)value.Trigger;
            EnglishCaseSelector.SelectedIndex = (int)value.EnglishCaseShoulder;
            KeepEnglishGroup.IsChecked = value.EnglishKeepGroup;
            PinyinLayoutPicker.SetValue(value.PinyinLayout, value.PinyinCustomOrder);
            EnglishLayoutPicker.SetValue(value.EnglishLayout, value.EnglishCustomOrder);
            ReusePinyinLayout.IsChecked = value.EnglishUsePinyinLayout;
            OpacityValue0.Value = OpacitySlider0.Value = value.PanelOpacity;
            OpacityValue1.Value = OpacitySlider1.Value = value.GridOpacity;
            OpacityValue2.Value = OpacitySlider2.Value = value.HighlightOpacity;
            BlurValue.Value = BlurSlider.Value = value.PanelBlur;
        }
        finally { updating = false; }
        RefreshLayouts(); Preview();
    }
    private void LayoutReuseChanged(object sender, RoutedEventArgs e) => RefreshLayouts();
    private void RefreshLayouts()
    {
        if (updating || EnglishLayoutPicker == null) return;
        var reuse = ReusePinyinLayout.IsChecked == true;
        EnglishLayoutPicker.IsEnabled = !reuse;
        EnglishLayoutPicker.Inherit(reuse ? PinyinLayoutPicker.SelectedLayout : null, PinyinLayoutPicker.CustomOrder);
        EnglishLayoutHint.Text = reuse ? "正在使用上方的拼音排序；取消勾选后恢复英文独立设置。" : "英文独立排序；选中自定义后可拖拽交换。";
        Preview();
    }
    private void Preview()
    {
        if (updating || StickSelector.SelectedIndex < 0 || TriggerSelector.SelectedIndex < 0 || EnglishCaseSelector.SelectedIndex < 0) return;
        try { VisibilityPreview.Settings = Draft; } catch (InvalidDataException) { }
        foreach (var icon in icons) icon.Family = Family;
    }
    internal void RefreshDevices()
    {
        var devices = connected();
        if (lastDevices == null || !devices.SequenceEqual(lastDevices))
        {
            var current = Selection; var wasUpdating = updating; updating = true;
            try { RebuildDevices(current.Id, current.Name.Replace("（未连接）", ""), current.Family); } finally { updating = wasUpdating; }
            Preview();
        }
        UpdateDeviceStatus();
    }
    private void RebuildDevices(string? id, string? name, GamepadFamily family)
    {
        lastDevices = connected().ToArray();
        var choices = new List<Choice> { new(null, "自动选择（保持当前手柄）", GamepadFamily.Xbox) };
        choices.AddRange(lastDevices.Select(pad => new Choice(pad.Id, $"{pad.Name} · #{pad.Instance}", pad.Family)));
        if (id != null && !lastDevices.Any(p => p.Id == id)) choices.Add(new(id, (name ?? "已保存的手柄") + "（未连接）", family));
        ControllerSelector.ItemsSource = choices; ControllerSelector.SelectedItem = choices.First(item => item.Id == id);
        UpdateDeviceStatus();
    }
    private void UpdateDeviceStatus()
    {
        var error = deviceError(); var count = connected().Count;
        DeviceStatus.Text = error != null ? "手柄读取失败：" + error :
            Selection.Id != null && !connected().Any(p => p.Id == Selection.Id) ? "所选手柄未连接，等待重新连接。" :
            count == 0 ? "尚未连接手柄，请通过 USB 或蓝牙连接。" : $"已连接 {count} 个手柄，设备列表会自动更新。";
        DeviceBadge.Text = count == 0 ? "等待连接" : $"{count} 个已连接";
    }
    private void Navigate(object sender, SelectionChangedEventArgs e)
    { if (InputPage != null && ReferenceEquals(e.OriginalSource, SettingsNavigation)) ShowPage(SettingsNavigation.SelectedIndex); }
    internal void ShowPage(int index)
    {
        InputPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ComponentPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        LayoutPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        SettingsNavigation.SelectedIndex = index;
        if (index == 2 && !operating) RefreshComponents();
    }
    private void RefreshComponents()
    {
        try
        {
            componentState = integration?.State(); ComponentStatus.Text = componentState?.Description ?? "组件管理不可用。";
            StandaloneState.Text = componentState?.Standalone == true ? "已注册" : "未注册";
            XiaobaiState.Text = componentState?.Injected == true ? "已注入" : "未注入";
            ToggleStandalone.Content = componentState?.Standalone == true ? "卸载 GamePad T9 输入法" : "注册 GamePad T9 输入法";
            ToggleXiaobai.Content = componentState?.Injected == true ? "还原小白 T9 输入" : "注入小白 T9 输入";
            UpdateComponentButtons();
        }
        catch (Exception ex) { componentState = null; ComponentStatus.Text = ex.Message; UpdateComponentButtons(); }
    }
    private void UpdateComponentButtons()
    {
        ToggleStandalone.IsEnabled = !operating && componentState != null && (componentState.Standalone || componentState.CanRegister);
        ToggleXiaobai.IsEnabled = !operating && componentState != null && (componentState.Injected || componentState.CanInject);
    }
    private async Task RunComponent(ComponentAction action)
    {
        if (operating || integration == null) return;
        operating = true; UpdateComponentButtons(); Footer.IsEnabled = false; OpenProgramProfiles.IsEnabled = false; ComponentProgress.Visibility = Visibility.Visible;
        ComponentStatus.Text = "正在处理，请完成 Windows 管理员授权…";
        try
        {
            var result = await integration.RequestAsync(action); RefreshComponents();
            ComponentStatus.Text = result.Message + "\n" + (componentState?.Description ?? "");
        }
        catch (Exception ex) { RefreshComponents(); ComponentStatus.Text = "操作未完成：" + ex.Message; }
        finally { operating = false; Footer.IsEnabled = true; OpenProgramProfiles.IsEnabled = true; ComponentProgress.Visibility = Visibility.Collapsed; UpdateComponentButtons(); }
    }
    private async void ToggleStandaloneClick(object sender, RoutedEventArgs e) => await RunComponent(componentState?.Standalone == true ? ComponentAction.UnregisterStandalone : ComponentAction.RegisterStandalone);
    private async void ToggleXiaobaiClick(object sender, RoutedEventArgs e) => await RunComponent(componentState?.Injected == true ? ComponentAction.RestoreXiaobai : ComponentAction.InjectXiaobai);
    private void OpenPrograms(object sender, RoutedEventArgs e) => ProgramsRequested?.Invoke();
    private void Defaults(object sender, RoutedEventArgs e) { SetDraft(new()); SaveError.IsOpen = false; }
    private void Cancel(object sender, RoutedEventArgs e) { if (!operating) Close(); }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (operating) return;
        try { var value = Draft; value.Validate(); save(value); Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { SaveError.Message = "无法保存设置：" + ex.Message; SaveError.IsOpen = true; }
    }
}
