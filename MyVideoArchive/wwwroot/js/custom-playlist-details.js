import { formatDate, formatDuration, formatNumber } from './utils.js';
import { getTagifyOptions } from './tagify-options.js';
import {
    loadAndAttachSubtitleTracksForPlaylist,
    bindPlaylistSubtitlePreferenceStorage,
    registerPlaylistButtons,
    applyProgrammaticPlaylistReorder,
    destroyPlaylistSortable,
    readPlaylistOrderFromDom,
    rebuildVideoJsPlaylist,
    savePosition,
    loadSavedPosition,
    clearPosition,
    STORAGE_KEY_RATE
} from './playlist-details-shared.js';
import { initVideoExtras, loadVideoExtras } from './video-details-shared.js';
import { buildVideoStreamSource, mergeVideoJsPlayerOptions } from './video-player.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

/**
 * Items from playlistVideos that have a downloaded video.
 * Each item is { video: {...}, ... }. Synced with the Video.js playlist.
 * @type {Array}
 */
let _availableItems = [];

const PAGE_SIZE = 500;
const STORAGE_KEY_AUTO_ADVANCE = 'mva-auto-advance';

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
        this.sortableInstance = null;

        this.autoAdvance.subscribe(val => {
            localStorage.setItem(STORAGE_KEY_AUTO_ADVANCE, val);
            if (player?.playlist) {
                player.playlist.autoadvance(val ? 0 : null);
            }
        });

        // ── Tags ──────────────────────────────────────────────────────────────
        this.videoTags = ko.observableArray([]);
        this._playlistTagifyInstance = null;
        this._videoTagifyInstance = null;
        this._videoTagsSaveTimeout = null;

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatNumber = formatNumber;

        initVideoExtras(this);
        this.loadVideoExtras = (videoId) => loadVideoExtras(this, videoId);
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

            setTimeout(() => this.initializeSortable(), 100);
            this._syncPlayerPlaylist(prevVideoId, prevTime);
        } catch (error) {
            console.error('Error loading playlist:', error);
        } finally {
            this.loading(false);
        }
    };

    initPlaylistTags = async () => {
        try {
            const tagsResponse = await fetch('/api/tags');
            const tagsData = await tagsResponse.json();
            const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

            const playlistTagsResponse = await fetch(`/api/custom-playlists/${this.playlistId}/tags`);
            const playlistTagsData = await playlistTagsResponse.json();
            const currentTags = (playlistTagsData.tags || []).map(t => t.Name ?? t.name);

            const input = document.getElementById('customPlaylistTagsInput');
            if (!input) return;

            this._playlistTagifyInstance = new Tagify(input, getTagifyOptions(allTagNames));

            if (currentTags.length > 0) {
                this._playlistTagifyInstance.addTags(currentTags);
            }

            let saveTimeout = null;
            this._playlistTagifyInstance.on('change', () => {
                clearTimeout(saveTimeout);
                saveTimeout = setTimeout(() => this.savePlaylistTags(), 600);
            });
        } catch (error) {
            console.error('Error initialising custom playlist tags:', error);
        }
    };

    savePlaylistTags = async () => {
        if (!this._playlistTagifyInstance) return;
        const tagNames = this._playlistTagifyInstance.value.map(t => t.value);

        try {
            await fetch(`/api/custom-playlists/${this.playlistId}/tags`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tagNames })
            });
        } catch (error) {
            console.error('Error saving custom playlist tags:', error);
        }
    };

    loadVideoTags = async (videoId) => {
        const input = document.getElementById('customPlaylistPageVideoTagsInput');
        if (!videoId) {
            this.videoTags([]);
            if (this._videoTagifyInstance && input) {
                this._videoTagifyInstance.removeAllTags();
            }
            return;
        }

        try {
            const response = await fetch(`/api/videos/${videoId}/tags`);
            if (!response.ok) {
                this.videoTags([]);
                if (this._videoTagifyInstance && input) this._videoTagifyInstance.removeAllTags();
                return;
            }
            const data = await response.json();
            const tags = (data.tags || []).map(t => t.Name ?? t.name);
            this.videoTags(tags);

            if (!input) return;

            if (!this._videoTagifyInstance) {
                const tagsResponse = await fetch('/api/tags');
                const tagsData = await tagsResponse.json();
                const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);
                this._videoTagifyInstance = new Tagify(input, getTagifyOptions(allTagNames));
                this._videoTagifyInstance.on('change', () => {
                    clearTimeout(this._videoTagsSaveTimeout);
                    this._videoTagsSaveTimeout = setTimeout(() => this.saveVideoTags(), 600);
                });
            }
            this._videoTagifyInstance.removeAllTags();
            if (tags.length > 0) this._videoTagifyInstance.addTags(tags);
        } catch (error) {
            console.error('Error loading video tags:', error);
            this.videoTags([]);
            if (this._videoTagifyInstance && input) this._videoTagifyInstance.removeAllTags();
        }
    };

    saveVideoTags = async () => {
        const video = this.currentVideo();
        if (!video?.id || !this._videoTagifyInstance) return;
        const tagNames = this._videoTagifyInstance.value.map(t => t.value);
        try {
            await fetch(`/api/videos/${video.id}/tags`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tagNames })
            });
            this.videoTags(tagNames);
        } catch (error) {
            console.error('Error saving video tags:', error);
        }
    };

    _syncPlayerPlaylist = (prevVideoId, prevTime) => {
        const entries = this.playlistVideos().filter(item => item.video.downloadedAt);
        const { entries: playable } = rebuildVideoJsPlaylist(player, {
            entries,
            toPlaylistItem: item => ({
                sources: [buildVideoStreamSource(item.video.id, item.video.streamContentType)],
                poster: item.video.thumbnailUrl || undefined,
                name: item.video.title
            }),
            getVideoId: item => item.video.id,
            prevVideoId,
            prevTime,
            autoAdvance: this.autoAdvance()
        });
        _availableItems = playable;
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

    initializeSortable = () => {
        const container = document.getElementById('videoListContainer');
        if (!container) return;
        this.sortableInstance = destroyPlaylistSortable(this.sortableInstance);

        this.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
            filter: '.mva-move-to-top',
            preventOnFilter: false,
            animation: 150,
            onEnd: async () => {
                const newOrder = readPlaylistOrderFromDom(container, videoId =>
                    this.playlistVideos().find(v => v.video.id === videoId)
                );
                if (newOrder.length === 0) {
                    return;
                }
                applyProgrammaticPlaylistReorder(this, 'videoListContainer', newOrder, v => v.video.id);
                const prevVideoId = this.currentVideo()?.id;
                const prevTime = player ? Math.floor(player.currentTime()) : 0;
                this._syncPlayerPlaylist(prevVideoId, prevTime);
                await this.savePlaylistOrder();
                this.initializeSortable();
            }
        });
    };

    moveVideoToTop = async (item) => {
        if (!item?.video) return;
        const arr = this.playlistVideos().slice();
        const idx = arr.findIndex(v => v.video.id === item.video.id);
        if (idx <= 0) return;
        arr.splice(idx, 1);
        arr.unshift(item);

        applyProgrammaticPlaylistReorder(this, 'videoListContainer', arr, v => v.video.id);

        const prevVideoId = this.currentVideo()?.id;
        const prevTime = player ? Math.floor(player.currentTime()) : 0;
        this._syncPlayerPlaylist(prevVideoId, prevTime);

        await this.savePlaylistOrder();
        this.initializeSortable();
    };

    savePlaylistOrder = async () => {
        const videoOrders = this.playlistVideos().map((item, i) => ({
            videoId: item.video.id,
            order: i + 1
        }));

        try {
            const response = await fetch(`/api/custom-playlists/${this.playlistId}/reorder`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoOrders })
            });
            if (!response.ok) {
                let message = 'Failed to save playlist order.';
                try {
                    const data = await response.json();
                    message = data.message || data.title || message;
                } catch {
                    /* ignore */
                }
                throw new Error(message);
            }
        } catch (error) {
            console.error('Error saving playlist order:', error);
            toast.error(error.message || 'Error saving playlist order.');
        }
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
                setTimeout(() => this.initializeSortable(), 100);
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

    player = videojs('videoPlayer', mergeVideoJsPlayerOptions({
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: { skipButtons: { forward: 10, backward: 10 } },
        userActions: { hotkeys: true }
    }));

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
        bindPlaylistSubtitlePreferenceStorage(player);
    });

    player.on('ratechange', () => {
        localStorage.setItem(STORAGE_KEY_RATE, player.playbackRate());
    });

    let _currentVideoId = null;

    player.on('playlistitem', async () => {
        const idx = player.playlist.currentItem();
        const item = _availableItems[idx];
        if (!item) return;

        _currentVideoId = item.video.id;
        viewModel.currentVideo(item.video);
        await Promise.all([
            viewModel.loadVideoTags(item.video.id),
            loadVideoExtras(viewModel, item.video.id)
        ]);

        setTimeout(() => {
            const activeItem = document.querySelector('.playlist-video-item.active');
            if (activeItem) activeItem.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }, 100);
    });

    player.on('loadedmetadata', () => {
        if (!_currentVideoId) return;
        void loadAndAttachSubtitleTracksForPlaylist(player, _currentVideoId);
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
    await viewModel.initPlaylistTags();
});
