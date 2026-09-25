using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Wandur.Core.Settings;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop;

/// <summary>Geometry selection is independent of the neighboring color palette menu.</summary>
internal sealed class SkinMenuButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public SkinMenuButton(Func<WorkspaceController> controller)
    {
        Name = "TitleSkinButton";
        Classes.Add("command-bar-button");
        Width = 36; Height = 30; MinWidth = MinHeight = 0;
        Padding = new Thickness(8, 5);
        Bind(ToolTip.TipProperty, LocalizedText.Binding(nameof(L.Skin)));
        Bind(AutomationProperties.NameProperty, LocalizedText.Binding(nameof(L.Skin)));
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse("M 1,1 H 17 V 17 H 1 Z M 1,5 H 17 M 5,5 V 17"),
            Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.4,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });
        Content = glyph;
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var id in WindowSkinId.All)
            {
                var item = new MenuItem { Header = WindowSkinId.DisplayName(id), Name = "Skin" + id,
                    ToggleType = MenuItemToggleType.CheckBox, IsChecked = WindowSkinId.Normalize(controller().Settings.Skin) == id };
                AutomationProperties.SetName(item, WindowSkinId.DisplayName(id));
                item.Click += (_, _) =>
                {
                    var active = controller();
                    try { active.SaveSettings(active.Settings with { Skin = id }); }
                    catch (Exception error) { active.ShowNotice(error.Message); }
                };
                menu.Items.Add(item);
            }
            if (menu.Popup.Child is MenuFlyoutPresenter presenter) presenter.ItemsSource = menu.Items.ToArray();
        };
        Flyout = menu;
    }
}
