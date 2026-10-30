using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Didin.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stomme.Repository;
using Stomme.Shared;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// For the Didin site's administrators: whether the site holds site-wide Module or Page permission rows
    /// (<see cref="SiteWideRows"/>). It reads this one site's permissions and answers their count and, for each, its id,
    /// entity, permission and role; nothing else.
    /// </summary>
    [ApiController]
    [Route("api/admin/permissions-check")]
    [Authorize(Roles = RoleNames.Admin)]
    public sealed class PermissionsCheckController(IPermissionRepository permissions, IRoleRepository roles) : DidinController
    {
        public sealed record Row(int PermissionId, string Entity, string Permission, string Role, int? UserId, bool IsAuthorized);

        public sealed record Result(int SiteWideRows, List<Row> Rows);

        // GET {site}/api/admin/permissions-check
        [HttpGet]
        public async Task<Result> Get()
        {
            var found = await SiteWideRows.FindAsync(permissions, Site.SiteId);
            var roleNames = (await roles.GetRolesAsync(Site.SiteId, true)).ToDictionary(role => role.RoleId, role => role.Name);
            var rows = found.Select(row => new Row(row.PermissionId, row.EntityName, row.PermissionName,
                row.RoleId is int id && roleNames.TryGetValue(id, out var name) ? name : null, row.UserId, row.IsAuthorized)).ToList();
            return new Result(rows.Count, rows);
        }
    }
}
