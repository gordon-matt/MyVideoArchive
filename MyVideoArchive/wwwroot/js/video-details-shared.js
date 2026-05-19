/**
 * Shared helpers for standard and custom video detail pages.
 */
import { getTagifyOptions } from './tagify-options.js';
import { buildVideoStreamSource, mergeVideoJsPlayerOptions } from './video-player.js';
import { isTextualExtraFileName, openExtraTextViewerModal } from './extras-text-viewer.js';

export const STORAGE_KEY_RATE = 'mva-playback-rate';
export const STORAGE_KEY_SUBTITLE_LANG = 'mva-subtitle-lang';

export function savePosition(videoId, time) {
    if (time > 5) localStorage.setItem(`mva-pos-${videoId}`, Math.floor(time));
}

export function loadSavedPosition(videoId) {
    return parseInt(localStorage.getItem(`mva-pos-${videoId}`) || '0', 10);
}

export function clearPosition(videoId) {
    localStorage.removeItem(`mva-pos-${videoId}`);
}

export function clearRemoteTextTracks(player) {
    if (!player?.remoteTextTracks) {
        return;
    }
    const list = player.remoteTextTracks();
    for (let i = list.length - 1; i >= 0; i--) {
        player.removeRemoteTextTrack(list[i]);
    }
}

const _playersWithSubtitlePrefListener = new WeakSet();

