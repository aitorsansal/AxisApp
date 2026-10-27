// PostgREST caps a response at 1000 rows by default, so whole-history reads are fetched in pages.
const PAGE_SIZE = 1000

export async function fetchAll<T>(
  page: (from: number, to: number) => PromiseLike<{ data: unknown; error: { message: string } | null }>,
): Promise<T[]> {
  const rows: T[] = []
  for (let from = 0; ; from += PAGE_SIZE) {
    const { data, error } = await page(from, from + PAGE_SIZE - 1)
    if (error) throw new Error(error.message)
    const batch = (data as T[]) ?? []
    rows.push(...batch)
    if (batch.length < PAGE_SIZE) return rows
  }
}
