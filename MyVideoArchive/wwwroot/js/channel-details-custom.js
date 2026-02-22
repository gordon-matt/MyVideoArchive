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
            if (response.ok) {
                const data = await response.json();
                this.channel(data);
            }
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
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomChannelViewModel(channelId);
    ko.applyBindings(viewModel);
    await viewModel.load();
});
