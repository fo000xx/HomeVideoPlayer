import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import {
  createTag,
  deleteTag,
  fetchTags,
  fetchVideos,
  replaceVideoTags,
  type TagOption,
  type VideoItem,
  type VideoSortField,
  type VideoSortOrder,
} from './videosApi'

function VideoLibrary() {
  const [activeTab, setActiveTab] = useState<'videos' | 'tags'>('videos')
  const [items, setItems] = useState<VideoItem[]>([])
  const [selectedVideo, setSelectedVideo] = useState<VideoItem | null>(null)
  const [playbackError, setPlaybackError] = useState('')
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [sortBy, setSortBy] = useState<VideoSortField>('date')
  const [sortOrder, setSortOrder] = useState<VideoSortOrder>('desc')
  const [totalCount, setTotalCount] = useState(0)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [errorMessage, setErrorMessage] = useState('')
  const [requestVersion, setRequestVersion] = useState(0)
  const [tags, setTags] = useState<TagOption[]>([])
  const [tagStatus, setTagStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [tagError, setTagError] = useState('')
  const [tagRequestVersion, setTagRequestVersion] = useState(0)
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([])
  const [newTagName, setNewTagName] = useState('')
  const [creatingTag, setCreatingTag] = useState(false)
  const [createTagError, setCreateTagError] = useState('')
  const [removingTagId, setRemovingTagId] = useState<number | null>(null)
  const [removeTagError, setRemoveTagError] = useState('')

  useEffect(() => {
    const controller = new AbortController()

    fetchTags(controller.signal)
      .then((response) => {
        setTags(response)
        setTagStatus('ready')
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setTagError(error instanceof Error ? error.message : 'Unable to load tags.')
        setTagStatus('error')
      })

    return () => controller.abort()
  }, [tagRequestVersion])

  useEffect(() => {
    const controller = new AbortController()

    fetchVideos({
      page,
      pageSize,
      sortBy,
      sortOrder,
      tagIds: selectedTagIds,
      signal: controller.signal,
    })
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
  }, [page, pageSize, sortBy, sortOrder, selectedTagIds, requestVersion])

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))

  useEffect(() => {
    if (activeTab !== 'videos') videoRef.current?.pause()
  }, [activeTab])

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const currentIndex = activeTab === 'videos' ? 0 : 1
    let nextIndex: number

    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowLeft':
        nextIndex = (currentIndex + 1) % 2
        break
      case 'Home':
        nextIndex = 0
        break
      case 'End':
        nextIndex = 1
        break
      default:
        return
    }

    event.preventDefault()
    setActiveTab(nextIndex === 0 ? 'videos' : 'tags')
    tabRefs.current[nextIndex]?.focus()
  }

  function changeSort(field: VideoSortField) {
    setStatus('loading')
    setPage(1)
    if (sortBy === field) {
      setSortOrder((current) => (current === 'asc' ? 'desc' : 'asc'))
      return
    }

    setSortBy(field)
    setSortOrder(field === 'name' ? 'asc' : 'desc')
  }

  async function handleCreateTag(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setCreatingTag(true)
    setCreateTagError('')

    try {
      const createdTag = await createTag(newTagName)
      setTags((current) => [...current, createdTag].sort((a, b) => a.name.localeCompare(b.name)))
      setNewTagName('')
    } catch (error: unknown) {
      setCreateTagError(error instanceof Error ? error.message : 'Unable to create tag.')
    } finally {
      setCreatingTag(false)
    }
  }

  async function handleRemoveTag(tag: TagOption) {
    if (!window.confirm(`Remove "${tag.name}" from the tag catalog and all videos?`)) return

    setRemovingTagId(tag.tagId)
    setRemoveTagError('')
    try {
      await deleteTag(tag.tagId)
      setTags((current) => current.filter((currentTag) => currentTag.tagId !== tag.tagId))
      setSelectedTagIds((current) => current.filter((tagId) => tagId !== tag.tagId))
      setStatus('loading')
      setRequestVersion((value) => value + 1)
    } catch (error: unknown) {
      setRemoveTagError(error instanceof Error ? error.message : 'Unable to remove tag.')
    } finally {
      setRemovingTagId(null)
    }
  }

  async function handleSaveVideoTags(videoId: number, tagIds: number[]) {
    await replaceVideoTags(videoId, tagIds)
    setStatus('loading')
    setRequestVersion((value) => value + 1)
    setTagRequestVersion((value) => value + 1)
  }

  return (
    <>
      <div className="library-tabs" role="tablist" aria-label="Video library sections">
        <button
          ref={(element) => { tabRefs.current[0] = element }}
          id="videos-tab"
          type="button"
          role="tab"
          aria-selected={activeTab === 'videos'}
          aria-controls="videos-panel"
          tabIndex={activeTab === 'videos' ? 0 : -1}
          onClick={() => setActiveTab('videos')}
          onKeyDown={handleTabKeyDown}
        >
          Videos
        </button>
        <button
          ref={(element) => { tabRefs.current[1] = element }}
          id="tags-tab"
          type="button"
          role="tab"
          aria-selected={activeTab === 'tags'}
          aria-controls="tags-panel"
          tabIndex={activeTab === 'tags' ? 0 : -1}
          onClick={() => setActiveTab('tags')}
          onKeyDown={handleTabKeyDown}
        >
          Manage tags
        </button>
      </div>

      <section
        id="videos-panel"
        role="tabpanel"
        aria-labelledby="videos-tab"
        tabIndex={0}
        hidden={activeTab !== 'videos'}
      >
        <h1>Video library</h1>
        <p>{totalCount} {totalCount === 1 ? 'video' : 'videos'}</p>

        <fieldset className="tag-filter" disabled={tagStatus !== 'ready'}>
          <legend>Filter by tags (videos must have all selected tags)</legend>
          {tagStatus === 'loading' ? (
            <p>Loading tags…</p>
          ) : tagStatus === 'error' ? (
            <p>Tag filters are unavailable until the catalog loads.</p>
          ) : tags.length === 0 ? (
            <p>No tags available. Visit Manage tags to create one and start categorizing videos.</p>
          ) : (
            <div className="tag-options">
              {tags.map((tag) => (
                <label key={tag.tagId}>
                  <input
                    type="checkbox"
                    checked={selectedTagIds.includes(tag.tagId)}
                    onChange={(event) => {
                      const checked = event.currentTarget.checked
                      setStatus('loading')
                      setPage(1)
                      setSelectedTagIds((current) =>
                        checked
                          ? [...current, tag.tagId]
                          : current.filter((tagId) => tagId !== tag.tagId),
                      )
                    }}
                  />
                  {tag.name} ({tag.videoCount})
                </label>
              ))}
            </div>
          )}
          {selectedTagIds.length > 0 && (
            <button
              type="button"
              onClick={() => {
                setStatus('loading')
                setSelectedTagIds([])
                setPage(1)
              }}
            >
              Clear tag filters
            </button>
          )}
        </fieldset>

        {selectedVideo && (
          <section className="video-player-section" aria-labelledby="selected-video-heading">
            <div className="video-player-header">
              <h2 id="selected-video-heading">{selectedVideo.videoName}</h2>
              <button
                type="button"
                className="video-player-close"
                aria-label="Close video player"
                onClick={() => {
                  setSelectedVideo(null)
                  setPlaybackError('')
                }}
              >
                ×
              </button>
            </div>
            <video
              key={selectedVideo.videoId}
              ref={videoRef}
              className="video-player"
              src={`/api/videos/${selectedVideo.videoId}/stream`}
              controls
              preload="metadata"
              aria-label={`Video player for ${selectedVideo.videoName}`}
              onError={() => setPlaybackError('Unable to load or play this video.')}
            >
              Your browser does not support HTML video playback.
            </video>
            {playbackError && <p role="alert">{playbackError}</p>}
          </section>
        )}

        {status === 'error' ? (
          <div role="alert">
            <p>{errorMessage}</p>
            <button
              type="button"
              onClick={() => {
                setStatus('loading')
                setRequestVersion((value) => value + 1)
              }}
            >
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
              ) : items.map((video) => (
                <VideoRow
                  key={`${video.videoId}:${video.tags.join(',')}`}
                  video={video}
                  tags={tags}
                  selected={selectedVideo?.videoId === video.videoId}
                  onSelect={() => {
                    setSelectedVideo(video)
                    setPlaybackError('')
                  }}
                  onSaveTags={handleSaveVideoTags}
                />
              ))}
            </tbody>
          </table>
        )}

        <div>
          <label>
            Rows per page{' '}
            <select
              value={pageSize}
              onChange={(event) => {
                setStatus('loading')
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
            onClick={() => {
              setStatus('loading')
              setPage((current) => Math.max(1, current - 1))
            }}
          >
            Previous
          </button>{' '}
          <button
            type="button"
            aria-label="Next page"
            disabled={page >= pageCount || status === 'loading'}
            onClick={() => {
              setStatus('loading')
              setPage((current) => Math.min(pageCount, current + 1))
            }}
          >
            Next
          </button>
        </div>
      </section>

      <section
        id="tags-panel"
        role="tabpanel"
        aria-labelledby="tags-tab"
        tabIndex={0}
        hidden={activeTab !== 'tags'}
      >
        <h1>Manage tags</h1>
        <section className="tag-management" aria-labelledby="manage-tags-heading">
          <h2 id="manage-tags-heading">Add a tag</h2>
          <form className="tag-create-form" onSubmit={handleCreateTag}>
            <label htmlFor="new-tag-name">New tag</label>
            <input
              id="new-tag-name"
              value={newTagName}
              maxLength={100}
              onChange={(event) => setNewTagName(event.target.value)}
            />
            <button type="submit" disabled={creatingTag || !newTagName.trim()}>
              {creatingTag ? 'Adding…' : 'Add tag'}
            </button>
          </form>
          {createTagError && <p role="alert">{createTagError}</p>}
          <h2 id="existing-tags-heading">All tags</h2>
          {tagStatus === 'loading' ? (
            <p>Loading tags…</p>
          ) : tagStatus === 'ready' && tags.length === 0 ? (
            <p>No tags have been created yet.</p>
          ) : tagStatus === 'ready' ? (
            <ul className="managed-tag-list" aria-labelledby="existing-tags-heading">
              {tags.map((tag) => (
                <li key={tag.tagId}>
                  <span>{tag.name} ({tag.videoCount} {tag.videoCount === 1 ? 'video' : 'videos'})</span>
                  <button
                    type="button"
                    disabled={removingTagId !== null}
                    onClick={() => handleRemoveTag(tag)}
                  >
                    {removingTagId === tag.tagId ? 'Removing…' : 'Remove'}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
          {removeTagError && <p role="alert">{removeTagError}</p>}
          {tagStatus === 'error' && (
            <div role="alert">
              <p>{tagError}</p>
              <button
                type="button"
                onClick={() => {
                  setTagStatus('loading')
                  setTagRequestVersion((value) => value + 1)
                }}
              >
                Retry loading tags
              </button>
            </div>
          )}
        </section>
      </section>
    </>
  )
}

function VideoRow({
  video,
  tags,
  selected,
  onSelect,
  onSaveTags,
}: {
  video: VideoItem
  tags: TagOption[]
  selected: boolean
  onSelect: () => void
  onSaveTags: (videoId: number, tagIds: number[]) => Promise<void>
}) {
  const [selectedTagNames, setSelectedTagNames] = useState([...video.tags])
  const [tagSearch, setTagSearch] = useState('')
  const [activeSuggestionIndex, setActiveSuggestionIndex] = useState(0)
  const [savingTags, setSavingTags] = useState(false)
  const [tagSaveError, setTagSaveError] = useState('')

  const suggestions = tags.filter((tag) =>
    !selectedTagNames.includes(tag.name)
    && tag.name.toLocaleLowerCase().includes(tagSearch.trim().toLocaleLowerCase()),
  )
  const showSuggestions = tagSearch.trim().length > 0 && suggestions.length > 0
  const noMatchingTags = tagSearch.trim().length > 0 && suggestions.length === 0
  const inputId = `video-tag-input-${video.videoId}`
  const suggestionsId = `video-tag-suggestions-${video.videoId}`

  const formattedDate = new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(video.videoDate))

  async function updateTags(nextTagNames: string[]) {
    setSavingTags(true)
    setTagSaveError('')

    try {
      const selectedTagIds = tags
        .filter((tag) => nextTagNames.includes(tag.name))
        .map((tag) => tag.tagId)
      await onSaveTags(video.videoId, selectedTagIds)
      setSelectedTagNames(nextTagNames)
      setTagSearch('')
      setActiveSuggestionIndex(0)
    } catch (error: unknown) {
      setTagSaveError(error instanceof Error ? error.message : 'Unable to save video tags.')
    } finally {
      setSavingTags(false)
    }
  }

  function addTag(tagName: string) {
    if (selectedTagNames.includes(tagName) || savingTags) return
    void updateTags([...selectedTagNames, tagName])
  }

  return (
    <tr>
      <td>
        <button
          type="button"
          className="video-select-button"
          aria-label={`Play ${video.videoName}`}
          aria-pressed={selected}
          onClick={onSelect}
        >
          {video.videoName}
        </button>
      </td>
      <td><time dateTime={video.videoDate}>{formattedDate}</time></td>
      <td>
        <p>{video.tags.length > 0 ? video.tags.join(', ') : '—'}</p>
        <fieldset className="video-tag-assignment" disabled={savingTags}>
          <legend>Assign tags to {video.videoName}</legend>
          {tags.length === 0 ? (
            <p>No tags available.</p>
          ) : (
            <>
              <div className="video-tag-labels" aria-label={`Current tags for ${video.videoName}`}>
                {selectedTagNames.map((tagName) => (
                  <span className="video-tag-label" key={tagName}>
                    {tagName}
                    <button
                      type="button"
                      aria-label={`Remove ${tagName} tag from ${video.videoName}`}
                      onClick={() => {
                        setTagSaveError('')
                        void updateTags(selectedTagNames.filter((current) => current !== tagName))
                      }}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
              <label className="video-tag-input-label" htmlFor={inputId}>
                Add a tag
              </label>
              <div className="video-tag-input-wrapper">
                <input
                  id={inputId}
                  type="text"
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={showSuggestions}
                  aria-controls={showSuggestions ? suggestionsId : undefined}
                  aria-activedescendant={
                    showSuggestions
                      ? `${suggestionsId}-${activeSuggestionIndex}`
                      : undefined
                  }
                  autoComplete="off"
                  value={tagSearch}
                  onChange={(event) => {
                    setTagSearch(event.target.value)
                    setActiveSuggestionIndex(0)
                    setTagSaveError('')
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'ArrowDown' && showSuggestions) {
                      event.preventDefault()
                      setActiveSuggestionIndex((index) => (index + 1) % suggestions.length)
                    } else if (event.key === 'ArrowUp' && showSuggestions) {
                      event.preventDefault()
                      setActiveSuggestionIndex((index) =>
                        (index - 1 + suggestions.length) % suggestions.length,
                      )
                    } else if (event.key === 'Enter' && showSuggestions) {
                      event.preventDefault()
                      addTag(suggestions[activeSuggestionIndex]?.name ?? suggestions[0].name)
                    } else if (event.key === 'Escape') {
                      setTagSearch('')
                    }
                  }}
                />
                {showSuggestions && (
                  <ul id={suggestionsId} className="video-tag-suggestions" role="listbox">
                    {suggestions.map((tag, index) => (
                      <li
                        id={`${suggestionsId}-${index}`}
                        key={tag.tagId}
                        role="option"
                        aria-selected={index === activeSuggestionIndex}
                        onMouseDown={(event) => event.preventDefault()}
                        onClick={() => addTag(tag.name)}
                      >
                        {tag.name}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              {noMatchingTags && <p>No matching tags.</p>}
            </>
          )}
        </fieldset>
        {savingTags && <p>Saving tags…</p>}
        {tagSaveError && <p role="alert">{tagSaveError}</p>}
      </td>
    </tr>
  )
}

export default VideoLibrary
