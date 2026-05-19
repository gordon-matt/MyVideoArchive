import { formatDate, formatDuration, formatNumber } from './utils.js';
import { getTagifyOptions } from './tagify-options.js';
import {
    setPlaylistVideoJsPlayerGetter,
    STORAGE_KEY_RATE,
    STORAGE_KEY_AUTO_ADVANCE,
    savePosition,
    loadSavedPosition,
    clearPosition,
    registerPlaylistButtons,
    loadAndAttachSubtitleTracksForPlaylist,
    bindPlaylistSubtitlePreferenceStorage
} from './playlist-details-shared.js';
import { initVideoExtras, loadVideoExtras } from './video-details-shared.js';
import { buildVideoStreamSource, mergeVideoJsPlayerOptions } from './video-player.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

/**
 * Filtered list of playlistVideos that are actually playable (downloaded + not hidden).
 * Kept in sync with the player's playlist.
 * @type {Array}
 */
let _availableVideos = [];

// ─── ViewModel ────────────────────────────────────────────────────────────────
class PlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        /** Channel playlist id for extras picker (not set on My Playlists pages). */
        this.channelPlaylistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.refreshing = ko.observable(false);
        this.useCustomOrder = ko.observable(false);
        this.showHidden = ko.observable(false);
        this.autoAdvance = ko.observable(
            localStorage.getItem(STORAGE_KEY_AUTO_ADVANCE) !== 'false'
        );
        this.sortableInstance = null;

        this.visibleVideoCount = ko.computed(() =>
            this.playlistVideos().filter(v => !v.isHidden).length
        );

        // ── Download All modal ────────────────────────────────────────────────
        this.downloadAllModalVideos = ko.observableArray([]);
        this.downloadingAll = ko.observable(false);
        this.downloadAllSelectAll = ko.observable(true);

        this.downloadAllSelectAll.subscribe(newValue => {
            this.downloadAllModalVideos().forEach(v => v.selectedForDownload(newValue));
        });

        this.hasDownloadableVideos = ko.computed(() =>
            this.playlistVideos().some(v =>
                !v.downloadedAt && !v.isQueued && !v.downloadFailed &&
                v.title !== PRIVATE_VIDEO_TITLE && v.title !== DELETED_VIDEO_TITLE
            )
        );

        // Toggle autoadvance on the plugin when the observable changes
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

        // ── Add to Series ─────────────────────────────────────────────────
        this.addToSeriesLoading = ko.observable(false);
        this.addToSeriesOptions = ko.observableArray([]);
        this.addToSeriesSelected = ko.observable(null);
        this.addToSeriesCurrentMemberships = ko.observableArray([]);
        this.addToSeriesShowNewForm = ko.observable(false);
        this.addToSeriesNewName = ko.observable('');
        this.addToSeriesCreating = ko.observable(false);
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

    refreshFromSource = async () => {
        if (this.refreshing()) {
            return;
        }

        this.refreshing(true);
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/sync`, { method: 'POST' });
            let data = {};
            try {
                data = await response.json();
            } catch {
                data = {};
            }

            if (response.ok) {
                toast.info(data.message || 'Playlist sync queued successfully.');
            }
            else {
                toast.error(data.message || 'Error queueing playlist sync.');
            }
        } catch (error) {
            console.error('Error triggering playlist sync:', error);
            toast.error('Error queueing playlist sync. Please try again.');
        } finally {
            this.refreshing(false);
        }
    };

    _fetchPlaylist = async () => {
        const response = await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`);
        if (!response.ok) throw new Error('Playlist not found');
        this.playlist(await response.json());
    };

    initPlaylistTags = async () => {
        try {
            const tagsResponse = await fetch('/api/tags');
            const tagsData = await tagsResponse.json();
            const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

            const playlistTagsResponse = await fetch(`/api/playlists/${this.playlistId}/tags`);
            const playlistTagsData = await playlistTagsResponse.json();
            const currentTags = (playlistTagsData.tags || []).map(t => t.Name ?? t.name);

            const input = document.getElementById('playlistTagsInput');
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
            console.error('Error initialising playlist tags:', error);
        }
    };

    savePlaylistTags = async () => {
        if (!this._playlistTagifyInstance) return;
        const tagNames = this._playlistTagifyInstance.value.map(t => t.value);

        try {
            await fetch(`/api/playlists/${this.playlistId}/tags`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tagNames })
            });
        } catch (error) {
            console.error('Error saving playlist tags:', error);
        }
    };

    loadVideoTags = async (videoId) => {
        const input = document.getElementById('playlistPageVideoTagsInput');
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
            sources: [buildVideoStreamSource(v.id, v.streamContentType)],
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
            // Scroll the player into view without moving the page to the very top
            document.getElementById('videoPlayer')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
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

    openDownloadAllModal = () => {
        const downloadable = this.playlistVideos()
            .filter(v =>
                !v.downloadedAt && !v.isQueued && !v.downloadFailed &&
                !v.isHidden &&
                v.title !== PRIVATE_VIDEO_TITLE && v.title !== DELETED_VIDEO_TITLE
            )
            .map(v => ({ ...v, selectedForDownload: ko.observable(true) }));

        this.downloadAllModalVideos(downloadable);
        this.downloadAllSelectAll(true);
        bootstrap.Modal.getOrCreateInstance(document.getElementById('downloadAllModal')).show();
    };

    confirmDownloadAll = async () => {
        const toDownload = this.downloadAllModalVideos().filter(v => v.selectedForDownload());

        if (toDownload.length === 0) {
            toast.warning('No videos selected for download.');
            return;
        }

        this.downloadingAll(true);
        try {
            // Group by channelId and issue one request per channel
            const byChannel = toDownload.reduce((acc, v) => {
                if (!acc[v.channelId]) acc[v.channelId] = [];
                acc[v.channelId].push(v.id);
                return acc;
            }, {});

            await Promise.all(
                Object.entries(byChannel).map(([channelId, videoIds]) =>
                    fetch(`/api/channels/${channelId}/videos/download`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ videoIds })
                    })
                )
            );

            bootstrap.Modal.getInstance(document.getElementById('downloadAllModal')).hide();
            toast.info(`${toDownload.length} video(s) queued for download.`);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error queueing downloads:', error);
            toast.error('Error queueing downloads. Please try again.');
        } finally {
            this.downloadingAll(false);
        }
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
            // Let "Move to top" receive real clicks; Sortable otherwise can swallow pointer events on list rows.
            filter: '.mva-move-to-top',
            preventOnFilter: false,
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

    moveVideoToTop = async (video) => {
        if (!this.useCustomOrder() || !video) return;
        const vid = video.id ?? video.Id;
        if (vid == null) return;
        const arr = this.playlistVideos().slice();
        const idx = arr.findIndex(v => (v.id ?? v.Id) === vid);
        if (idx <= 0) return;
        arr.splice(idx, 1);
        arr.unshift(video);
        this.playlistVideos(arr);
        await this.saveCustomOrder();
        setTimeout(() => this.initializeSortable(), 50);
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

    // ── Add to Series ─────────────────────────────────────────────────────────

    openAddToSeries = async () => {
        const pl = this.playlist();
        if (!pl) return;

        this.addToSeriesLoading(true);
        this.addToSeriesShowNewForm(false);
        this.addToSeriesNewName('');
        this.addToSeriesOptions([]);
        this.addToSeriesCurrentMemberships([]);
        new bootstrap.Modal(document.getElementById('addToSeriesModal')).show();

        try {
            const [channelSeriesRes, memberRes] = await Promise.all([
                fetch(`/api/channels/${pl.ChannelId}/series`),
                fetch(`/api/playlists/${this.playlistId}/series`)
            ]);

            if (channelSeriesRes.ok) {
                const data = await channelSeriesRes.json();
                this.addToSeriesOptions(data.series || []);
            }
            if (memberRes.ok) {
                const data = await memberRes.json();
                this.addToSeriesCurrentMemberships((data.series || []).map(s => s.id));
            }
        } catch (error) {
            console.error('Error loading series:', error);
            toast.error('Failed to load series.');
        } finally {
            this.addToSeriesLoading(false);
        }
    };

    playlistIsInSeries = (seriesId) => {
        return this.addToSeriesCurrentMemberships().includes(seriesId);
    };

    addToExistingSeries = async (series) => {
        if (this.playlistIsInSeries(series.id)) return;
        try {
            const currentIds = (this.addToSeriesOptions()
                .find(s => s.id === series.id)?.playlists || [])
                .map(p => p.id);

            if (!currentIds.includes(this.playlistId)) {
                currentIds.push(this.playlistId);
            }

            const response = await fetch(`/api/series/${series.id}/playlists`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: currentIds })
            });

            if (response.ok) {
                this.addToSeriesCurrentMemberships([...this.addToSeriesCurrentMemberships(), series.id]);
                toast.success(`Added to "${series.name}".`);
            } else {
                const data = await response.json().catch(() => ({}));
                toast.error(data.message || 'Failed to add to series.');
            }
        } catch (error) {
            console.error('Error adding to series:', error);
            toast.error('Failed to add to series.');
        }
    };

    openNewSeriesInline = () => {
        this.addToSeriesShowNewForm(true);
        this.addToSeriesNewName('');
    };

    createSeriesAndAdd = async () => {
        const name = this.addToSeriesNewName().trim();
        if (!name) return;
        const pl = this.playlist();
        if (!pl) return;

        this.addToSeriesCreating(true);
        try {
            const createRes = await fetch(`/api/channels/${pl.ChannelId}/series`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name })
            });

            if (!createRes.ok) { toast.error('Failed to create series.'); return; }
            const data = await createRes.json();
            const newSeries = data.series;

            await fetch(`/api/series/${newSeries.id}/playlists`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: [this.playlistId] })
            });

            this.addToSeriesOptions([...this.addToSeriesOptions(), newSeries]);
            this.addToSeriesCurrentMemberships([...this.addToSeriesCurrentMemberships(), newSeries.id]);
            this.addToSeriesShowNewForm(false);
            toast.success(`Created series "${name}" and added this playlist.`);
        } catch (error) {
            console.error('Error creating series:', error);
            toast.error('Failed to create series.');
        } finally {
            this.addToSeriesCreating(false);
        }
    };
}

