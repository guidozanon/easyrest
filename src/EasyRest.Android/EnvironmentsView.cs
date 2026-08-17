using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using EasyRest.Models;
using EasyRest.Services;

using Button = Avalonia.Controls.Button;

namespace EasyRest.Android;

/// <summary>Ambientes y sus variables, que es lo que hace que la misma colección sirva contra
/// producción y contra la de pruebas sin editar una URL.
///
/// Una variable se puede marcar como secreta y entonces su valor va enmascarado, con un botón
/// para verlo. No es criptografía —en el disco del teléfono sigue estando en claro, como en el
/// escritorio—: es que un token no tiene por qué quedar a la vista mientras mostrás la pantalla
/// en una reunión o en el subte. La marca se guarda con el mismo nombre (<c>secretKeys</c>) que
/// usa el documento que viaja al servidor de sync, así que el día que la app sincronice ambientes
/// ya está expresado lo que hay que cifrar.</summary>
internal class EnvironmentsView : UserControl
{
    readonly Action _alCambiar;
    readonly StackPanel _pila = new() { Margin = new Thickness(12, 0, 12, 16), Spacing = 12 };

    List<EnvironmentModel> _ambientes;
    EnvironmentModel? _actual;

    public EnvironmentsView(List<EnvironmentModel> ambientes, EnvironmentModel? activo, Action alCambiar)
    {
        _ambientes = ambientes;
        _actual = activo ?? ambientes.FirstOrDefault();
        _alCambiar = alCambiar;

        Content = new ScrollViewer { Content = _pila };
        Redibujar();
    }

    void Guardar()
    {
        Storage.SaveEnvironments(_ambientes);
        _alCambiar();
    }

    void Redibujar()
    {
        _pila.Children.Clear();

        var selector = new WrapPanel();
        foreach (var ambiente in _ambientes)
        {
            var cual = ambiente;
            var boton = Ui.Opcion(ambiente.Name, ReferenceEquals(ambiente, _actual), () =>
            {
                _actual = cual;
                Storage.SetActiveEnvironmentId(cual.Id);
                _alCambiar();
                Redibujar();
            });
            boton.Margin = new Thickness(0, 0, 6, 6);
            selector.Children.Add(boton);
        }
        _pila.Children.Add(selector);

        _pila.Children.Add(Ui.Barra(
            Ui.Accion("+ Ambiente", NuevoAmbiente),
            Ui.Accion("Renombrar", Renombrar),
            Ui.Accion("Eliminar", Eliminar)));

        if (_actual == null)
        {
            _pila.Children.Add(Ui.Parrafo(
                "No hay ambientes todavía. Creá uno y usá sus variables con {{nombre}} en la URL, " +
                "las cabeceras, la auth o el cuerpo.", Ui.Tenue));
            return;
        }

        _pila.Children.Add(Ui.Rotulo($"Variables de «{_actual.Name}» · el ambiente activo es el resaltado"));

        var filas = new StackPanel { Spacing = 6 };
        foreach (var variable in _actual.Variables.ToList()) filas.Children.Add(Fila(variable));
        _pila.Children.Add(filas);

        var agregar = Ui.Accion("+ Variable", () =>
        {
            _actual.Variables.Add(new KeyValueItem());
            Guardar();
            Redibujar();
        });
        agregar.HorizontalAlignment = HorizontalAlignment.Left;
        _pila.Children.Add(agregar);
    }

    Control Fila(KeyValueItem variable)
    {
        var ambiente = _actual!;
        var secreta = ambiente.IsSecret(variable.Key);

        var clave = Ui.Campo(variable.Key, "nombre");
        clave.TextChanged += (_, _) =>
        {
            // renombrar la clave tiene que arrastrar la marca de secreto, o el valor quedaría a
            // la vista sin que nadie lo haya pedido
            var anterior = variable.Key;
            var nueva = clave.Text ?? "";
            if (ambiente.IsSecret(anterior))
            {
                ambiente.SetSecret(anterior, false);
                ambiente.SetSecret(nueva, true);
            }
            variable.Key = nueva;
            Guardar();
        };

        var valor = Ui.Campo(variable.Value, "valor");
        if (secreta) valor.PasswordChar = '•';
        valor.TextChanged += (_, _) => { variable.Value = valor.Text ?? ""; Guardar(); };

        var marcar = Ui.Opcion(secreta ? "🔒" : "🔓", secreta, () =>
        {
            ambiente.SetSecret(variable.Key, !secreta);
            Guardar();
            Redibujar();
        });
        marcar.MinWidth = Ui.Toque;

        var ver = Ui.Accion("👁", () => valor.RevealPassword = !valor.RevealPassword);
        ver.MinWidth = Ui.Toque;
        ver.IsVisible = secreta;

        var borrar = Ui.Accion("✕", () =>
        {
            ambiente.SetSecret(variable.Key, false);
            ambiente.Variables.Remove(variable);
            Guardar();
            Redibujar();
        });
        borrar.MinWidth = Ui.Toque;

        var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto,Auto") };
        var controles = new Control[] { clave, valor, marcar, ver, borrar };
        for (var i = 0; i < controles.Length; i++)
        {
            Grid.SetColumn(controles[i], i);
            grilla.Children.Add(controles[i]);
        }
        valor.Margin = new Thickness(6, 0);
        return grilla;
    }

    void NuevoAmbiente() => Dialogo.Texto("Nuevo ambiente", "", "nombre", nombre =>
    {
        var ambiente = new EnvironmentModel { Name = nombre };
        _ambientes.Add(ambiente);
        _actual = ambiente;
        Storage.SetActiveEnvironmentId(ambiente.Id);
        Guardar();
        Redibujar();
    });

    void Renombrar()
    {
        if (_actual == null) return;
        var ambiente = _actual;
        Dialogo.Texto("Renombrar ambiente", ambiente.Name, "nombre", nombre =>
        {
            ambiente.Name = nombre;
            Guardar();
            Redibujar();
        });
    }

    void Eliminar()
    {
        if (_actual == null) return;
        var ambiente = _actual;
        Dialogo.Confirmar("Eliminar ambiente", $"«{ambiente.Name}» y sus variables. No se puede deshacer.",
            "Eliminar", () =>
            {
                _ambientes.Remove(ambiente);
                _actual = _ambientes.FirstOrDefault();
                Storage.SetActiveEnvironmentId(_actual?.Id);
                Guardar();
                Redibujar();
            });
    }
}
