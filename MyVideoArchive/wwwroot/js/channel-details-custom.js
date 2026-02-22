import { formatDate } from './utils.js';

class CustomChannelViewModel {
    constructor(channelId) {
        this.channelId = channelId;
        this.channel = ko.observable(null);
        this.videos = ko.observableArray([]);
        this.playlists = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.playlistsLoading = ko.observable(false);

        // Edit channel form
        this.editName = ko.observable('');
        this.editDescription = ko.observable('');
        this.editThumbnailUrl = ko.observable('');

        // Create playlist form
        this.newPlaylistName = ko.observable('');
        this.newPlaylistDescription = ko.observable('');

        // Playlist thumbnail upload state
        this.thumbnailTargetPlaylist = ko.observable(null);
        this.thumbnailPreviewUrl = ko.observable(null);
        this.thumbnailFile = ko.observable(null);
        this.uploadingThumbnail = ko.observable(false);

        // Cache-bust counter so Knockout re-renders thumbnails after upload
        this.thumbnailCacheBust = ko.observable(Date.now());

        this.formatDate = formatDate;
    }

    load = async () => {
        await Promise.all([
            this.loadChannel(),
            this.loadVideos(),
            this.loadPlaylists()
        ]);
    };

    loadChannel = async () => {
        try {
            const response = await fetch(`/odata/ChannelOData(${this.channelId})`);
            if (response.ok) this.channel(await response.json());
        } catch (error) {
            console.error('Error loading channel:', error);
        }
    };

    loadVideos = async () => {
        this.loading(true);
        try {
            const response = await fetch(`/odata/VideoOData?$filter=ChannelId eq ${this.channelId}&$orderby=UploadDate desc`);
            if (response.ok) {
                const data = await response.json();
                this.videos(data.value || []);
            }
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loading(false);
        }
    };

    loadPlaylists = async () => {
        this.playlistsLoading(true);
        try {
            const response = await fetch(`/odata/PlaylistOData?$filter=ChannelId eq ${this.channelId}&$orderby=Name`);
            if (response.ok) {
                const data = await response.json();
                this.playlists(data.value || []);
            }
        } catch (error) {
            console.error('Error loading playlists:', error);
        } finally {
            this.playlistsLoading(false);
        }
    };

    // ── Edit channel ─────────────────────────────────────────────────────────

    openEditChannel = () => {
        const ch = this.channel();
        if (!ch) return;
        this.editName(ch.Name || '');
        this.editDescription(ch.Description || '');
        this.editThumbnailUrl(ch.ThumbnailUrl || '');
        new bootstrap.Modal(document.getElementById('editChannelModal')).show();
    };

    saveChannel = async () => {
        try {
            const response = await fetch(`/api/custom/channels/${this.channelId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.editName(),
                    description: this.editDescription() || null,
                    thumbnailUrl: this.editThumbnailUrl() || null
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
        if (!confirm(`Delete playlist "${playlist.Name}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/api/custom/playlists/${playlist.Id}`, { method: 'DELETE' });
            if (response.ok) {
                this.playlists.remove(playlist);
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
        this.thumbnailPreviewUrl(playlist.ThumbnailUrl ? playlist.ThumbnailUrl + '?t=' + Date.now() : null);
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
        // Reset input so re-selecting the same file triggers change again
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
                // Force Knockout to re-render all thumbnail images with a new cache-bust token
                this.thumbnailCacheBust(Date.now());
                // Update the ThumbnailUrl on the playlist object in the array
                const data = await response.json();
                const idx = this.playlists().indexOf(playlist);
                if (idx >= 0) {
                    // Replace the object so Knockout picks up the change
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
});
