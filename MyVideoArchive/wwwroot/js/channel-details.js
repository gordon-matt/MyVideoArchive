import { formatDate, formatDuration } from './utils.js';

class ChannelDetailsViewModel {
    constructor(channelId) {
        this.channelId = channelId;
        this.channel = ko.observable(null);

        // ── Videos tab ───────────────────────────────────────────────────────
        this.videos = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.videosCurrentPage = ko.observable(1);
        this.videosPageSize = 24;
        this.videosTotalPages = ko.observable(1);
        this.videosTotalCount = ko.observable(0);
        this.videosSearch = ko.observable('');
        this.videosSearchInput = ko.observable(''); // bound to the input box
        // '' = All, -1 = None, N = playlist id
        this.videosPlaylistFilter = ko.observable('');
        this.videoViewMode = ko.observable('list'); // 'list' | 'grid'
        this.subscribedPlaylists = ko.observableArray([]); // for dropdown

        // ── Available videos tab ──────────────────────────────────────────────
        this.availableVideos = ko.observableArray([]);
        this.availableLoading = ko.observable(false);
        this.currentPage = ko.observable(1);
        this.pageSize = 24;
        this.totalPages = ko.observable(1);
        this.totalCount = ko.observable(0);
        this.showIgnored = ko.observable(false);
        this.selectAll = ko.observable(false);
        this.availableViewMode = ko.observable('list'); // 'list' | 'grid'
        this.availableVideosSearch = ko.observable('');
        this.availableVideosSearchInput = ko.observable('');

        // ── File system scan ──────────────────────────────────────────────────
        this.scanningFiles = ko.observable(false);
        this.scanMessage = ko.observable(null);
        this._scanPollTimer = null;

        // ── Subscribers tab (admin only) ──────────────────────────────────────
        this.subscribers = ko.observableArray([]);
        this.subscribersLoading = ko.observable(false);
        this.subscribersCount = ko.computed(() => this.subscribers().length);

        // ── Playlists tab ─────────────────────────────────────────────────────
        this.playlists = ko.observableArray([]);
        this.playlistsLoading = ko.observable(false);
        this.refreshingPlaylists = ko.observable(false);
        this.showIgnoredPlaylists = ko.observable(false);
        this.selectAllPlaylists = ko.observable(false);
        this.playlistsCurrentPage = ko.observable(1);
        this.playlistsPageSize = 24;
        this.playlistsTotalPages = ko.observable(1);
        this.playlistsTotalCount = ko.observable(0);
        this.subscribingPlaylists = ko.observable(false);

        // ── Checkbox sync ─────────────────────────────────────────────────────
        this.selectAll.subscribe((newValue) => {
            this.availableVideos().forEach(v => v.selected(newValue));
        });

        this.selectAllPlaylists.subscribe((newValue) => {
            this.playlists().forEach(p => p.selected(newValue));
        });

        // ── React to playlist filter changes on the Videos tab ───────────────
        this.videosPlaylistFilter.subscribe(async () => {
            this.videosCurrentPage(1);
            await this.loadVideos();
        });

        // ── React to "show ignored" checkbox changes (no click handler needed) ─
        this.showIgnored.subscribe(() => {
            this.currentPage(1);
            this.loadAvailableVideos();
        });

        this.showIgnoredPlaylists.subscribe(() => {
            this.playlistsCurrentPage(1);
            this.loadPlaylists();
        });

        // ── Computed page numbers ─────────────────────────────────────────────
        this.videosPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.videosCurrentPage(), this.videosTotalPages()));

        this.pageNumbers = ko.computed(() => this._buildPageNumbers(
            this.currentPage(), this.totalPages()));

        this.playlistsPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.playlistsCurrentPage(), this.playlistsTotalPages()));

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
    }

    _buildPageNumbers(current, total) {
        const pages = [];
        let start = Math.max(1, current - 2);
        let end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);
        for (let i = start; i <= end; i++) pages.push(i);
        return pages;
    }

    // ── Channel ───────────────────────────────────────────────────────────────

    loadChannel = async () => {
        try {
            const response = await fetch(`/odata/ChannelOData(${this.channelId})`);
            if (response.ok) this.channel(await response.json());
        } catch (error) {
            console.error('Error loading channel:', error);
        }
    };

    // ── Videos tab ───────────────────────────────────────────────────────────

    loadVideos = async () => {
        this.loading(true);
        const params = new URLSearchParams({
            page: this.videosCurrentPage(),
            pageSize: this.videosPageSize
        });

        if (this.videosSearch()) params.set('search', this.videosSearch());

        const pf = this.videosPlaylistFilter();
        if (pf !== null && pf !== '') params.set('playlistId', pf);

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/downloaded?${params}`);
            const data = await response.json();

            const videos = (data.videos || []).map(v => {
                v.watched = ko.observable(false);
                return v;
            });

            this.videos(videos);
            this.videosTotalPages(data.pagination.totalPages);
            this.videosTotalCount(data.pagination.totalCount);

            if (videos.length > 0) {
                await fetch(`/api/user/videos/watched/by-channel/${this.channelId}`)
                    .then(r => r.json())
                    .then(watchedData => {
                        const watchedSet = new Set(watchedData.watchedIds || []);
                        this.videos().forEach(v => v.watched(watchedSet.has(v.id)));
                    })
                    .catch(() => { });
            }
        } catch (error) {
            console.error('Error loading downloaded videos:', error);
        } finally {
            this.loading(false);
        }
    };

    loadSubscribedPlaylists = async () => {
        try {
            const url = `/odata/PlaylistOData?$filter=ChannelId eq ${this.channelId} and SubscribedAt ne null&$select=Id,Name&$orderby=Name`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.subscribedPlaylists(data.value || []);
            }
        } catch (error) {
            console.error('Error loading subscribed playlists:', error);
        }
    };

    searchVideos = async () => {
        this.videosSearch(this.videosSearchInput());
        this.videosCurrentPage(1);
        await this.loadVideos();
    };

    clearVideoSearch = async () => {
        this.videosSearchInput('');
        this.videosSearch('');
        this.videosCurrentPage(1);
        await this.loadVideos();
    };

    setVideoViewMode = async (mode) => {
        this.videoViewMode(mode);
        await this._saveViewModeSettings();
    };

    // Videos pagination
    videosGoToPage = async (page) => {
        if (page >= 1 && page <= this.videosTotalPages()) {
            this.videosCurrentPage(page);
            await this.loadVideos();
        }
    };

    videosPreviousPage = async () => {
        if (this.videosCurrentPage() > 1) {
            this.videosCurrentPage(this.videosCurrentPage() - 1);
            await this.loadVideos();
        }
    };

    videosNextPage = async () => {
        if (this.videosCurrentPage() < this.videosTotalPages()) {
            this.videosCurrentPage(this.videosCurrentPage() + 1);
            await this.loadVideos();
        }
    };

    // ── File system scan ──────────────────────────────────────────────────────

    scanFiles = async () => {
        this.scanningFiles(true);
        this.scanMessage(null);

        try {
            const response = await fetch(`/api/admin/channels/${this.channelId}/scan-filesystem`, { method: 'POST' });

            if (response.status === 409) {
                this.scanMessage('A scan is already in progress on another channel or the admin page.');
                this.scanningFiles(false);
                return;
            }

            if (response.status === 403 || response.status === 401) {
                this.scanMessage('You do not have permission to run a file system scan.');
                this.scanningFiles(false);
                return;
            }

            if (!response.ok) {
                this.scanMessage('Failed to start scan.');
                this.scanningFiles(false);
                return;
            }

            // 202 Accepted - scan started in background; poll for completion
            this._startScanPolling();
        } catch (error) {
            console.error('Error starting file system scan:', error);
            this.scanMessage('An unexpected error occurred while starting the scan.');
            this.scanningFiles(false);
        }
    };

    cancelScan = async () => {
        try {
            await fetch('/api/admin/scan-filesystem/cancel', { method: 'POST' });
            this.scanMessage('Cancellation requested.');
        } catch (error) {
            console.error('Error cancelling scan:', error);
        }
    };

    _startScanPolling = () => {
        this._stopScanPolling();
        this._scanPollTimer = setInterval(() => this._pollScanStatus(), 1000);
    };

    _stopScanPolling = () => {
        if (this._scanPollTimer !== null) {
            clearInterval(this._scanPollTimer);
            this._scanPollTimer = null;
        }
    };

    _pollScanStatus = async () => {
        try {
            const response = await fetch('/api/admin/scan-filesystem/status');
            if (!response.ok) return;

            const data = await response.json();

            if (!data.isRunning) {
                this._stopScanPolling();
                this.scanningFiles(false);

                if (data.lastResult) {
                    const r = data.lastResult;
                    this.scanMessage(
                        `Scan complete: ${r.newVideos} new, ${r.updatedVideos} updated, ${r.missingFiles} missing from disk.`
                    );
                    // Reload videos to reflect any path changes
                    await this.loadVideos();
                } else if (data.errorMessage) {
                    this.scanMessage(`Scan error: ${data.errorMessage}`);
                }
            }
        } catch (error) {
            console.error('Error polling scan status:', error);
        }
    };

    deleteVideoFile = async (video) => {
        if (!confirm(`Delete the file for "${video.Title}"? This cannot be undone. The video will be marked as ignored.`)) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/${video.id}/file`, {
                method: 'DELETE'
            });

            const data = await response.json();

            if (response.ok) {
                this.videos.remove(video);
                this.videosTotalCount(this.videosTotalCount() - 1);
            } else {
                toast.error(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file:', error);
            toast.error('Error deleting video file. Please try again.');
        }
    };

    // ── Available videos tab ──────────────────────────────────────────────────

    loadAvailableVideos = async () => {
        this.availableLoading(true);
        try {
            const params = new URLSearchParams({
                page: this.currentPage(),
                pageSize: this.pageSize,
                showIgnored: this.showIgnored()
            });
            if (this.availableVideosSearch()) params.set('search', this.availableVideosSearch());
            const response = await fetch(
                `/api/channels/${this.channelId}/videos/available?${params}`
            );
            const data = await response.json();
            const videos = data.videos.map(v => {
                v.selected = ko.observable(false);
                return v;
            });
            this.availableVideos(videos);
            this.totalPages(data.pagination.totalPages);
            this.totalCount(data.pagination.totalCount);
            this.selectAll(false);
        } catch (error) {
            console.error('Error loading available videos:', error);
        } finally {
            this.availableLoading(false);
        }
    };

    goToPage = async (page) => {
        if (page >= 1 && page <= this.totalPages()) {
            this.currentPage(page);
            await this.loadAvailableVideos();
        }
    };

    previousPage = async () => {
        if (this.currentPage() > 1) {
            this.currentPage(this.currentPage() - 1);
            await this.loadAvailableVideos();
        }
    };

    nextPage = async () => {
        if (this.currentPage() < this.totalPages()) {
            this.currentPage(this.currentPage() + 1);
            await this.loadAvailableVideos();
        }
    };

    downloadSelected = async () => {
        const selectedVideos = this.availableVideos().filter(v => v.selected());
        const selectedIds = selectedVideos.map(v => v.id);

        if (selectedIds.length === 0) {
            toast.warning('Please select at least one video to download.');
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/download`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoIds: selectedIds })
            });
            const data = await response.json();
            selectedVideos.forEach(video => this.availableVideos.remove(video));
            this.selectAll(false);
            toast.success(data.message);
        } catch (error) {
            console.error('Error downloading videos:', error);
            toast.error('Error queueing downloads. Please try again.');
        }
    };

    downloadAll = async () => {
        if (!confirm('Are you sure you want to download all available videos for this channel?')) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/download-all`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            const data = await response.json();
            this.availableVideos([]);
            toast.success(data.message);
        } catch (error) {
            console.error('Error downloading all videos:', error);
            toast.error('Error queueing downloads. Please try again.');
        }
    };

    ignoreVideo = async (video) => {
        try {
            await fetch(`/api/channels/${this.channelId}/videos/${video.id}/ignore`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isIgnored: !video.isIgnored })
            });
            await this.loadAvailableVideos();
        } catch (error) {
            console.error('Error ignoring video:', error);
            toast.error('Error updating video status. Please try again.');
        }
    };

    searchAvailableVideos = async () => {
        this.availableVideosSearch(this.availableVideosSearchInput());
        this.currentPage(1);
        await this.loadAvailableVideos();
    };

    clearAvailableSearch = async () => {
        this.availableVideosSearchInput('');
        this.availableVideosSearch('');
        this.currentPage(1);
        await this.loadAvailableVideos();
    };

    setAvailableViewMode = async (mode) => {
        this.availableViewMode(mode);
        await this._saveViewModeSettings();
    };

    loadUserSettings = async () => {
        try {
            const response = await fetch('/api/user/settings');
            if (response.ok) {
                const settings = await response.json();
                this.videoViewMode(settings.videosTabViewMode || 'list');
                this.availableViewMode(settings.availableTabViewMode || 'list');
            }
        } catch (error) {
            console.error('Error loading user settings:', error);
        }
    };

    _saveViewModeSettings = async () => {
        try {
            await fetch('/api/user/settings', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    videosTabViewMode: this.videoViewMode(),
                    availableTabViewMode: this.availableViewMode()
                })
            });
        } catch (error) {
            console.error('Error saving user settings:', error);
        }
    };

    // ── Subscribers tab ───────────────────────────────────────────────────────

    loadSubscribers = async () => {
        this.subscribersLoading(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/subscribers`);
            if (response.ok) {
                const data = await response.json();
                this.subscribers(data.subscribers || []);
            }
        } catch (error) {
            console.error('Error loading subscribers:', error);
        } finally {
            this.subscribersLoading(false);
        }
    };

    // ── Playlists tab ─────────────────────────────────────────────────────────

    loadPlaylists = async () => {
        this.playlistsLoading(true);
        try {
            const response = await fetch(
                `/api/channels/${this.channelId}/playlists/available?showIgnored=${this.showIgnoredPlaylists()}&page=${this.playlistsCurrentPage()}&pageSize=${this.playlistsPageSize}`
            );
            const data = await response.json();
            const playlists = data.playlists.map(p => {
                p.selected = ko.observable(false);
                return p;
            });
            this.playlists(playlists);
            this.playlistsTotalPages(data.pagination.totalPages);
            this.playlistsTotalCount(data.pagination.totalCount);
            this.selectAllPlaylists(false);
        } catch (error) {
            console.error('Error loading playlists:', error);
        } finally {
            this.playlistsLoading(false);
        }
    };

    playlistsGoToPage = async (page) => {
        if (page >= 1 && page <= this.playlistsTotalPages()) {
            this.playlistsCurrentPage(page);
            await this.loadPlaylists();
        }
    };

    playlistsPreviousPage = async () => {
        if (this.playlistsCurrentPage() > 1) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() - 1);
            await this.loadPlaylists();
        }
    };

    playlistsNextPage = async () => {
        if (this.playlistsCurrentPage() < this.playlistsTotalPages()) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() + 1);
            await this.loadPlaylists();
        }
    };

    subscribeSelectedPlaylists = async () => {
        const selectedIds = this.playlists().filter(p => p.selected()).map(p => p.id);

        if (selectedIds.length === 0) {
            toast.warning('Please select at least one playlist to subscribe.');
            return;
        }

        this.subscribingPlaylists(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/playlists/subscribe`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: selectedIds })
            });
            const data = await response.json();
            toast.success(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error subscribing to playlists:', error);
            toast.error('Error subscribing to playlists. Please try again.');
        } finally {
            this.subscribingPlaylists(false);
        }
    };

    subscribeAllPlaylists = async () => {
        if (!confirm('Are you sure you want to subscribe to all playlists for this channel?')) {
            return;
        }

        this.subscribingPlaylists(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/playlists/subscribe-all`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            const data = await response.json();
            toast.success(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error subscribing to all playlists:', error);
            toast.error('Error subscribing to playlists. Please try again.');
        } finally {
            this.subscribingPlaylists(false);
        }
    };

    ignorePlaylist = async (playlist) => {
        try {
            await fetch(`/api/channels/${this.channelId}/playlists/${playlist.id}/ignore`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isIgnored: !playlist.isIgnored })
            });
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error ignoring playlist:', error);
            toast.error('Error updating playlist status. Please try again.');
        }
    };

    refreshPlaylists = async () => {
        const platformName = this.channel()?.Platform ?? 'the platform';
        if (!confirm(`This will fetch the latest playlists from ${platformName}. Continue?`)) {
            return;
        }

        this.refreshingPlaylists(true);

        try {
            const response = await fetch(`/api/channels/${this.channelId}/playlists/refresh`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            const data = await response.json();
            this.refreshingPlaylists(false);
            toast.success(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error refreshing playlists:', error);
            this.refreshingPlaylists(false);
            toast.error('Error refreshing playlists from the platform. Please try again.');
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new ChannelDetailsViewModel(channelId);
    ko.applyBindings(viewModel);
    await viewModel.loadUserSettings();
    await viewModel.loadChannel();
    await viewModel.loadVideos();
    await viewModel.loadSubscribedPlaylists();

    // Load available videos when the tab is clicked
    $('#available-tab').on('shown.bs.tab', function () {
        if (viewModel.availableVideos().length === 0) {
            viewModel.loadAvailableVideos();
        }
    });

    // Load playlists when the tab is clicked
    $('#playlists-tab').on('shown.bs.tab', function () {
        if (viewModel.playlists().length === 0) {
            viewModel.loadPlaylists();
        }
    });

    // Load subscribers when the tab is clicked (admin only)
    if (isAdmin) {
        $('#subscribers-tab').on('shown.bs.tab', function () {
            if (viewModel.subscribers().length === 0) {
                viewModel.loadSubscribers();
            }
        });
    }

    // Allow pressing Enter in the search boxes
    document.getElementById('videosSearchInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.searchVideos();
    });
    document.getElementById('availableVideosSearchInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.searchAvailableVideos();
    });
});
