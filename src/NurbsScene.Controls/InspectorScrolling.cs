using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Allows a single-line inspector field to pass wheel motion to the
/// containing inspector. TextBox's template owns an inner ScrollViewer; on some
/// Uno hosts it consumes the wheel even though the field cannot scroll vertically.
/// Multiline editors, number spinners and modified/horizontal wheels retain their
/// native handling. No application model or editor value is changed here.</summary>
internal static class InspectorScrolling
{
    public static void Attach(TextBox field, ScrollViewer inspector)
    {
        field.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, args) =>
            {
                if (field.AcceptsReturn || field.TextWrapping != TextWrapping.NoWrap ||
                    (args.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)) != 0)
                    return;
                for (DependencyObject? parent = VisualTreeHelper.GetParent(field);
                     parent is not null && !ReferenceEquals(parent, inspector);
                     parent = VisualTreeHelper.GetParent(parent))
                    if (parent is NumberBox) return;
                var pointer = args.GetCurrentPoint(field);
                if (pointer.Properties.IsHorizontalMouseWheel || inspector.ScrollableHeight <= 0) return;
                var target = Math.Clamp(inspector.VerticalOffset - pointer.Properties.MouseWheelDelta * .4,
                    0, inspector.ScrollableHeight);
                if (target == inspector.VerticalOffset) return;
                args.Handled = true;
                inspector.ChangeView(null, target, null, disableAnimation: true);
            }), handledEventsToo: true);
    }
}
