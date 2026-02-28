import { formatDate } from './utils.js';

class CustomChannelViewModel {
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
        this.videosSearchInput = ko.observable('');
        this.videoViewMode = ko.observable('list'); // 'list' | 'grid'

        // ── Playlists tab ─────────────────────────────────────────────────────
        this.playlists = ko.observableArray([]);
        this.playlistsLoading = ko.observable(false);
        this.playlistsCurrentPage = ko.observable(1);
        this.playlistsPageSize = 24;
        this.playlistsTotalPages = ko.observable(1);
        this.playlistsTotalCount = ko.observable(0);

        // Edit channel form
        this.editName = ko.observable('');
        this.editDescription = ko.observable('');
        this.editThumbnailUrl = ko.observable('');

        // Channel thumbnail upload (in edit modal)
        this.channelThumbnailPreviewUrl = ko.observable(null);
        this.channelThumbnailFile = ko.observable(null);

        // Create playlist form
        this.newPlaylistName = ko.observable('');
        this.newPlaylistDescription = ko.observable('');

        // Playlist thumbnail upload state
        this.thumbnailTargetPlaylist = ko.observable(null);
        this.thumbnailPreviewUrl = ko.observable(null);
        this.thumbnailFile = ko.observable(null);
        this.uploadingThumbnail = ko.observable(false);
        this.thumbnailCacheBust = ko.observable(Date.now());

