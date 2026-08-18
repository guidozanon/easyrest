using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Services;
using EasyRest.Services.Sync;

using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>La cuarta pestaña: el sync y lo que se usa de vez en cuando.
///
/// Es lo que antes eran cinco botones apretados en el encabezado de la app. Nada de esto se toca
/// mientras se trabaja —conectar un servidor se hace una vez, importar cada tanto, el diagnóstico
/// casi nunca—, así que sacarlos de la pantalla de trabajo es lo que le devuelve el ancho a la
/// lista de requests.
///
/// Arriba va el estado del sync, que sí se mira seguido: si el workspace está atado, contra qué
/// servidor, y el botón para forzar la bajada.</summary>
internal class MasView : UserControl
{
    readonly Func<Task> _alSincronizar;
    readonly ContentControl _sync = new();
    readonly TextBlock _estado = Ui.Nota("");

    public MasView(Func<Task> alSincronizar, Action alServidor, Action alImportar, Action alDiagnóstico)
    {
        _alSincronizar = alSincronizar;

        var pila = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 24),
            Spacing = 0,
            Children = { _sync }
        };

        var estado = new Border { Padding = new Thickness(16, 8, 16, 12), Child = _estado };
        pila.Children.Add(estado);

        pila.Children.Add(Fila(Iconos.Nube, "Servidor de sync",
            "Conectar el teléfono y elegir workspace", alServidor));
        pila.Children.Add(Fila(Iconos.Bajar, "Importar",
            "Un OpenAPI por link o pegado, o un cURL", alImportar));
        pila.Children.Add(Fila(Iconos.Aviso, "Diagnóstico",
            "Qué ve la app del sistema y de la red", alDiagnóstico));

        var versión = Assembly.GetExecutingAssembly().GetName().Version;
        var pie = Ui.Nota($"EasyRest {versión?.ToString(3) ?? "—"} · Android");
        pie.Margin = new Thickness(16, 20, 16, 0);
        pie.TextAlignment = TextAlignment.Center;
        pila.Children.Add(pie);

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(Ui.Titulo("Más"));
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(new ScrollViewer { Content = pila });

        Content = raíz;
        Refrescar();
    }

    /// <summary>La llama el shell cada vez que se entra a la pestaña: el binding puede haber
    /// cambiado en la pantalla del servidor, y una tarjeta que dice «sin servidor» después de
    /// haberte conectado es peor que no tener tarjeta.</summary>
    public void Refrescar()
    {
        var binding = SyncBinding.Load(Storage.SyncBindingFile);

        if (!binding.IsSet)
        {
            _sync.Content = TarjetaDeSync(
                Ui.CTenue, "Sin servidor de sync",
                "Las colecciones viven sólo en este teléfono. Conectá un servidor y bajan solas, " +
                "junto con los ambientes.", false);
            return;
        }

        var vencida = SyncAccountStore.Default.Find(binding.ServerUrl) == null;
        _sync.Content = vencida
            ? TarjetaDeSync(Ui.CDurazno, binding.WorkspaceName,
                "La sesión venció. Entrá a «Servidor de sync» y volvé a conectarte.", false)
            : TarjetaDeSync(Ui.CVerde, binding.WorkspaceName, binding.ServerUrl, true);
    }

    Control TarjetaDeSync(Color color, string título, string detalle, bool puedeSincronizar)
    {
        var encabezado = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Ui.Icono(Iconos.Nube, 20, new SolidColorBrush(color)),
                new TextBlock
                {
                    Text = título,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Ui.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var pila = new StackPanel
        {
            Spacing = 12,
            Children = { encabezado, Ui.Parrafo(detalle, Ui.Subtexto, 12) }
        };

        if (puedeSincronizar)
            pila.Children.Add(Ui.PrimarioAsync("Sincronizar", Iconos.Sincronizar, SincronizarAsync));

        return new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Tinte(color, 0.28),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(16, 0),
            Child = pila
        };
    }

    async Task SincronizarAsync()
    {
        _estado.Text = "Sincronizando…";
        await _alSincronizar();
        Refrescar();
    }

    /// <summary>Lo que el shell quiera contar de la última sincronización.</summary>
    public void Contar(string texto) => _estado.Text = texto;

    static Control Fila(Geometry icono, string título, string detalle, Action al)
    {
        var textos = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = título, FontSize = 14, Foreground = Ui.Normal },
                Ui.Nota(detalle)
            }
        };

        var contenido = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Ui.Icono(icono, 19, Ui.Subtexto), textos }
        };

        var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(contenido, 0);
        var chevron = Ui.Icono(Iconos.Chevron, 14, Ui.Tenue);
        Grid.SetColumn(chevron, 1);
        fila.Children.Add(contenido);
        fila.Children.Add(chevron);

        return Ui.Fila(fila, al, 64);
    }
}
