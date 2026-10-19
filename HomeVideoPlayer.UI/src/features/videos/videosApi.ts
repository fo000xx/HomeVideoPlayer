export interface VideoItem {
  videoId: number
  videoName: string
  videoDate: string
  tags: string[]
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
  signal: AbortSignal
}

export async function fetchVideos({
  page,
  pageSize,
  sortBy,
  sortOrder,
  signal,
}: FetchVideosOptions): Promise<VideoPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    sortBy,
    sortOrder,
  })
  const response = await fetch(`/api/videos?${query}`, { signal })

  if (!response.ok) {
    let message = `Request failed (${response.status}).`
    try {
      const error = await response.json() as { message?: string }
      if (error.message) message = error.message
    } catch {
      // Preserve the status message when the error body is not JSON.
    }
    throw new Error(message)
  }

  return response.json() as Promise<VideoPage>
}