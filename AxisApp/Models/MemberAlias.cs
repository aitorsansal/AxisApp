using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace AxisApp.Models;

/// <summary>A private, per-account nickname override for how a member is displayed — see
/// Services/MemberDisplay.cs. Composite primary key (owner_id, member_id); only one property is
/// marked [PrimaryKey] below, same as GroupMember/ExpenseShare, so SupabaseAliasesRepository never
/// relies on an implicit single-column PK match (delete-then-insert instead of .Update()).</summary>
[Table("member_aliases")]
public class MemberAlias : BaseModel
{
    /// <summary>shouldInsert must be true — owner_id is a required FK with no default, unlike an
    /// auto-generated PK. false silently drops it from every insert payload, which fails NOT NULL
    /// / RLS with the generic "new row violates row-level security policy" (42501) — exact same
    /// footgun already documented on GroupMember.GroupId, hit live here too.</summary>
    [PrimaryKey("owner_id", shouldInsert: true)]
    public Guid OwnerId { get; set; }

    [Column("member_id")]
    public Guid MemberId { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = "";
}
