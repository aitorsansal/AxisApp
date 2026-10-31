export interface ActivityLabelInput {
  isSettlement: boolean
  payerId: string
  shareMemberIds?: readonly string[] | null
  viewerId?: string | null
}

export interface ActivityLabel {
  key: string
  args: Array<string | number>
}

export function activityLabel(
  { isSettlement, payerId, shareMemberIds, viewerId }: ActivityLabelInput,
  name: (memberId: string) => string,
): ActivityLabel {
  const payerIsViewer = !!viewerId && payerId === viewerId
  const others = [...new Set(shareMemberIds ?? [])].filter((id) => id !== payerId)
  const plain: ActivityLabel = payerIsViewer
    ? { key: 'GroupDetail_YouPaid', args: [] }
    : { key: 'GroupDetail_PaidBy', args: [name(payerId)] }

  if (others.length === 0) return plain

  if (isSettlement) {
    const recipient = others[0]
    if (payerIsViewer) return { key: 'GroupDetail_YouPaidTo', args: [name(recipient)] }
    if (viewerId && recipient === viewerId) return { key: 'GroupDetail_PaidToYou', args: [name(payerId)] }
    return { key: 'GroupDetail_PaidTo', args: [name(payerId), name(recipient)] }
  }

  if (others.length === 1) {
    const other = others[0]
    if (payerIsViewer) return { key: 'GroupDetail_YouPaidFor', args: [name(other)] }
    if (viewerId && other === viewerId) return { key: 'GroupDetail_PaidForYou', args: [name(payerId)] }
    return { key: 'GroupDetail_PaidFor', args: [name(payerId), name(other)] }
  }

  if (payerIsViewer) return { key: 'GroupDetail_YouPaidForCount', args: [others.length] }
  return { key: 'GroupDetail_PaidForCount', args: [name(payerId), others.length] }
}
