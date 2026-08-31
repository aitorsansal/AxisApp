using AxisApp.Models;

namespace AxisApp.Services;

public interface IGroupsRepository
{
    /// <summary>Groups the current account is a member of.</summary>
    Task<List<Group>> GetMyGroupsAsync();
    Task<Group> GetByIdAsync(Guid groupId);
    Task<Group> CreateAsync(string name);

    /// <summary>Self-service leave via the leave_group() RPC. Rejects the group's creator (they
    /// must transfer ownership or dissolve instead) and rejects a nonzero balance in that group —
    /// see schema.sql's leave_group() remarks.</summary>
    Task LeaveAsync(Guid groupId);

    /// <summary>Hands the group's created_by to another current, claimed (real-account) member,
    /// via the transfer_group_ownership() RPC. Only the current creator may call this.</summary>
    Task TransferOwnershipAsync(Guid groupId, Guid newOwnerMemberId);

    /// <summary>Dissolves the group outright. Creator-only per RLS ("delete own groups"); the FK
    /// cascade shape does the rest — membership/pending invites are removed, payments/expenses/
    /// recurring_payments survive as unscoped (group_id set null) history. See schema.sql's
    /// "Leave / transfer ownership / dissolve" block.</summary>
    Task DeleteAsync(Guid groupId);
}
