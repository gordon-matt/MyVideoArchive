/**
 * Shared helpers for playlist Details and DetailsCustom (Video.js playlist + per-video extras).
 */
import {
    STORAGE_KEY_RATE,
    STORAGE_KEY_SUBTITLE_LANG,
    savePosition,
    loadSavedPosition,
    clearPosition,
    clearRemoteTextTracks,
    bindSubtitlePreferenceStorage
} from './video-details-shared.js';

export {
    STORAGE_KEY_RATE,
    STORAGE_KEY_SUBTITLE_LANG,
    savePosition,
    loadSavedPosition,
    clearPosition
};

/** @type {() => import('video.js').VideoJsPlayer | null} */
let getVideoJsPlayer = () => null;

export function setPlaylistVideoJsPlayerGetter(fn) {
    getVideoJsPlayer = fn;
}

export const STORAGE_KEY_AUTO_ADVANCE = 'mva-auto-advance';

/**
 * Tear down Sortable so Knockout/DOM updates are not fighting direct child moves.
 * @param {import('sortablejs').default | null | undefined} instance
 * @returns {null}
 */
export function destroyPlaylistSortable(instance) {
    if (instance) {
        instance.destroy();
    }
    return null;
}

/**
 * Re-append list rows so DOM order matches the observable (after programmatic reorder).
 * @param {HTMLElement | null} container
 * @param {Array<unknown>} orderedItems
 * @param {(item: unknown) => number | string} getVideoId
 */
export function syncPlaylistListDomOrder(container, orderedItems, getVideoId) {
    if (!container || !orderedItems?.length) {
        return;
    }

    for (const item of orderedItems) {
        const id = getVideoId(item);
        const el = container.querySelector(
            `.playlist-video-item[data-video-id="${CSS.escape(String(id))}"]`
        );
        if (el) {
            container.appendChild(el);
        }
    }
}

/**
 * Read playlist row order from the DOM (after a Sortable drag).
 * @param {HTMLElement} container
 * @param {(videoId: number) => unknown | undefined} findItem
 */
export function readPlaylistOrderFromDom(container, findItem) {
    const newOrder = [];
    container.querySelectorAll('.playlist-video-item').forEach(el => {
        const videoId = parseInt(el.getAttribute('data-video-id') ?? '', 10);
        if (!Number.isFinite(videoId)) {
            return;
        }
        const entry = findItem(videoId);
        if (entry) {
            newOrder.push(entry);
        }
    });
    return newOrder;
}

/**
 * Apply a new playlist order to Knockout and sync the DOM (use after "move to top", etc.).
 * @param {{ playlistVideos: (v: unknown[]) => void, sortableInstance?: import('sortablejs').default | null }} vm
 * @param {string} containerId
 * @param {Array<unknown>} newOrder
 * @param {(item: unknown) => number | string} getVideoId
 */
export function applyProgrammaticPlaylistReorder(vm, containerId, newOrder, getVideoId) {
    vm.sortableInstance = destroyPlaylistSortable(vm.sortableInstance);
    vm.playlistVideos(newOrder);
    syncPlaylistListDomOrder(document.getElementById(containerId), newOrder, getVideoId);
}

/**
 * Rebuild the Video.js playlist and keep the same video playing when possible.
 * Uses the playlist setter's initial index (avoids loading item 0 then switching).
 *
 * @param {import('video.js').VideoJsPlayer | null | undefined} player
 * @param {object} options
 * @param {Array<unknown>} options.entries - playable items in display order
 * @param {(entry: unknown) => object} options.toPlaylistItem
 * @param {(entry: unknown) => number} options.getVideoId
 * @param {number | null | undefined} [options.prevVideoId]
 * @param {number} [options.prevTime]
 * @param {boolean} options.autoAdvance
 * @returns {{ entries: Array<unknown>, startIndex: number }}
 */
