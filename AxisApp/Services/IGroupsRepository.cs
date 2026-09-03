using AxisApp.Models;

namespace AxisApp.Services;

public interface IGroupsRepository
{
    /// <summary>Groups the current account is a member of.</summary>
    Task<List<Group>> GetMyGroupsAsync();
    Task<Group> GetByIdAsync(Guid groupId);
    Task<Group> CreateAsync(string name);

    /// <summary>Renames a group. Owner-only — enforced by the existing "update own groups" RLS
    /// policy (created_by = auth.uid()), the same policy transfer_group_ownership() routes around
    /// via a security-definer RPC because it needs to change created_by itself; a plain rename
    /// doesn't touch created_by, so it needs no RPC. Takes the full loaded Group (Name mutated by
    /// the caller) rather than just (id, name), same "never build a fresh model for an update"
    /// discipline as SupabaseMembersRepository.UpdateAsync — a fresh Group would silently zero
    /// CreatedBy/CreatedAt.</summary>
    Task<Group> RenameAsync(Group group);

    /// <summary>Self-service leave via the leave_group() RPC. Rejects the group's creator (they
    /// must transfer ownership or dissolve instead) and rejects a nonzero balance in that group —
    /// see schema.sql's leave_group() remarks.</summary>
    Task LeaveAsync(Guid groupId);

    /// <summary>Hands the group's created_by to another current, claimed (real-account) member,
    /// via the transfer_group_ownership() RPC. Only the current creator may call this.</summary>
    Task TransferOwnershipAsync(Guid groupId, Guid newOwnerMemberId);

    /// <summary>Dissolves the group outright. Creator-only per RLS ("delete own groups"); the FK
    /// cascade shape does the rest — membership/pending invites are removed, payments/expenses/
    /// recurring_expenses survive as unscoped (group_id set null) history. See schema.sql's
    /// "Leave / transfer ownership / dissolve" block.</summary>
    Task DeleteAsync(Guid groupId);

    /// <summary>Removes a phantom member from the group via the remove_group_member() RPC.
    /// Callable by any current group member (mirrors add-a-phantom's permissiveness); rejects a
    /// claimed member (they must leave on their own) and a nonzero balance. See schema.sql's
    /// remove_group_member() remarks.</summary>
    Task RemoveMemberAsync(Guid groupId, Guid memberId);
}
