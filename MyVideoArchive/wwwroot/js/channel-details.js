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
        this.pageSize = 20;
        this.totalPages = ko.observable(1);
        this.totalCount = ko.observable(0);
        this.showIgnored = ko.observable(false);
        this.selectAll = ko.observable(false);

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
                const idParams = new URLSearchParams();
                videos.forEach(v => idParams.append('videoIds', v.id));
                await fetch(`/api/user/videos/watched?${idParams}`)
                    .then(r => r.json())
                    .then(watchedData => {
                        const watchedSet = new Set(watchedData.watchedIds || []);
                        this.videos().forEach(v => v.watched(watchedSet.has(v.id)));
                    })
                    .catch(() => {});
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

    setVideoViewMode = (mode) => {
        this.videoViewMode(mode);
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

    deleteVideoFile = async (video) => {
        if (!confirm(`Delete the file for "${video.Title}"? This cannot be undone. The video will be marked as ignored.`)) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/${video.Id}/file`, {
                method: 'DELETE'
            });

            const data = await response.json();

            if (response.ok) {
                this.videos.remove(video);
                this.videosTotalCount(this.videosTotalCount() - 1);
            } else {
                alert(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file:', error);
            alert('Error deleting video file. Please try again.');
        }
    };

    // ── Available videos tab ──────────────────────────────────────────────────

    loadAvailableVideos = async () => {
        this.availableLoading(true);
        try {
            const response = await fetch(
                `/api/channels/${this.channelId}/videos/available?page=${this.currentPage()}&pageSize=${this.pageSize}&showIgnored=${this.showIgnored()}`
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
            alert('Please select at least one video to download.');
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
            alert(data.message);
        } catch (error) {
            console.error('Error downloading videos:', error);
            alert('Error queueing downloads. Please try again.');
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
            alert(data.message);
        } catch (error) {
            console.error('Error downloading all videos:', error);
            alert('Error queueing downloads. Please try again.');
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
            alert('Error updating video status. Please try again.');
        }
    };

    toggleShowIgnored = async () => {
        this.currentPage(1);
        await this.loadAvailableVideos();
        return true;
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
            alert('Please select at least one playlist to subscribe.');
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/playlists/subscribe`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: selectedIds })
            });
            const data = await response.json();
            alert(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error subscribing to playlists:', error);
            alert('Error subscribing to playlists. Please try again.');
        }
    };

    subscribeAllPlaylists = async () => {
        if (!confirm('Are you sure you want to subscribe to all playlists for this channel?')) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/playlists/subscribe-all`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            const data = await response.json();
            alert(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error subscribing to all playlists:', error);
            alert('Error subscribing to playlists. Please try again.');
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
            alert('Error updating playlist status. Please try again.');
        }
    };

    toggleShowIgnoredPlaylists = async () => {
        this.playlistsCurrentPage(1);
        await this.loadPlaylists();
        return true;
    };

    refreshPlaylists = async () => {
        if (!confirm('This will fetch the latest playlists from YouTube. Continue?')) {
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
            alert(data.message);
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error refreshing playlists:', error);
            this.refreshingPlaylists(false);
            alert('Error refreshing playlists from YouTube. Please try again.');
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new ChannelDetailsViewModel(channelId);
    ko.applyBindings(viewModel);
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

    // Allow pressing Enter in the search box
    document.getElementById('videosSearchInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.searchVideos();
    });
});
