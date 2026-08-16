using System.Net.Http;

namespace EasyRest.Services.Sync;

/// <summary>Decide con qué sincroniza un workspace. Existe para que la UI no tenga que preguntar
/// "¿es git o es el server?" en cada lugar donde sincroniza o muestra estado.
///
/// La atadura a un servidor de sync gana sobre git: si alguien la configuró, es lo que quiere usar.
/// Un workspace puede ser un repo git igual, y seguir andando con `git` desde la terminal.</summary>
public static class WorkspaceSyncResolver
{
    /// <summary>Qué sincroniza el workspace de esa carpeta, o null si no sincroniza nada.
    ///
    /// Devuelve null también cuando hay atadura pero no hay sesión: eso no es "no configurado"
    /// sino "hay que volver a loguearse", y para distinguirlo está <see cref="NeedsLogin"/>.</summary>
    public static IWorkspaceSync? For(string workspaceRoot, string bindingPath, string statePath,
        SyncAccountStore? store = null, HttpClient? http = null)
    {
        var binding = SyncBinding.Load(bindingPath);
        if (binding.IsSet)
        {
            var conexión = SyncConnection.Restore(binding.ServerUrl, store, http);
            if (conexión != null)
                return new RemoteWorkspaceSync(workspaceRoot, conexión.Api, binding.WorkspaceId, statePath);
            return null;
        }

        var git = new GitWorkspaceSync(workspaceRoot);
        return git.IsConfigured ? git : null;
    }

    /// <summary>Hay un servidor configurado pero no hay sesión guardada: la UI tiene que ofrecer
    /// iniciar sesión en vez de decir que no hay sincronización.</summary>
    public static bool NeedsLogin(string bindingPath, SyncAccountStore? store = null)
    {
        var binding = SyncBinding.Load(bindingPath);
        return binding.IsSet && (store ?? SyncAccountStore.Default).Find(binding.ServerUrl) == null;
    }
}
