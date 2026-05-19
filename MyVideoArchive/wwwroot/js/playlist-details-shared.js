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
    bindSubtitlePreferenceStorage,
    loadVideoExtras,
    removeVideoExtra,
    openVideoExtrasPicker,
    reloadVideoExtrasPicker,
    confirmVideoExtrasPicker
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
    return loadVideoExtras(vm, videoId);
}

export async function openVideoExtrasPickerForPlaylist(vm) {
    return openVideoExtrasPicker(vm);
}

export async function reloadVideoExtrasPickerForPlaylist(vm) {
    return reloadVideoExtrasPicker(vm);
}

export async function confirmVideoExtrasPickerForPlaylist(vm) {
    return confirmVideoExtrasPicker(vm);
}

export async function removeVideoExtraForPlaylist(vm, item) {
    return removeVideoExtra(vm, item);
}
