import { formatDate, formatDuration, formatNumber } from './utils.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

/**
 * Items from playlistVideos that have a downloaded video.
 * Each item is { video: {...}, ... }. Synced with the Video.js playlist.
 * @type {Array}
 */
let _availableItems = [];

const PAGE_SIZE = 500;
const STORAGE_KEY_RATE = 'mva-playback-rate';
const STORAGE_KEY_AUTO_ADVANCE = 'mva-auto-advance';

// ─── Playback position persistence ────────────────────────────────────────────
function savePosition(videoId, time) {
    if (time > 5) localStorage.setItem(`mva-pos-${videoId}`, Math.floor(time));
}
function loadSavedPosition(videoId) {
    return parseInt(localStorage.getItem(`mva-pos-${videoId}`) || '0', 10);
}
function clearPosition(videoId) {
    localStorage.removeItem(`mva-pos-${videoId}`);
}

// ─── Custom control bar buttons ────────────────────────────────────────────────
function registerPlaylistButtons() {
    if (videojs.getComponent('PlaylistPrevButton')) return;

    class PlaylistPrevButton extends videojs.getComponent('Button') {
        constructor(player, options) {
            super(player, options);
            this.controlText('Previous Video');
        }
        buildCSSClass() { return 'vjs-playlist-prev-btn ' + super.buildCSSClass(); }
        handleClick(e) { super.handleClick(e); player?.playlist?.previous(); }
    }

    class PlaylistNextButton extends videojs.getComponent('Button') {
        constructor(player, options) {
            super(player, options);
            this.controlText('Next Video');
        }
        buildCSSClass() { return 'vjs-playlist-next-btn ' + super.buildCSSClass(); }
        handleClick(e) { super.handleClick(e); player?.playlist?.next(); }
    }

    videojs.registerComponent('PlaylistPrevButton', PlaylistPrevButton);
    videojs.registerComponent('PlaylistNextButton', PlaylistNextButton);
}

// ─── ViewModel ────────────────────────────────────────────────────────────────
class CustomPlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.autoAdvance = ko.observable(
            localStorage.getItem(STORAGE_KEY_AUTO_ADVANCE) !== 'false'
        );

        this.autoAdvance.subscribe(val => {
            localStorage.setItem(STORAGE_KEY_AUTO_ADVANCE, val);
            if (player?.playlist) {
                player.playlist.autoadvance(val ? 0 : null);
            }
        });

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatNumber = formatNumber;
    }

    loadPlaylist = async () => {
        this.loading(true);

        const prevVideoId = this.currentVideo()?.id;
        const prevTime = player ? Math.floor(player.currentTime()) : 0;

        try {
            const params = new URLSearchParams({ page: 1, pageSize: PAGE_SIZE });
            const response = await fetch(`/api/custom-playlists/${this.playlistId}/videos?${params}`);
            if (!response.ok) { console.error('Playlist not found'); return; }

            const data = await response.json();
            this.playlist(data.playlist);
            this.playlistVideos(data.videos || []);

            document.title = `${data.playlist?.name ?? 'Playlist'} - MyVideoArchive`;

            this._syncPlayerPlaylist(prevVideoId, prevTime);
        } catch (error) {
            console.error('Error loading playlist:', error);
        } finally {
            this.loading(false);
        }
    };

    _syncPlayerPlaylist = (prevVideoId, prevTime) => {
        if (!player?.playlist) return;

        // Items with a downloaded video (not hidden — custom playlists don't have hidden)
        _availableItems = this.playlistVideos().filter(item => item.video.downloadedAt);

        const items = _availableItems.map(item => ({
            sources: [{ src: `/api/videos/${item.video.id}/stream`, type: 'video/mp4' }],
            poster: item.video.thumbnailUrl || undefined,
            name: item.video.title
        }));

        player.playlist(items);
        player.playlist.autoadvance(this.autoAdvance() ? 0 : null);

        if (_availableItems.length === 0) return;

        if (prevVideoId) {
            const idx = _availableItems.findIndex(i => i.video.id === prevVideoId);
            if (idx > 0) {
                if (prevTime > 5) localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
                player.playlist.currentItem(idx);
            } else if (idx === 0 && prevTime > 5) {
                localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
            }
        }
    };

    /** Called when the user clicks a video in the sidebar. */
    playVideo = (item) => {
        if (!item.video.downloadedAt) return;
        const index = _availableItems.findIndex(i => i.video.id === item.video.id);
        if (index >= 0) {
            player.playlist.currentItem(index);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    };

    prevVideo = () => player?.playlist?.previous();
    nextVideo = () => player?.playlist?.next();

    openVideoDetails = () => {
        const video = this.currentVideo();
        if (video) window.location.href = `/videos/${video.id}`;
    };

    removeVideo = async (item) => {
        if (!confirm(`Remove "${item.video.title}" from this playlist?`)) return;

        const wasCurrentVideo = this.currentVideo()?.id === item.video.id;

        try {
            const response = await fetch(
                `/api/custom-playlists/${this.playlistId}/videos/${item.video.id}`,
                { method: 'DELETE' });

            if (response.ok) {
                if (wasCurrentVideo) this.currentVideo(null);
                this.playlistVideos.remove(item);
                // Rebuild playlist; if current video was removed, start fresh from index 0
                const newPrevId = wasCurrentVideo ? null : this.currentVideo()?.id;
                const newPrevTime = wasCurrentVideo || !player ? 0 : Math.floor(player.currentTime());
                this._syncPlayerPlaylist(newPrevId, newPrevTime);
            } else {
                toast.error('Failed to remove video from playlist.');
            }
        } catch (error) {
            console.error('Error removing video:', error);
            toast.error('An error occurred. Please try again.');
        }
    };
}

