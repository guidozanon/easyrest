using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyRest;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // La fila de tabs no muestra scrollbar: la rueda del mouse sobre ella scrollea horizontal
        EventManager.RegisterClassHandler(typeof(TabControl), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(TabStripMouseWheel));
    }

    static void TabStripMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        // Solo actuar si el mouse está sobre la fila de tabs (TabPanel), no sobre el contenido
        DependencyObject? node = e.OriginalSource as DependencyObject;
        TabPanel? tabPanel = null;
        while (node != null && node != tabControl)
        {
            if (node is TabPanel panel) { tabPanel = panel; break; }
            node = GetParent(node);
        }
        if (tabPanel == null) return;

        DependencyObject? parent = GetParent(tabPanel);
        while (parent != null && parent is not ScrollViewer) parent = GetParent(parent);
        if (parent is ScrollViewer { ScrollableWidth: > 0 } scroll)
        {
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    internal static DependencyObject? GetParent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);
}
