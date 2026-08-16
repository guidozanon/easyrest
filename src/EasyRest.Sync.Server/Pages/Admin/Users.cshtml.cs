using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Pages.Admin;

public class UsersModel(SyncDbContext db, AdminService admin) : AdminPageModel
{
    public record Row(Guid Id, string Email, string DisplayName, string Provider,
        bool IsServerAdmin, bool Disabled, int Workspaces, DateTime LastSeenAt);

    public List<Row> Users { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public Task<IActionResult> OnPostToggleAdminAsync(Guid userId, bool value, CancellationToken ct) =>
        ApplyAsync(userId, isServerAdmin: value, disabled: null,
            value ? "Ahora es administrador del server." : "Ya no es administrador del server.", ct);

    public Task<IActionResult> OnPostToggleDisabledAsync(Guid userId, bool value, CancellationToken ct) =>
        ApplyAsync(userId, isServerAdmin: null, disabled: value,
            value ? "Usuario desactivado: sus sesiones se cortaron." : "Usuario reactivado.", ct);

    async Task<IActionResult> ApplyAsync(Guid userId, bool? isServerAdmin, bool? disabled,
        string aviso, CancellationToken ct)
    {
        var result = await admin.UpdateUserAsync(CurrentUser.Id, userId, isServerAdmin, disabled, ct);

        return result.Outcome == AdminOutcome.Ok
            ? RedirectToPage(new { aviso })
            : RedirectToPage(new { errorMsg = result.Error });
    }

    async Task LoadAsync(CancellationToken ct)
    {
        var counts = await db.Memberships
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        Users = (await db.Users.OrderBy(u => u.Email).ToListAsync(ct))
            .Select(u => new Row(u.Id, u.Email, u.DisplayName, u.Provider, u.IsServerAdmin,
                u.Disabled, counts.GetValueOrDefault(u.Id), u.LastSeenAt))
            .ToList();
    }
}