// ─── Bootstrap ────────────────────────────────────────────────────────────────
var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    setPlaylistVideoJsPlayerGetter(() => player);
    registerPlaylistButtons();

    viewModel = new PlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);

    // Initialise Video.js
    player = videojs('videoPlayer', mergeVideoJsPlayerOptions({
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: { skipButtons: { forward: 10, backward: 10 } },
        userActions: { hotkeys: true }
    }));

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
        bindPlaylistSubtitlePreferenceStorage(player);
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
        await viewModel.loadVideoTags(video.id);
        await viewModel.loadVideoExtras(video.id);

        // Scroll active sidebar item into view within the video list container only
        setTimeout(() => {
            const container = document.querySelector('.video-list');
            const activeItem = container?.querySelector('.playlist-video-item.active');
            if (activeItem && container) {
                const itemTop = activeItem.offsetTop;
                const itemHeight = activeItem.offsetHeight;
                const scrollTop = container.scrollTop;
                const containerHeight = container.clientHeight;

                // Only scroll if the item is outside the visible area of the container
                if (itemTop < scrollTop) {
                    container.scrollTo({ top: itemTop, behavior: 'smooth' });
                } else if (itemTop + itemHeight > scrollTop + containerHeight) {
                    container.scrollTo({ top: itemTop + itemHeight - containerHeight, behavior: 'smooth' });
                }
            }
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
        void loadAndAttachSubtitleTracksForPlaylist(player, _currentVideoId);
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
    await viewModel.initPlaylistTags();
});
