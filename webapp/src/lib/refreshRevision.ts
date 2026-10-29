export class RefreshRevision {
  private revision = 0
  private request = 0

  invalidate(): number { return ++this.revision }

  begin(expectedRevision = this.revision): () => boolean {
    if (expectedRevision !== this.revision) return () => false
    const request = ++this.request
    return () => request === this.request && expectedRevision === this.revision
  }
}
