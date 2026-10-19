import { useEffect, useState } from 'react'
import {
  fetchVideos,
  type VideoItem,
  type VideoSortField,
  type VideoSortOrder,
} from './videosApi'

function VideoLibrary() {
  const [items, setItems] = useState<VideoItem[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [sortBy, setSortBy] = useState<VideoSortField>('date')
  const [sortOrder, setSortOrder] = useState<VideoSortOrder>('desc')
  const [totalCount, setTotalCount] = useState(0)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [errorMessage, setErrorMessage] = useState('')
  const [requestVersion, setRequestVersion] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')

    fetchVideos({ page, pageSize, sortBy, sortOrder, signal: controller.signal })
      .then((response) => {
        setItems(response.items)
        setTotalCount(response.totalCount)
        setStatus('ready')
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setErrorMessage(error instanceof Error ? error.message : 'Unable to load videos.')
        setStatus('error')
      })

    return () => controller.abort()
  }, [page, pageSize, sortBy, sortOrder, requestVersion])

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))

  function changeSort(field: VideoSortField) {
    setPage(1)
    if (sortBy === field) {
      setSortOrder((current) => (current === 'asc' ? 'desc' : 'asc'))
      return
    }

    setSortBy(field)
    setSortOrder(field === 'name' ? 'asc' : 'desc')
  }

  return (
    <>
      <h1>Video library</h1>
      <p>{totalCount} {totalCount === 1 ? 'video' : 'videos'}</p>

      {status === 'error' ? (
        <div role="alert">
          <p>{errorMessage}</p>
          <button type="button" onClick={() => setRequestVersion((value) => value + 1)}>
            Retry
          </button>
        </div>
      ) : status === 'ready' && items.length === 0 ? (
        <p>No videos found.</p>
      ) : (
        <table aria-busy={status === 'loading'}>
          <thead>
            <tr>
              <th
                scope="col"
                aria-sort={sortBy === 'name' ? (sortOrder === 'asc' ? 'ascending' : 'descending') : 'none'}
              >
                <button type="button" onClick={() => changeSort('name')}>
                  Name {sortBy === 'name' ? (sortOrder === 'asc' ? '↑' : '↓') : '↕'}
                </button>
              </th>
              <th
                scope="col"
                aria-sort={sortBy === 'date' ? (sortOrder === 'asc' ? 'ascending' : 'descending') : 'none'}
              >
                <button type="button" onClick={() => changeSort('date')}>
                  Date {sortBy === 'date' ? (sortOrder === 'asc' ? '↑' : '↓') : '↕'}
                </button>
              </th>
              <th scope="col">Tags</th>
            </tr>
          </thead>
          <tbody>
            {status === 'loading' && items.length === 0 ? (
              <tr><td colSpan={3}>Loading videos…</td></tr>
            ) : items.map((video) => <VideoRow key={video.videoId} video={video} />)}
          </tbody>
        </table>
      )}

      <div>
        <label>
          Rows per page{' '}
          <select
            value={pageSize}
            onChange={(event) => {
              setPageSize(Number(event.target.value))
              setPage(1)
            }}
          >
            {[20, 50, 100].map((size) => <option key={size} value={size}>{size}</option>)}
          </select>
        </label>
        <span> Page {page} of {pageCount} </span>
        <button
          type="button"
          aria-label="Previous page"
          disabled={page <= 1 || status === 'loading'}
          onClick={() => setPage((current) => Math.max(1, current - 1))}
        >
          Previous
        </button>{' '}
        <button
          type="button"
          aria-label="Next page"
          disabled={page >= pageCount || status === 'loading'}
          onClick={() => setPage((current) => Math.min(pageCount, current + 1))}
        >
          Next
        </button>
      </div>
    </>
  )
}

function VideoRow({ video }: { video: VideoItem }) {
  const formattedDate = new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(video.videoDate))

  return (
    <tr>
      <td>{video.videoName}</td>
      <td><time dateTime={video.videoDate}>{formattedDate}</time></td>
      <td>{video.tags.length > 0 ? video.tags.join(', ') : '—'}</td>
    </tr>
  )
}

export default VideoLibrary
