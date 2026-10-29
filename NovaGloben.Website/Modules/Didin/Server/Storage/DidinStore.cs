#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Stomme.SharedKernel.Domain;
using Stomme.Tenancy.Abstractions;

namespace Didin.Server.Storage
{
    /// <summary>
    /// Didin's documents on one site, read and written as their own types: what the module's code asks of storage. Every
    /// call names the site; every read filters by site, kind and numbers, never by text. A write needs the request's (or
    /// the job's) tenant and is refused without one, as the module store refuses to read without one.
    /// </summary>
    public sealed class DidinStore(IRepository<DidinDocument, Guid> documents, ITenantContext tenants, TimeProvider clock)
    {
        /// <summary>
        /// How documents are written and read: the catalogue's own property names, read back case-insensitively. A list
        /// stored as null (the old catalogue's documents come as they were) reads as the empty list its type starts with,
        /// so no reader has to guard every list.
        /// </summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NullListStaysEmpty } },
        };

        private static void NullListStaysEmpty(JsonTypeInfo type)
        {
            if (type.Kind != JsonTypeInfoKind.Object) return;
            foreach (var property in type.Properties)
            {
                if (property.Set is { } set && property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    property.Set = (target, value) => { if (value is not null) set(target, value); };
                }
            }
        }

        /// <summary>The document of a kind with these numbers, or null.</summary>
        public async Task<T?> LoadAsync<T>(int siteId, DidinKind kind, int number, int owner = 0, CancellationToken ct = default)
            where T : class
        {
            var row = await FindAsync(siteId, kind, number, owner, ct);
            return row is null ? null : Read<T>(row);
        }

        /// <summary>Every document of a kind on the site, one per identity.</summary>
        public async Task<List<T>> AllAsync<T>(int siteId, DidinKind kind, CancellationToken ct = default)
            where T : class
        {
            var rows = await documents.FindAsync(d => d.SiteId == siteId && d.Kind == kind, ct);
            return Newest(rows).Select(Read<T>).ToList();
        }

        /// <summary>Every document of a kind with this number (all of a title's ratings, all of a file's subtitle tracks).</summary>
        public async Task<List<T>> AllWithNumberAsync<T>(int siteId, DidinKind kind, int number, CancellationToken ct = default)
            where T : class
        {
            var rows = await documents.FindAsync(d => d.SiteId == siteId && d.Kind == kind && d.Number == number, ct);
            return Newest(rows).Select(Read<T>).ToList();
        }

        /// <summary>
        /// One row per identity (number and owner), the newest. A write finds its row before it adds one, so two imports
        /// run at once can each add the same document; a list then reads it once, as <see cref="FindAsync"/> does.
        /// </summary>
        private static IEnumerable<DidinDocument> Newest(IEnumerable<DidinDocument> rows) =>
            rows.GroupBy(d => (d.Number, d.Owner)).Select(g => g.OrderByDescending(d => d.UpdatedUtc).First());

        /// <summary>Writes the document, replacing the one with the same kind and numbers.</summary>
        public async Task StoreAsync<T>(int siteId, DidinKind kind, int number, int owner, T document, CancellationToken ct = default)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(document);
            var json = JsonSerializer.Serialize(document, Json);
            var now = clock.GetUtcNow().UtcDateTime;
            var row = await FindAsync(siteId, kind, number, owner, ct);
            if (row is null)
            {
                var tenant = tenants.CurrentTenantId
                    ?? throw new InvalidOperationException("A Didin document is written for a tenant, and none is resolved.");
                await documents.AddAsync(DidinDocument.Create(Guid.NewGuid(), tenant, siteId, kind, number, owner, json, now), ct);
            }
            else if (row.Json != json)
            {
                row.Replace(json, now);
                await documents.UpdateAsync(row, ct);
            }
        }

        /// <summary>Removes the document, if there is one.</summary>
        public async Task DeleteAsync(int siteId, DidinKind kind, int number, int owner = 0, CancellationToken ct = default)
        {
            var row = await FindAsync(siteId, kind, number, owner, ct);
            if (row is not null)
            {
                await documents.DeleteAsync(row, ct);
            }
        }

        private async Task<DidinDocument?> FindAsync(int siteId, DidinKind kind, int number, int owner, CancellationToken ct)
        {
            var rows = await documents.FindAsync(
                d => d.SiteId == siteId && d.Kind == kind && d.Number == number && d.Owner == owner, ct);
            // One row per identity; should a race ever write two, the newest is the document.
            return rows.OrderByDescending(d => d.UpdatedUtc).FirstOrDefault();
        }

        private static T Read<T>(DidinDocument row) where T : class =>
            JsonSerializer.Deserialize<T>(row.Json, Json)
                ?? throw new InvalidOperationException($"Didin document {row.Kind} {row.Number} is empty.");
    }
}
