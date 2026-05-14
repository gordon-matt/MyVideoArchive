/**
 * Shared helpers for playlist Details and DetailsCustom (Video.js playlist + per-video extras).
 */

/** @type {() => import('video.js').VideoJsPlayer | null} */
let getVideoJsPlayer = () => null;

export function setPlaylistVideoJsPlayerGetter(fn) {
    getVideoJsPlayer = fn;
}

export const STORAGE_KEY_RATE = 'mva-playback-rate';
export const STORAGE_KEY_AUTO_ADVANCE = 'mva-auto-advance';

/** Same key as `video-details.js` so caption choice persists across pages. */
export const STORAGE_KEY_SUBTITLE_LANG = 'mva-subtitle-lang';

/** Remove remote text tracks before switching to another video. */
export function clearRemoteTextTracks(player) {
    if (!player?.remoteTextTracks) {
        return;
    }
    const list = player.remoteTextTracks();
    for (let i = list.length - 1; i >= 0; i--) {
        player.removeRemoteTextTrack(list[i]);
    }
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

const _playersWithSubtitlePrefListener = new WeakSet();

/** Persist caption on/off to localStorage (same behaviour as single-video page). */
export function bindPlaylistSubtitlePreferenceStorage(player) {
    if (!player?.textTracks || _playersWithSubtitlePrefListener.has(player)) {
        return;
    }
    _playersWithSubtitlePrefListener.add(player);
    player.textTracks().addEventListener('change', () => {
        const tracks = player.textTracks();
        for (let i = 0; i < tracks.length; i++) {
            const t = tracks[i];
            if (t.kind === 'captions' && t.mode === 'showing' && t.language) {
                localStorage.setItem(STORAGE_KEY_SUBTITLE_LANG, t.language);
                return;
            }
        }
        localStorage.removeItem(STORAGE_KEY_SUBTITLE_LANG);
    });
}

export function savePosition(videoId, time) {
    if (time > 5) localStorage.setItem(`mva-pos-${videoId}`, Math.floor(time));
}

export function loadSavedPosition(videoId) {
    return parseInt(localStorage.getItem(`mva-pos-${videoId}`) || '0', 10);
}

export function clearPosition(videoId) {
    localStorage.removeItem(`mva-pos-${videoId}`);
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

export async function loadVideoExtrasForPlaylist(vm, videoId) {
    if (!videoId) {
        vm.extrasItems([]);
        return;
    }
    vm.extrasLoading(true);
    try {
        const response = await fetch(`/api/videos/${videoId}/additional-content`);
        if (response.ok) {
            const data = await response.json();
            vm.extrasItems(data.items || []);
        } else {
            vm.extrasItems([]);
        }
    } catch (error) {
        console.error('Error loading video extras:', error);
        vm.extrasItems([]);
    } finally {
        vm.extrasLoading(false);
    }
}

async function loadExtrasPickerItemsForCurrentVideo(vm) {
    const video = vm.currentVideo();
    if (!video?.id || globalThis.isAdmin !== true) {
        return;
    }

    vm.extrasPickerLoading(true);
    try {
        const onlyUnassigned =
            typeof vm.extrasPickerOnlyUnassigned === 'function' && vm.extrasPickerOnlyUnassigned();
        const query = onlyUnassigned ? '?onlyUnassignedInPlaylist=true' : '';
        const response = await fetch(
            `/api/playlists/${vm.playlistId}/videos/${video.id}/additional-content/available${query}`
        );
        if (response.ok) {
            const data = await response.json();
            vm.extrasPickerItems(data.items || []);
        } else {
            toast.error('Could not load available files.');
        }
    } catch (error) {
        console.error('Error loading extras picker:', error);
        toast.error('Could not load available files.');
    } finally {
        vm.extrasPickerLoading(false);
    }
}

export async function openVideoExtrasPickerForPlaylist(vm) {
    const video = vm.currentVideo();
    if (!video?.id || globalThis.isAdmin !== true) return;

    if (typeof vm.extrasPickerOnlyUnassigned === 'function') {
        vm.extrasPickerOnlyUnassigned(false);
    }

    vm.extrasPickerSelectedIds([]);
    vm.extrasPickerItems([]);
    const modal = new bootstrap.Modal(document.getElementById('videoExtrasPickerModal'));
    modal.show();

    await loadExtrasPickerItemsForCurrentVideo(vm);
}

/** Refetch picker list (e.g. after toggling "Only show unassigned"). Clears checkbox selections. */
export async function reloadVideoExtrasPickerForPlaylist(vm) {
    const video = vm.currentVideo();
    if (!video?.id || globalThis.isAdmin !== true) return;

    vm.extrasPickerSelectedIds([]);
    await loadExtrasPickerItemsForCurrentVideo(vm);
}

export async function confirmVideoExtrasPickerForPlaylist(vm) {
    const video = vm.currentVideo();
    const ids = vm.extrasPickerSelectedIds().slice();
    if (!video?.id || ids.length === 0) return;

    vm.extrasPickerSaving(true);
    try {
        const response = await fetch(
            `/api/playlists/${vm.playlistId}/videos/${video.id}/additional-content`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ itemIds: ids })
            }
        );
        if (response.ok) {
            bootstrap.Modal.getInstance(document.getElementById('videoExtrasPickerModal'))?.hide();
            vm.extrasPickerSelectedIds([]);
            await loadVideoExtrasForPlaylist(vm, video.id);
            toast.success('Files associated with this video.');
        } else {
            const data = await response.json().catch(() => ({}));
            toast.error(data.message || 'Failed to associate files.');
        }
    } catch (error) {
        console.error('Error associating extras:', error);
        toast.error('Failed to associate files.');
    } finally {
        vm.extrasPickerSaving(false);
    }
}

export async function removeVideoExtraForPlaylist(vm, item) {
    if (globalThis.isAdmin !== true) return;
    const video = vm.currentVideo();
    if (!video?.id) return;
    if (!confirm(`Remove "${item.fileName}" from this video? The file will remain on the channel.`)) return;

    try {
        const response = await fetch(
            `/api/videos/${video.id}/additional-content/${item.id}`,
            { method: 'DELETE' }
        );
        if (response.ok) {
            vm.extrasItems.remove(item);
            toast.success('Removed from this video.');
        } else {
            const data = await response.json().catch(() => ({}));
            toast.error(data.message || 'Failed to remove association.');
        }
    } catch (error) {
        console.error('Error removing video extra:', error);
        toast.error('Failed to remove association.');
    }
}
