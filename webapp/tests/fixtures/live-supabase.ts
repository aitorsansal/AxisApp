type Change = () => void
type Status = (status: string) => void
const channels = new Set<{ change?: Change; status?: Status }>()

export function emitLiveChange() { for (const channel of channels) channel.change?.() }
export function emitLiveConnected() { for (const channel of channels) channel.status?.('SUBSCRIBED') }
export function liveChannelCount() { return channels.size }

export const supabase = {
  channel(_name: string) {
    const channel: { change?: Change; status?: Status } = {}
    channels.add(channel)
    const api = {
      on(_event: string, _filter: unknown, change: Change) { channel.change = change; return api },
      subscribe(status: Status) { channel.status = status; return api },
      channel,
    }
    return api
  },
  async removeChannel(api: { channel: object }) { channels.delete(api.channel) },
}
