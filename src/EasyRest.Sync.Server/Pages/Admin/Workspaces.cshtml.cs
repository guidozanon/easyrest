using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Pages.Admin;

public class WorkspacesModel(SyncDbContext db, AdminService admin) : AdminPageModel
{
    public record Row(Guid Id, string Name, string OwnerEmail, int Members, int Documents, DateTime CreatedAt);
    public record Member(Guid UserId, string Email, WorkspaceRole Role, bool CanReadSecrets);

    public List<Row> Workspaces { get; private set; } = new();
    public Workspace? Selected { get; private set; }
    public List<Member> Members { get; private set; } = new();

    public async Task OnGetAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } workspaceId)
        {
            Selected = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
            if (Selected != null)
            {
                // se materializa antes de proyectar al record: el constructor posicional no lo
                // sabe traducir el provider
                var rows = await db.Memberships
                    .Where(m => m.WorkspaceId == workspaceId)
                    .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
                    .ToListAsync(ct);

                Members = rows
                    .Select(x => new Member(x.u.Id, x.u.Email, x.m.Role, x.m.CanReadSecrets))
                    .OrderBy(m => m.Email)
                    .ToList();
                return;
            }
        }

        await LoadListAsync(ct);
    }

    public async Task<IActionResult> OnPostTransferAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var result = await admin.TransferOwnershipAsync(workspaceId, userId, ct);

        return result.Outcome == AdminOutcome.Ok
            ? RedirectToPage(new { id = workspaceId, aviso = "Se transfirió el workspace." })
            : RedirectToPage(new { id = workspaceId, errorMsg = result.Error });
    }

    async Task LoadListAsync(CancellationToken ct)
    {
        var members = await db.Memberships
            .GroupBy(m => m.WorkspaceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var documents = await db.Documents
            .Where(d => !d.Deleted)
            .GroupBy(d => d.WorkspaceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var owners = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        Workspaces = (await db.Workspaces.OrderBy(w => w.Name).ToListAsync(ct))
            .Select(w => new Row(w.Id, w.Name, owners.GetValueOrDefault(w.OwnerUserId, "(sin dueño)"),
                members.GetValueOrDefault(w.Id), documents.GetValueOrDefault(w.Id), w.CreatedAt))
            .ToList();
    }
}
