export interface VideoItem {
  videoId: number
  videoName: string
  videoDate: string
  tags: string[]
}

export interface TagOption {
  tagId: number
  name: string
}

export interface VideoPage {
  items: VideoItem[]
  page: number
  pageSize: number
  totalCount: number
}

export type VideoSortField = 'name' | 'date'
export type VideoSortOrder = 'asc' | 'desc'

interface FetchVideosOptions {
  page: number
  pageSize: number
  sortBy: VideoSortField
  sortOrder: VideoSortOrder
  tagIds: number[]
  signal: AbortSignal
}

export async function fetchVideos({
  page,
  pageSize,
  sortBy,
  sortOrder,
  tagIds,
  signal,
}: FetchVideosOptions): Promise<VideoPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    sortBy,
    sortOrder,
  })
  if (tagIds.length > 0) query.set('tagIds', tagIds.join(','))
  const response = await fetch(`/api/videos?${query}`, { signal })

  await ensureSuccess(response)

  return response.json() as Promise<VideoPage>
}

export async function fetchTags(signal: AbortSignal): Promise<TagOption[]> {
  const response = await fetch('/api/tags', { signal })
  await ensureSuccess(response)
  return response.json() as Promise<TagOption[]>
}

export async function createTag(name: string): Promise<TagOption> {
  const response = await fetch('/api/tags', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  await ensureSuccess(response)
  return response.json() as Promise<TagOption>
}

export async function deleteTag(tagId: number): Promise<void> {
  const response = await fetch(`/api/tags/${tagId}`, { method: 'DELETE' })
  await ensureSuccess(response)
}

export async function replaceVideoTags(videoId: number, tagIds: number[]): Promise<void> {
  const response = await fetch(`/api/videos/${videoId}/tags`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tagIds }),
  })
  await ensureSuccess(response)
}

async function ensureSuccess(response: Response): Promise<void> {
  if (response.ok) return

  let message = `Request failed (${response.status}).`
  try {
    const error = await response.json() as { message?: string; description?: string }
    if (error.message || error.description) message = error.message || error.description || message
  } catch {
    // Preserve the status message when the error body is not JSON.
  }
  throw new Error(message)
}