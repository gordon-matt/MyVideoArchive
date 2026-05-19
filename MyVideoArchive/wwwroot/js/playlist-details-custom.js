import { formatDate, formatDuration, encodeArchiveUrlForHtml } from './utils.js';
import {
    setPlaylistVideoJsPlayerGetter,
    STORAGE_KEY_RATE,
    STORAGE_KEY_AUTO_ADVANCE,
    savePosition,
    loadSavedPosition,
    clearPosition,
    registerPlaylistButtons,
    applyPlaylistDefaultOrder,
    loadAndAttachSubtitleTracksForPlaylist,
    bindPlaylistSubtitlePreferenceStorage
} from './playlist-details-shared.js';
import { initVideoExtras, loadVideoExtras } from './video-details-shared.js';
import { buildVideoStreamSource, mergeVideoJsPlayerOptions } from './video-player.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

/** Filtered subset: downloaded + not hidden, synced with the Video.js playlist. */
let _availableVideos = [];

// ─── ViewModel ────────────────────────────────────────────────────────────────
class CustomPlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.channelPlaylistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.useCustomOrder = ko.observable(false);
        this.isAdmin = window.isAdmin === true;
        this.applyingDefaultOrder = ko.observable(false);
        this.showHidden = ko.observable(false);
        this.autoAdvance = ko.observable(
            localStorage.getItem(STORAGE_KEY_AUTO_ADVANCE) !== 'false'
        );
        this.sortableInstance = null;

        this.visibleVideoCount = ko.computed(() =>
            this.playlistVideos().filter(v => !v.isHidden).length
        );

        this.editPlaylistName = ko.observable('');
        this.editPlaylistDescription = ko.observable('');

        this.autoAdvance.subscribe(val => {
            localStorage.setItem(STORAGE_KEY_AUTO_ADVANCE, val);
            if (player?.playlist) {
                player.playlist.autoadvance(val ? 0 : null);
            }
        });

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;

        initVideoExtras(this);
        this.loadVideoExtras = (videoId) => loadVideoExtras(this, videoId);

        // ── Add to Series ─────────────────────────────────────────────────
        this.addToSeriesLoading = ko.observable(false);
        this.addToSeriesOptions = ko.observableArray([]);
        this.addToSeriesCurrentMemberships = ko.observableArray([]);
        this.addToSeriesShowNewForm = ko.observable(false);
        this.addToSeriesNewName = ko.observable('');
        this.addToSeriesCreating = ko.observable(false);

        // ── Inline metadata editing ───────────────────────────────────────────
        this.showMetadataEdit = ko.observable(false);
        this.editVideoTitle = ko.observable('');
        this.editVideoUploadDate = ko.observable('');
        this.editVideoDescription = ko.observable('');
        this.savingVideoMetadata = ko.observable(false);

        this.currentVideo.subscribe(() => this.showMetadataEdit(false));
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
        const pl = await response.json();
        const ch = pl.Channel ?? pl.channel;
        const platform = ch?.Platform ?? ch?.platform;
        let thumb = pl.ThumbnailUrl ?? pl.thumbnailUrl;
        if (!thumb && platform === 'Custom') {
            try {
                const fr = await fetch(`/api/custom/playlists/${this.playlistId}/display-thumbnail`, { credentials: 'same-origin' });
                if (fr.ok) {
                    const d = await fr.json();
                    const u = d.thumbnailUrl ?? d.ThumbnailUrl;
                    if (u) {
                        thumb = encodeArchiveUrlForHtml(u);
                        pl.ThumbnailUrl = pl.thumbnailUrl = thumb;
                    }
                }
            } catch {
                /* ignore */
            }
        }
        this.playlist(pl);
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

        const prevVideoId = this.currentVideo()?.id;
        const prevTime = player ? Math.floor(player.currentTime()) : 0;

        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/videos?useCustomOrder=${this.useCustomOrder()}&showHidden=${this.showHidden()}`);
            const data = await response.json();
            const videos = (data.videos || []).map(v => {
                const raw = v.thumbnailUrl ?? v.ThumbnailUrl;
                const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                return { ...v, thumbnailUrl: thumb, ThumbnailUrl: thumb };
            });
            this.playlistVideos(videos);

            setTimeout(() => this.initializeSortable(), 100);
            this._syncPlayerPlaylist(prevVideoId, prevTime);
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loadingVideos(false);
        }
    };

    _syncPlayerPlaylist = (prevVideoId, prevTime) => {
        if (!player?.playlist) return;

        _availableVideos = this.playlistVideos().filter(v => v.downloadedAt && !v.isHidden);

        const items = _availableVideos.map(v => ({
            sources: [buildVideoStreamSource(v.id, v.streamContentType)],
            poster: v.thumbnailUrl || undefined,
            name: v.title
        }));

        player.playlist(items);
        player.playlist.autoadvance(this.autoAdvance() ? 0 : null);

        if (_availableVideos.length === 0) return;

        if (prevVideoId) {
            const idx = _availableVideos.findIndex(v => v.id === prevVideoId);
            if (idx > 0) {
                if (prevTime > 5) localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
                player.playlist.currentItem(idx);
            } else if (idx === 0 && prevTime > 5) {
                localStorage.setItem(`mva-pos-${prevVideoId}`, prevTime);
            }
        }
    };

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
        } catch { await this.saveCustomOrder(true); }
    };

    clearCustomOrder = async () => { await this.saveCustomOrder(true); };

    initializeSortable = () => {
        const container = document.getElementById('videoListContainer');
        if (!container) return;
        if (this.sortableInstance) this.sortableInstance.destroy();

        this.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
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

    saveCustomOrder = async (reloadAfterSave = false) => {
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

    applyAsDefault = async () => {
        if (!this.isAdmin || !this.useCustomOrder() || this.applyingDefaultOrder()) {
            return;
        }

        const message =
            'Set the current order as the default "Original Order" for all users? ' +
            'Users can still use their own custom order.';
        if (!confirm(message)) {
            return;
        }

        this.applyingDefaultOrder(true);
        try {
            await applyPlaylistDefaultOrder(this.playlistId, this.playlistVideos());
            toast.success('Default order updated for all users.');
        } catch (error) {
            console.error('Error applying default order:', error);
            toast.error(error.message || 'Error applying default order.');
        } finally {
            this.applyingDefaultOrder(false);
        }
    };

    openEditPlaylist = () => {
        const pl = this.playlist();
        if (!pl) return;
        this.editPlaylistName(pl.Name || '');
        this.editPlaylistDescription(pl.Description || '');
        new bootstrap.Modal(document.getElementById('editPlaylistModal')).show();
    };

    savePlaylist = async () => {
        try {
            const response = await fetch(`/api/custom/playlists/${this.playlistId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.editPlaylistName(),
                    description: this.editPlaylistDescription() || null
                })
            });
            if (response.ok) {
                bootstrap.Modal.getInstance(document.getElementById('editPlaylistModal')).hide();
                await this._fetchPlaylist();
            } else {
                toast.error('Failed to update playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error saving playlist:', error);
        }
    };

    // ── Extras (files linked to the current video) ───────────────────────────
    // ── Inline metadata editing ────────────────────────────────────────────────

    openVideoMetadataEdit = () => {
        const video = this.currentVideo();
        if (!video) return;
        this.editVideoTitle(video.title || '');
        this.editVideoDescription(video.description || '');
        if (video.uploadDate) {
            const d = new Date(video.uploadDate);
            this.editVideoUploadDate(d.toISOString().split('T')[0]);
        } else {
            this.editVideoUploadDate('');
        }
        this.showMetadataEdit(true);
    };

    cancelVideoMetadataEdit = () => this.showMetadataEdit(false);

    saveVideoMetadata = async () => {
        const video = this.currentVideo();
        if (!video?.id) return;
        this.savingVideoMetadata(true);
        try {
            const response = await fetch(`/api/custom/videos/${video.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title: this.editVideoTitle().trim(),
                    description: this.editVideoDescription() || null,
                    thumbnailUrl: video.thumbnailUrl ?? null,
                    uploadDate: this.editVideoUploadDate()
                        ? new Date(this.editVideoUploadDate()).toISOString()
                        : null,
                    duration: video.duration || null,
                    filePath: video.filePath || null,
                    playlistIds: null
                })
            });
            if (response.ok) {
                const updated = {
                    ...video,
                    title: this.editVideoTitle().trim(),
                    description: this.editVideoDescription() || null,
                    uploadDate: this.editVideoUploadDate()
                        ? new Date(this.editVideoUploadDate()).toISOString()
                        : video.uploadDate
                };
                this.currentVideo(updated);
                const idx = this.playlistVideos().findIndex(v => v.id === video.id);
                if (idx >= 0) {
                    const vids = [...this.playlistVideos()];
                    vids[idx] = updated;
                    this.playlistVideos(vids);
                }
                this.showMetadataEdit(false);
                toast.success('Metadata saved.');
            } else {
                toast.error('Failed to save metadata. Please try again.');
            }
        } catch (error) {
            console.error('Error saving video metadata:', error);
            toast.error('Failed to save metadata.');
        } finally {
            this.savingVideoMetadata(false);
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
            const currentIds = (series.playlists || []).map(p => p.id);
            if (!currentIds.includes(this.playlistId)) currentIds.push(this.playlistId);

            const response = await fetch(`/api/series/${series.id}/playlists`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: currentIds })
            });

            if (response.ok) {
                this.addToSeriesCurrentMemberships([...this.addToSeriesCurrentMemberships(), series.id]);
                toast.success(`Added to "${series.name}".`);
            } else {
                toast.error('Failed to add to series.');
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

    player.on('playlistitem', () => {
        const idx = player.playlist.currentItem();
        const video = _availableVideos[idx];
        if (!video) return;

        _currentVideoId = video.id;
        viewModel.currentVideo(video);
        viewModel.loadVideoExtras(video.id);

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
});