        // ── Computed page numbers ─────────────────────────────────────────────
        this.videosPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.videosCurrentPage(), this.videosTotalPages()));

        this.playlistsPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.playlistsCurrentPage(), this.playlistsTotalPages()));

        this.formatDate = formatDate;
    }

    _buildPageNumbers(current, total) {
        const pages = [];
        let start = Math.max(1, current - 2);
        let end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);
        for (let i = start; i <= end; i++) pages.push(i);
        return pages;
    }

    load = async () => {
        await Promise.all([
            this.loadChannel(),
            this.loadVideos(),
            this.loadPlaylists()
        ]);
    };

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
        try {
            const skip = (this.videosCurrentPage() - 1) * this.videosPageSize;
            let filter = `ChannelId eq ${this.channelId}`;
            const search = this.videosSearch().trim();
            if (search) {
                const escaped = search.replace(/'/g, "''");
                filter += ` and contains(tolower(Title), '${escaped.toLowerCase()}')`;
            }

            const url = `/odata/VideoOData?$filter=${encodeURIComponent(filter)}&$orderby=UploadDate desc&$top=${this.videosPageSize}&$skip=${skip}&$count=true&$select=Id,Title,ThumbnailUrl,UploadDate,DownloadedAt,NeedsMetadataReview`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.videos(data.value || []);
                const total = data['@odata.count'] ?? 0;
                this.videosTotalCount(total);
                this.videosTotalPages(Math.max(1, Math.ceil(total / this.videosPageSize)));
            }
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loading(false);
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

    // ── Playlists tab ─────────────────────────────────────────────────────────

    loadPlaylists = async () => {
        this.playlistsLoading(true);
        try {
            const skip = (this.playlistsCurrentPage() - 1) * this.playlistsPageSize;
            const url = `/odata/PlaylistOData?$filter=ChannelId eq ${this.channelId}&$orderby=Name&$top=${this.playlistsPageSize}&$skip=${skip}&$count=true`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.playlists(data.value || []);
                const total = data['@odata.count'] ?? 0;
                this.playlistsTotalCount(total);
                this.playlistsTotalPages(Math.max(1, Math.ceil(total / this.playlistsPageSize)));
            }
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

    // ── Edit channel ──────────────────────────────────────────────────────────

    openEditChannel = () => {
        const ch = this.channel();
        if (!ch) return;
        this.editName(ch.Name || '');
        this.editDescription(ch.Description || '');
        this.editThumbnailUrl(ch.ThumbnailUrl || '');
        this.channelThumbnailPreviewUrl(ch.ThumbnailUrl ? ch.ThumbnailUrl + '?t=' + Date.now() : null);
        this.channelThumbnailFile(null);
        new bootstrap.Modal(document.getElementById('editChannelModal')).show();
    };

    saveChannel = async () => {
        try {
            let thumbnailUrl = this.editThumbnailUrl() || null;
            const file = this.channelThumbnailFile();
            if (file) {
                const form = new FormData();
                form.append('file', file);
                const uploadRes = await fetch(`/api/custom/channels/${this.channelId}/thumbnail`, {
                    method: 'POST',
                    body: form
                });
                if (!uploadRes.ok) {
                    alert('Failed to upload thumbnail. Please try again.');
                    return;
                }
                const data = await uploadRes.json();
                thumbnailUrl = data.thumbnailUrl;
            }

            const response = await fetch(`/api/custom/channels/${this.channelId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.editName(),
                    description: this.editDescription() || null,
                    thumbnailUrl
                })
            });
            if (response.ok) {
                bootstrap.Modal.getInstance(document.getElementById('editChannelModal')).hide();
                await this.loadChannel();
            } else {
                alert('Failed to update channel. Please try again.');
            }
        } catch (error) {
            console.error('Error saving channel:', error);
            alert('Failed to update channel.');
        }
    };

    // Channel thumbnail upload (in edit modal)
    onChannelThumbnailDragOver = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        return true;
    };

    onChannelThumbnailDrop = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setChannelThumbnailFile(file);
        return true;
    };

    triggerChannelThumbnailInput = () => {
        document.getElementById('channelThumbnailInput').click();
    };

    onChannelThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setChannelThumbnailFile(file);
        event.target.value = '';
    };

    _setChannelThumbnailFile = (file) => {
        this.channelThumbnailFile(file);
        const reader = new FileReader();
        reader.onload = (e) => this.channelThumbnailPreviewUrl(e.target.result);
        reader.readAsDataURL(file);
    };

    // ── Create playlist ───────────────────────────────────────────────────────

    openCreatePlaylist = () => {
        this.newPlaylistName('');
        this.newPlaylistDescription('');
        new bootstrap.Modal(document.getElementById('createPlaylistModal')).show();
    };

    createPlaylist = async () => {
        try {
            const response = await fetch(`/api/custom/channels/${this.channelId}/playlists`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.newPlaylistName(),
                    description: this.newPlaylistDescription() || null
                })
            });
            if (response.ok) {
                const data = await response.json();
                bootstrap.Modal.getInstance(document.getElementById('createPlaylistModal')).hide();
                window.location.href = `/playlists/${data.id}`;
            } else {
                alert('Failed to create playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error creating playlist:', error);
            alert('Failed to create playlist.');
        }
    };

    // ── Delete playlist ───────────────────────────────────────────────────────

    deletePlaylist = async (playlist) => {
        if (!confirm(`Delete playlist "${playlist.name}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/api/custom/playlists/${playlist.id}`, { method: 'DELETE' });
            if (response.ok) {
                await this.loadPlaylists();
            } else {
                alert('Failed to delete playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error deleting playlist:', error);
            alert('Failed to delete playlist.');
        }
    };

    // ── Playlist thumbnail ────────────────────────────────────────────────────

    openThumbnailModal = (playlist) => {
        this.thumbnailTargetPlaylist(playlist);
        this.thumbnailPreviewUrl(playlist.thumbnailUrl ? playlist.thumbnailUrl + '?t=' + Date.now() : null);
        this.thumbnailFile(null);
        new bootstrap.Modal(document.getElementById('playlistThumbnailModal')).show();
    };

    onThumbnailDragOver = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        return true;
    };

    onThumbnailDrop = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setThumbnailFile(file);
        return true;
    };

    triggerThumbnailInput = () => {
        document.getElementById('playlistThumbnailInput').click();
    };

    onThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setThumbnailFile(file);
        event.target.value = '';
    };

    _setThumbnailFile = (file) => {
        this.thumbnailFile(file);
        const reader = new FileReader();
        reader.onload = (e) => this.thumbnailPreviewUrl(e.target.result);
        reader.readAsDataURL(file);
    };

    uploadPlaylistThumbnail = async () => {
        const playlist = this.thumbnailTargetPlaylist();
        const file = this.thumbnailFile();
        if (!playlist || !file) return;

        this.uploadingThumbnail(true);
        try {
            const form = new FormData();
            form.append('file', file);
            const response = await fetch(`/api/custom/playlists/${playlist.Id}/thumbnail`, {
                method: 'POST',
                body: form
            });

            if (response.ok) {
                this.thumbnailCacheBust(Date.now());
                const data = await response.json();
                const idx = this.playlists().indexOf(playlist);
                if (idx >= 0) {
                    const updated = Object.assign({}, playlist, { ThumbnailUrl: data.thumbnailUrl });
                    this.playlists.splice(idx, 1, updated);
                }
                bootstrap.Modal.getInstance(document.getElementById('playlistThumbnailModal')).hide();
            } else {
                alert('Failed to upload thumbnail. Please try again.');
            }
        } catch (error) {
            console.error('Error uploading thumbnail:', error);
            alert('Failed to upload thumbnail.');
        } finally {
            this.uploadingThumbnail(false);
        }
    };
}

document.addEventListener('DOMContentLoaded', async () => {
    const viewModel = new CustomChannelViewModel(channelId);
    ko.applyBindings(viewModel);
    await viewModel.load();

    // Allow pressing Enter in the search box
    document.getElementById('videosSearchInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.searchVideos();
    });
});