export function rebuildVideoJsPlaylist(player, {
    entries,
    toPlaylistItem,
    getVideoId,
    prevVideoId,
    prevTime = 0,
    autoAdvance
}) {
    if (!player?.playlist) {
        return { entries, startIndex: -1 };
    }

    const items = entries.map(toPlaylistItem);
    let startIndex = 0;

    if (prevVideoId != null) {
        const idx = entries.findIndex(e => getVideoId(e) === prevVideoId);
        if (idx >= 0) {
            startIndex = idx;
        }
    } else if (typeof player.playlist.currentItem === 'function') {
        const current = player.playlist.currentItem();
        if (current >= 0 && current < entries.length) {
            startIndex = current;
        }
    }

    if (items.length === 0) {
        player.playlist([], -1);
        player.playlist.autoadvance(autoAdvance ? 0 : null);
        return { entries, startIndex: -1 };
    }

    if (prevVideoId != null && prevTime > 5) {
        localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
    }

    player.playlist(items, startIndex);
    player.playlist.autoadvance(autoAdvance ? 0 : null);

    return { entries, startIndex };
}

/**
 * Loads sidecar WebVTT tracks for a library video and attaches them to the Video.js player.
 */
export async function loadAndAttachSubtitleTracksForPlaylist(player, videoDbId) {
    clearRemoteTextTracks(player);
    if (!player || !videoDbId) {
        return;
    }
    try {
        const response = await fetch(`/api/videos/${videoDbId}/subtitles`, { credentials: 'same-origin' });
        if (!response.ok) {
            return;
        }
        const data = await response.json();
        const subtitles = data.subtitles ?? data.Subtitles ?? [];
        const savedLang = localStorage.getItem(STORAGE_KEY_SUBTITLE_LANG) || '';
        for (const sub of subtitles) {
            const url = sub.url ?? sub.Url;
            const lang = sub.lang ?? sub.Lang;
            const label = sub.label ?? sub.Label ?? lang;
            if (!url || !lang) {
                continue;
            }
            player.addRemoteTextTrack(
                {
                    kind: 'captions',
                    src: url,
                    srclang: lang,
                    label,
                    default: savedLang ? String(lang).toLowerCase() === savedLang.toLowerCase() : false
                },
                false
            );
        }
    } catch (error) {
        console.error('Error loading subtitles:', error);
    }
}

export function bindPlaylistSubtitlePreferenceStorage(player) {
    bindSubtitlePreferenceStorage(player);
}

/**
 * POST current list order as the playlist default (PlaylistVideo.Order). Admin only.
 * @param {number} playlistId
 * @param {Array<{ id: number }>} playlistVideos
 * @returns {Promise<boolean>}
 */
export async function applyPlaylistDefaultOrder(playlistId, playlistVideos) {
    const videoOrders = playlistVideos.map((v, i) => ({
        videoId: v.id ?? v.Id,
        order: i + 1
    }));

    const response = await fetch(`/api/playlists/${playlistId}/apply-default-order`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ videoOrders })
    });

    if (!response.ok) {
        let message = 'Failed to apply default order.';
        try {
            const data = await response.json();
            message = data.message || data.title || message;
        } catch {
            /* ignore */
        }
        throw new Error(message);
    }

    return true;
}

export function registerPlaylistButtons() {
    if (videojs.getComponent('PlaylistPrevButton')) return;

    class PlaylistPrevButton extends videojs.getComponent('Button') {
        constructor(player, options) {
            super(player, options);
            this.controlText('Previous Video');
        }
        buildCSSClass() { return 'vjs-playlist-prev-btn ' + super.buildCSSClass(); }
        handleClick(e) {
            super.handleClick(e);
            getVideoJsPlayer()?.playlist?.previous();
        }
    }

    class PlaylistNextButton extends videojs.getComponent('Button') {
        constructor(player, options) {
            super(player, options);
            this.controlText('Next Video');
        }
        buildCSSClass() { return 'vjs-playlist-next-btn ' + super.buildCSSClass(); }
        handleClick(e) {
            super.handleClick(e);
            getVideoJsPlayer()?.playlist?.next();
        }
    }

    videojs.registerComponent('PlaylistPrevButton', PlaylistPrevButton);
    videojs.registerComponent('PlaylistNextButton', PlaylistNextButton);
}

