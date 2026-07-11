namespace FantaSim.App.Common.Storage;

/// <summary>
/// Generic byte-payload wrapper stored via the resident <c>IDocumentStore</c>
/// (<see cref="ResidentDocumentStore"/>). Every consumer (the crust-product cache today; a future
/// filmstrip cache, slice 2) upserts/reads exactly this one Type through
/// <c>IDocumentStore.UpsertAsync&lt;T&gt;</c>/<c>GetAsync&lt;T&gt;</c> — LiteDB's BSON mapper (and any
/// future document-store backend) then only ever builds a Type-keyed mapper cache entry for THIS
/// resident type, never for a bundle-owned payload record. That is the mechanism that keeps the
/// crust/filmstrip persistence payload types safely collectible: they live in a resident-or-contract
/// assembly (see project/contracts/App.World/Persistence/CrustProductCacheRecord.cs) and get
/// MessagePack-encoded to bytes BEFORE crossing into this wrapper, so the document store itself never
/// resolves a formatter/mapper keyed by a bundle type (the seven-pin-class rule 3/4,
/// vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md section 3.3).
///
/// Mirrors App.Activity's local <c>DocumentPayload</c> (Services/Service.cs) shape exactly, but is
/// defined resident (App.Common) rather than inside a plugin assembly.
/// </summary>
public sealed class DocumentBlob
{
    public byte[] Data { get; set; } = System.Array.Empty<byte>();

    public DocumentBlob()
    {
    }

    public DocumentBlob(byte[] data) => Data = data;
}
