import { formatDate, formatDuration, formatNumber } from './utils.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

/**
 * Filtered list of playlistVideos that are actually playable (downloaded + not hidden).
 * Kept in sync with the player's playlist.
 * @type {Array}
 */
let _availableVideos = [];

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
class PlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.useCustomOrder = ko.observable(false);
        this.showHidden = ko.observable(false);
        this.autoAdvance = ko.observable(
            localStorage.getItem(STORAGE_KEY_AUTO_ADVANCE) !== 'false'
        );
        this.sortableInstance = null;

        this.visibleVideoCount = ko.computed(() =>
            this.playlistVideos().filter(v => !v.isHidden).length
        );

        // Toggle autoadvance on the plugin when the observable changes
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
        try {
            await Promise.all([this._fetchPlaylist(), this._loadOrderSetting()]);
            this.loading(false);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error loading playlist:', error);
            this.loading(false);
        }
    };

    _fetchPlaylist = async () => {
        const response = await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`);
        if (!response.ok) throw new Error('Playlist not found');
        this.playlist(await response.json());
    };

    _loadOrderSetting = async () => {
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/order-setting`);
            if (response.ok) {
                const data = await response.json();
                this.useCustomOrder(data.useCustomOrder);
            }
        } catch (error) {
            console.error('Error loading order setting:', error);
        }
    };

    loadPlaylistVideos = async () => {
        this.loadingVideos(true);

        // Remember what was playing so we can restore it after a rebuild
        const prevVideoId = this.currentVideo()?.id;
        const prevTime = player ? Math.floor(player.currentTime()) : 0;

        await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`)
            .then(r => r.json()).then(d => this.playlist(d)).catch(() => {});

        await fetch(`/api/playlists/${this.playlistId}/videos?useCustomOrder=${this.useCustomOrder()}&showHidden=${this.showHidden()}`)
            .then(r => r.json())
            .then(async data => {
                const videos = (data.videos || []).map(v => ({ ...v, watched: false }));
                this.playlistVideos(videos);
                this.loadingVideos(false);

                if (videos.length > 0) {
                    try {
                        const wr = await fetch(`/api/user/videos/watched/by-playlist/${this.playlistId}`);
                        const wd = await wr.json();
                        const watchedSet = new Set(wd.watchedIds || []);
                        this.playlistVideos().forEach(v => { v.watched = watchedSet.has(v.id); });
                        this.playlistVideos.valueHasMutated();
                    } catch (e) { /* non-critical */ }
                }

                setTimeout(() => this.initializeSortable(), 100);
                this._syncPlayerPlaylist(prevVideoId, prevTime);
            })
            .catch(error => {
                console.error('Error loading playlist videos:', error);
                this.loadingVideos(false);
            });
    };

    /** Rebuild the Video.js playlist from currently available videos. */
    _syncPlayerPlaylist = (prevVideoId, prevTime) => {
        if (!player?.playlist) return;

        _availableVideos = this.playlistVideos().filter(v => v.downloadedAt && !v.isHidden);

        const items = _availableVideos.map(v => ({
            sources: [{ src: `/api/videos/${v.id}/stream`, type: 'video/mp4' }],
            poster: v.thumbnailUrl || undefined,
            name: v.title
        }));

        // player.playlist(items) auto-calls currentItem(0) internally, firing 'playlistitem'
        player.playlist(items);
        player.playlist.autoadvance(this.autoAdvance() ? 0 : null);

        if (_availableVideos.length === 0) return;

        // If a specific video was playing before, seek to it at its saved position
        if (prevVideoId) {
            const idx = _availableVideos.findIndex(v => v.id === prevVideoId);
            if (idx > 0) {
                // Persist prevTime so the loadedmetadata handler can restore it
                if (prevTime > 5) localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
                player.playlist.currentItem(idx);
            } else if (idx === 0 && prevTime > 5) {
                // Already at index 0 from auto-call, just persist the time
                localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
            }
        }
    };

    /** Called when the user clicks a video in the sidebar. */
    playVideo = (video) => {
        if (!video.downloadedAt || video.isHidden) return;
        const index = _availableVideos.findIndex(v => v.id === video.id);
        if (index >= 0) {
            player.playlist.currentItem(index);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    };

    prevVideo = () => player?.playlist?.previous();
    nextVideo = () => player?.playlist?.next();

    downloadVideo = async (video, event) => {
        event.stopPropagation();
        if (video.isQueued) { toast.warning('This video is already queued for download.'); return; }
        if (!confirm(`Queue "${video.title}" for download?`)) return;

        try {
            const response = await fetch(`/api/channels/${video.channelId}/videos/download`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoIds: [video.id] })
            });
            const data = await response.json();
            toast.info(data.message);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error queueing video:', error);
            toast.error('Error queueing video for download. Please try again.');
        }
    };

    hideVideo = async (video) => {
        try {
            await fetch(`/api/playlists/${this.playlistId}/videos/${video.id}/hidden`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isHidden: true })
            });
            await this.loadPlaylistVideos();
        } catch (error) { console.error('Error hiding video:', error); }
    };

    unhideVideo = async (video) => {
        try {
            await fetch(`/api/playlists/${this.playlistId}/videos/${video.id}/hidden`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isHidden: false })
            });
            await this.loadPlaylistVideos();
        } catch (error) { console.error('Error unhiding video:', error); }
    };

    toggleShowHidden = () => { this.loadPlaylistVideos(); return true; };

    toggleOrderMode = () => {
        if (!this.useCustomOrder()) { this.clearCustomOrder(); } else { this.enableCustomOrder(); }
        return true;
    };

    enableCustomOrder = async () => {
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/order-setting`);
            const data = await response.json();
            if (!data.hasCustomOrder) { await this.saveCustomOrder(true); }
            else { await this.loadPlaylistVideos(); }
        } catch (error) {
            console.error('Error checking custom order:', error);
            await this.saveCustomOrder(true);
        }
    };

    clearCustomOrder = async () => { await this.saveCustomOrder(true); };

    initializeSortable = () => {
        const container = document.getElementById('videoListContainer');
        if (!container) return;
        if (this.sortableInstance) this.sortableInstance.destroy();

        this.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
            animation: 150,
            disabled: !this.useCustomOrder(),
            onEnd: async () => {
                const items = container.querySelectorAll('.playlist-video-item');
                const newOrder = [];
                items.forEach(item => {
                    const videoId = parseInt(item.getAttribute('data-video-id'));
                    const video = this.playlistVideos().find(v => v.id === videoId);
                    if (video) newOrder.push(video);
                });
                this.playlistVideos(newOrder);
                await this.saveCustomOrder();
            }
        });
    };

    saveCustomOrder = async (reloadAfterSave) => {
        const videoOrders = this.useCustomOrder()
            ? this.playlistVideos().filter(v => !v.isHidden).map((v, i) => ({ videoId: v.id, order: i + 1 }))
            : [];

        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/reorder`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ useCustomOrder: this.useCustomOrder(), videoOrders })
            });
            if (!response.ok) throw new Error('Server error');
            if (this.sortableInstance) this.sortableInstance.option('disabled', !this.useCustomOrder());
            if (reloadAfterSave) await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error saving order:', error);
            toast.error('Error saving custom order: ' + error.message);
        }
    };
}

// ─── Bootstrap ────────────────────────────────────────────────────────────────
var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    registerPlaylistButtons();

    viewModel = new PlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);

    // Initialise Video.js
    player = videojs('videoPlayer', {
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: { skipButtons: { forward: 10, backward: 10 } },
        userActions: { hotkeys: true }
    });

    // Add Prev / Next playlist navigation buttons to the control bar
    player.ready(() => {
        player.controlBar.addChild('PlaylistPrevButton', {}, 0);

        // Insert NextButton right after SkipForward (find it by class)
        const children = player.controlBar.children();
        let insertAt = 4; // safe fallback
        for (let i = 0; i < children.length; i++) {
            if (children[i].hasClass?.('vjs-skip-forward-10')) {
                insertAt = i + 1;
                break;
            }
        }
        player.controlBar.addChild('PlaylistNextButton', {}, insertAt);

        // Restore saved playback rate
        const savedRate = parseFloat(localStorage.getItem(STORAGE_KEY_RATE) || '1');
        player.playbackRate(savedRate);
    });

    player.on('ratechange', () => {
        localStorage.setItem(STORAGE_KEY_RATE, player.playbackRate());
    });

    // ── Playlist item change ──────────────────────────────────────────────────
    // Single source of truth: all UI updates happen here when the playing item changes
    let _currentVideoId = null;

    player.on('playlistitem', async () => {
        const idx = player.playlist.currentItem();
        const video = _availableVideos[idx];
        if (!video) return;

        _currentVideoId = video.id;
        viewModel.currentVideo(video);

        // Scroll active sidebar item into view
        setTimeout(() => {
            const activeItem = document.querySelector('.playlist-video-item.active');
            if (activeItem) activeItem.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }, 100);

        // Mark watched (idempotent API)
        if (!video.watched) {
            try {
                await fetch(`/api/user/videos/${video.id}/watched`, { method: 'POST' });
                video.watched = true;
                viewModel.playlistVideos.valueHasMutated();
            } catch (e) { /* non-critical */ }
        }
    });

    // ── Restore playback position after metadata loads ────────────────────────
    player.on('loadedmetadata', () => {
        if (!_currentVideoId) return;
        const saved = loadSavedPosition(_currentVideoId);
        if (saved > 5 && isFinite(player.duration()) && saved < player.duration() - 5) {
            player.currentTime(saved);
        }
    });

    // ── Save position every 2 seconds while playing ───────────────────────────
    let _lastSave = 0;
    player.on('timeupdate', () => {
        if (!_currentVideoId) return;
        const now = Date.now();
        if (now - _lastSave >= 2000) {
            _lastSave = now;
            savePosition(_currentVideoId, player.currentTime());
        }
    });

    // ── Clear saved position when a video finishes ────────────────────────────
    player.on('ended', () => {
        if (_currentVideoId) clearPosition(_currentVideoId);
    });

    await viewModel.loadPlaylist();
});
