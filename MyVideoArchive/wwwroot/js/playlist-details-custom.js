import { formatDate, formatDuration } from './utils.js';

class CustomPlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.currentVideoUrl = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.useCustomOrder = ko.observable(false);
        this.sortableInstance = null;

        // Edit playlist form
        this.editPlaylistName = ko.observable('');
        this.editPlaylistDescription = ko.observable('');

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
    }

    loadPlaylist = async () => {
        try {
            const response = await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`);
            if (!response.ok) throw new Error('Playlist not found');

            const data = await response.json();
            this.playlist(data);
            this.loading(false);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error loading playlist:', error);
            this.loading(false);
        }
    };

    loadPlaylistVideos = async () => {
        this.loadingVideos(true);
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/videos?useCustomOrder=${this.useCustomOrder()}`);
            const data = await response.json();
            this.playlistVideos(data.videos || []);

            setTimeout(() => this.initializeSortable(), 100);

            const firstDownloaded = this.playlistVideos().find(v => v.downloadedAt);
            if (firstDownloaded) this.playVideo(firstDownloaded);
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loadingVideos(false);
        }
    };

    playVideo = (video) => {
        if (!video.downloadedAt) return;
        this.currentVideo(video);
        this.currentVideoUrl(`/api/videos/${video.id}/stream`);
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    toggleOrderMode = () => {
        const isNowCustom = this.useCustomOrder();
        if (!isNowCustom) {
            this.clearCustomOrder();
        } else {
            this.enableCustomOrder();
        }
        return true;
    };

    enableCustomOrder = async () => {
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/order-setting`);
            const data = await response.json();
            if (!data.useCustomOrder) {
                await this.saveCustomOrder(true);
            } else {
                await this.loadPlaylistVideos();
            }
        } catch {
            await this.saveCustomOrder(true);
        }
    };

    clearCustomOrder = async () => {
        await this.saveCustomOrder(true);
    };

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

    saveCustomOrder = async (reloadAfterSave = false) => {
        const videoOrders = this.useCustomOrder()
            ? this.playlistVideos().map((v, i) => ({ videoId: v.id, order: i + 1 }))
            : [];

        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/reorder`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ useCustomOrder: this.useCustomOrder(), videoOrders })
            });

            if (!response.ok) throw new Error('Server error');

            if (this.sortableInstance)
                this.sortableInstance.option('disabled', !this.useCustomOrder());

            if (reloadAfterSave) await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error saving order:', error);
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
                await this.loadPlaylist();
            } else {
                alert('Failed to update playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error saving playlist:', error);
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomPlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylist();
});