/** Persist caption on/off to localStorage (same behaviour across video and playlist pages). */
export function bindSubtitlePreferenceStorage(player) {
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

export function attachSubtitleTracks(player, subtitles) {
    clearRemoteTextTracks(player);
    if (!player || !Array.isArray(subtitles) || subtitles.length === 0) {
        return;
    }

    const savedLang = localStorage.getItem(STORAGE_KEY_SUBTITLE_LANG) || '';

    subtitles.forEach(sub => {
        player.addRemoteTextTrack({
            kind: 'captions',
            src: sub.url,
            srclang: sub.lang,
            label: sub.label,
            default: savedLang
                ? sub.lang.toLowerCase() === savedLang.toLowerCase()
                : false
        }, false);
    });

    bindSubtitlePreferenceStorage(player);
}

export function getVideoDetailsPlayerOptions() {
    return {
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: {
            skipButtons: {
                forward: 10,
                backward: 10
            }
        },
        userActions: {
            hotkeys: true
        }
    };
}

/** @returns {import('video.js').VideoJsPlayer} */
export function createVideoDetailsPlayer(elementId = 'videoPlayer', mimeHint) {
    const player = videojs(elementId, mergeVideoJsPlayerOptions(getVideoDetailsPlayerOptions(), mimeHint));

    player.ready(() => {
        const savedRate = parseFloat(localStorage.getItem(STORAGE_KEY_RATE) || '1');
        player.playbackRate(savedRate);
    });

    player.on('ratechange', () => {
        localStorage.setItem(STORAGE_KEY_RATE, player.playbackRate());
    });

    return player;
}

export function bindPlaybackPositionPersistence(player, videoDbId) {
    player.on('loadedmetadata', () => {
        const saved = loadSavedPosition(videoDbId);
        if (saved > 5 && isFinite(player.duration()) && saved < player.duration() - 5) {
            player.currentTime(saved);
        }
    });

    let _lastSave = 0;
    player.on('timeupdate', () => {
        const now = Date.now();
        if (now - _lastSave >= 2000) {
            _lastSave = now;
            savePosition(videoDbId, player.currentTime());
        }
    });

    player.on('ended', () => clearPosition(videoDbId));
}

export function disposeVideoJsPlayer(player) {
    if (player) {
        player.dispose();
    }
}

/**
 * @param {import('video.js').VideoJsPlayer | null} currentPlayer
 * @returns {import('video.js').VideoJsPlayer | null}
 */
export function syncVideoDetailsPlayer(currentPlayer, videoDbId, filePath, subtitles, streamContentType) {
    if (!filePath) {
        disposeVideoJsPlayer(currentPlayer);
        return null;
    }

    const mimeHint = streamContentType ?? filePath;

    let player = currentPlayer;
    if (!player) {
        player = createVideoDetailsPlayer('videoPlayer', mimeHint);
        bindPlaybackPositionPersistence(player, videoDbId);
    }

    const setSource = () => {
        player.src(buildVideoStreamSource(videoDbId, mimeHint));
        attachSubtitleTracks(player, subtitles);
    };

    if (player.isReady_) {
        setSource();
    } else {
        player.ready(setSource);
    }

    return player;
}

export async function fetchVideoSubtitles(videoDbId) {
    try {
        const response = await fetch(`/api/videos/${videoDbId}/subtitles`);
        if (!response.ok) {
            return [];
        }
        const data = await response.json();
        return data.subtitles || [];
    } catch (error) {
        console.error('Error loading subtitles:', error);
        return [];
    }
}

/** @returns {number | null} */
export function resolveVideoDbId(vm) {
    if (vm.videoId != null) {
        return vm.videoId;
    }
    const video = typeof vm.currentVideo === 'function'
        ? vm.currentVideo()
        : typeof vm.video === 'function'
            ? vm.video()
            : null;
    return video?.Id ?? video?.id ?? null;
}

export function initVideoExtrasBindings(vm) {
    vm.extrasItems = ko.observableArray([]);
    vm.extrasLoading = ko.observable(false);
    vm.isTextualExtraName = name => isTextualExtraFileName(name);
    vm.openTextExtra = item => openExtraTextViewerModal(item.id, item.fileName);
    vm.removeVideoExtra = async (item) => removeVideoExtra(vm, item);
}

export async function loadVideoExtras(vm, videoId) {
    const id = videoId ?? resolveVideoDbId(vm);
    if (!id) {
        vm.extrasItems([]);
        return;
    }

    vm.extrasLoading(true);
    try {
        const response = await fetch(`/api/videos/${id}/additional-content`);
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

export async function removeVideoExtra(vm, item) {
    if (globalThis.isAdmin !== true) {
        return;
    }

    const videoId = resolveVideoDbId(vm);
    if (!videoId) {
        return;
    }

    if (!confirm(`Remove "${item.fileName}" from this video? The file will remain on the channel.`)) {
        return;
    }

    try {
        const response = await fetch(
            `/api/videos/${videoId}/additional-content/${item.id}`,
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

export async function initVideoPageTags(vm) {
    try {
        const tagsResponse = await fetch('/api/tags');
        const tagsData = await tagsResponse.json();
        const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

        const videoTagsResponse = await fetch(`/api/videos/${vm.videoId}/tags`);
        const videoTagsData = await videoTagsResponse.json();
        const currentTags = (videoTagsData.tags || []).map(t => t.Name ?? t.name);

        const input = document.getElementById('videoTagsInput');
        if (!input) return;

        vm._tagifyInstance = new Tagify(input, getTagifyOptions(allTagNames));

        if (currentTags.length > 0) {
            vm._tagifyInstance.addTags(currentTags);
        }

        let saveTimeout = null;
        vm._tagifyInstance.on('change', () => {
            clearTimeout(saveTimeout);
            saveTimeout = setTimeout(() => vm.saveTags(), 600);
        });
    } catch (error) {
        console.error('Error initialising tags:', error);
    }
}

/**
 * @param {object} vm
 * @param {{ afterSave?: () => Promise<void> | void }} [options]
 */
export async function saveVideoPageTags(vm, options = {}) {
    if (!vm._tagifyInstance) return;
    const tagNames = vm._tagifyInstance.value.map(t => t.value);

    try {
        await fetch(`/api/videos/${vm.videoId}/tags`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tagNames })
        });

        if (options.afterSave) {
            await options.afterSave();
        }
    } catch (error) {
        console.error('Error saving tags:', error);
    }
}