// ─── Bootstrap ────────────────────────────────────────────────────────────────
var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    registerPlaylistButtons();

    viewModel = new CustomPlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);

    player = videojs('videoPlayer', {
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: { skipButtons: { forward: 10, backward: 10 } },
        userActions: { hotkeys: true }
    });

    player.ready(() => {
        player.controlBar.addChild('PlaylistPrevButton', {}, 0);

        const children = player.controlBar.children();
        let insertAt = 4;
        for (let i = 0; i < children.length; i++) {
            if (children[i].hasClass?.('vjs-skip-forward-10')) {
                insertAt = i + 1;
                break;
            }
        }
        player.controlBar.addChild('PlaylistNextButton', {}, insertAt);

        const savedRate = parseFloat(localStorage.getItem(STORAGE_KEY_RATE) || '1');
        player.playbackRate(savedRate);
    });

    player.on('ratechange', () => {
        localStorage.setItem(STORAGE_KEY_RATE, player.playbackRate());
    });

    let _currentVideoId = null;

    player.on('playlistitem', () => {
        const idx = player.playlist.currentItem();
        const item = _availableItems[idx];
        if (!item) return;

        _currentVideoId = item.video.id;
        viewModel.currentVideo(item.video);

        setTimeout(() => {
            const activeItem = document.querySelector('.playlist-video-item.active');
            if (activeItem) activeItem.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }, 100);
    });

    player.on('loadedmetadata', () => {
        if (!_currentVideoId) return;
        const saved = loadSavedPosition(_currentVideoId);
        if (saved > 5 && isFinite(player.duration()) && saved < player.duration() - 5) {
            player.currentTime(saved);
        }
    });

    let _lastSave = 0;
    player.on('timeupdate', () => {
        if (!_currentVideoId) return;
        const now = Date.now();
        if (now - _lastSave >= 2000) {
            _lastSave = now;
            savePosition(_currentVideoId, player.currentTime());
        }
    });

    player.on('ended', () => {
        if (_currentVideoId) clearPosition(_currentVideoId);
    });

    await viewModel.loadPlaylist();
});
