using Avalonia.Media;

namespace EasyRest.Android;

/// <summary>Los íconos, como geometrías.
///
/// Son los mismos trazos que usa el escritorio (están en Theme.axaml) más los que hicieron falta
/// para el móvil. Se dibujan con vectores y no con emoji ni con caracteres de dingbat: un glifo
/// depende de que la fuente del sistema lo tenga —y en Android muchas veces no lo tiene, así que
/// aparece el cuadradito— y además no se puede alinear ni recolorear con precisión.</summary>
internal static class Iconos
{
    // Del escritorio, tal cual
    public static readonly Geometry Rayo = Geometry.Parse("M13,2 L4,14 L11,14 L9,22 L20,9 L12,9 Z");
    public static readonly Geometry Carpeta = Geometry.Parse("M2.5,5.5 L9,5.5 L11,7.6 L21.5,7.6 L21.5,18.5 L2.5,18.5 Z");
    public static readonly Geometry Enviar = Geometry.Parse("M7,5 L19,12 L7,19 Z");
    public static readonly Geometry Guardar = Geometry.Parse("M4,4 L15,4 L20,9 L20,20 L4,20 Z M8,4 L8,9 L16,9 L16,4 M7,20 L7,13 L17,13 L17,20");
    public static readonly Geometry Copiar = Geometry.Parse("M9,9 L20,9 L20,20 L9,20 Z M15,9 L15,4 L4,4 L4,15 L9,15");
    public static readonly Geometry Basura = Geometry.Parse("M5,7 L19,7 M10,7 L10,4.5 L14,4.5 L14,7 M6.5,7 L7.6,20 L16.4,20 L17.5,7 M10,10.5 L10,16.5 M14,10.5 L14,16.5");

    // Propios del móvil
    public static readonly Geometry Buscar = Geometry.Parse("M11,4.5 a6.5,6.5 0 1,0 0.1,0 M16,16 L21,21");
    public static readonly Geometry Atras = Geometry.Parse("M15,5 L8,12 L15,19");
    public static readonly Geometry Chevron = Geometry.Parse("M9,6 L15,12 L9,18");
    public static readonly Geometry ChevronAbajo = Geometry.Parse("M6,9 L12,15 L18,9");
    public static readonly Geometry Mas = Geometry.Parse("M12,5 L12,19 M5,12 L19,12");
    public static readonly Geometry Puntos = Geometry.Parse("M5,12 h0.01 M12,12 h0.01 M19,12 h0.01");
    public static readonly Geometry Lineas = Geometry.Parse("M4,6 L20,6 M4,12 L20,12 M4,18 L20,18");
    public static readonly Geometry Lista = Geometry.Parse("M4,6 L20,6 M4,12 L20,12 M4,18 L14,18");
    public static readonly Geometry Globo = Geometry.Parse("M12,3 a9,9 0 1,0 0.1,0 M3,12 L21,12 M12,3 c-3,4 -3,14 0,18 M12,3 c3,4 3,14 0,18");
    public static readonly Geometry Candado = Geometry.Parse("M6,11 L18,11 L18,20 L6,20 Z M9,11 L9,7.5 a3,3 0 0,1 6,0 L15,11");
    public static readonly Geometry Ojo = Geometry.Parse("M2,12 C5,7 8.5,5 12,5 C15.5,5 19,7 22,12 C19,17 15.5,19 12,19 C8.5,19 5,17 2,12 Z M12,9 a3,3 0 1,0 0.1,0");
    public static readonly Geometry Sincronizar = Geometry.Parse("M4,12 a8,8 0 0,1 13.5,-5.8 M20,12 a8,8 0 0,1 -13.5,5.8 M17,3 L17.5,6.5 L14,6.8 M7,21 L6.5,17.5 L10,17.2");
    public static readonly Geometry Nube = Geometry.Parse("M7,18 a4,4 0 0,1 0.4,-8 a5.5,5.5 0 0,1 10.4,1.6 a3.2,3.2 0 0,1 -0.8,6.4 Z");
    public static readonly Geometry Bajar = Geometry.Parse("M12,4 L12,15 M8,11 L12,15 L16,11 M5,19 L19,19");
    public static readonly Geometry Compartir = Geometry.Parse("M12,4 L12,15 M8,8 L12,4 L16,8 M5,15 L5,20 L19,20 L19,15");
    public static readonly Geometry Tilde = Geometry.Parse("M4,12.5 L9.5,18 L20,6.5");
    public static readonly Geometry Cuadrado = Geometry.Parse("M6,6 L18,6 L18,18 L6,18 Z");
    public static readonly Geometry Aviso = Geometry.Parse("M12,3 L21,20 L3,20 Z M12,9 L12,14 M12,17 L12,17.01");
    public static readonly Geometry Llave = Geometry.Parse("M14,7 a4,4 0 1,1 -4,4 L4,17 L4,20 L7,20 L7,18 L9,18 L9,16 L11,16 L11,14");
}
