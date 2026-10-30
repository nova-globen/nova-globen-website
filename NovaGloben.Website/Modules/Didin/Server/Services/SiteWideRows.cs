using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Didin.Server.Services
{
    /// <summary>
    /// Site-wide Module or Page permission rows (EntityId -1) on one site. An earlier version of the Didin site's
    /// reconciler gave Home and its module one permission list, which Stomme before 10.76 could have written as such
    /// rows; none should exist. The reconciler logs the result at every boot, and the site's administrators can read it
    /// (<see cref="Controllers.PermissionsCheckController"/>).
    /// </summary>
    public static class SiteWideRows
    {
        public static async Task<List<Permission>> FindAsync(IPermissionRepository permissions, int siteId)
        {
            var found = new List<Permission>();
            foreach (var entity in new[] { EntityNames.Module, EntityNames.Page })
            {
                found.AddRange((await permissions.GetPermissionsAsync(siteId, entity, -1)).Where(row => row.EntityId == -1));
            }
            return found;
        }
    }
}
