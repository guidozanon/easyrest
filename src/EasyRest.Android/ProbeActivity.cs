namespace EasyRest.Android;

// TEMPORAL — sonda de diagnóstico, se borra apenas responda.
//
// El APK sale sin ninguna actividad y hay dos explicaciones posibles: o el paso que genera los
// stubs de Java no está mirando este assembly, o sí lo mira pero se saltea MainActivity porque
// hereda de AvaloniaMainActivity<App>, que es genérico. Esta actividad es lo más simple que
// existe —hereda directo de Android.App.Activity— así que separa los dos casos: si aparece en
// el manifiesto y MainActivity no, el problema es la herencia genérica; si no aparece ninguna,
// el assembly no se está escaneando.
[Activity(Name = "com.rentlysoft.easyrest.ProbeActivity", Exported = false)]
public class ProbeActivity : global::Android.App.Activity
{
}
