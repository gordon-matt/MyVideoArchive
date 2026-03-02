import { formatDate, formatDuration, formatNumber } from './utils.js';

// Load all videos into the sidebar in one shot (custom playlists typically stay small)
const PAGE_SIZE = 500;

class CustomPlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.currentVideoUrl = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatNumber = formatNumber;
    }

    loadPlaylist = async () => {
        this.loading(true);
        try {
            const params = new URLSearchParams({ page: 1, pageSize: PAGE_SIZE });
            const response = await fetch(`/api/custom-playlists/${this.playlistId}/videos?${params}`);

            if (!response.ok) {
                console.error('Playlist not found');
                return;
            }

            const data = await response.json();
            this.playlist(data.playlist);
            this.playlistVideos(data.videos || []);

            document.title = `${data.playlist?.name ?? 'Playlist'} - MyVideoArchive`;

            const firstDownloaded = this.playlistVideos().find(item => item.video.downloadedAt);
            if (firstDownloaded) {
                this.playVideo(firstDownloaded);
            }
        } catch (error) {
            console.error('Error loading playlist:', error);
        } finally {
            this.loading(false);
        }
    };

    playVideo = (item) => {
        if (!item.video.downloadedAt) return;

        this.currentVideo(item.video);
        this.currentVideoUrl(`/api/videos/${item.video.id}/stream`);

        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    openVideoDetails = () => {
        const video = this.currentVideo();
        if (video) {
            window.location.href = `/videos/${video.id}`;
        }
    };

    removeVideo = async (item) => {
        if (!confirm(`Remove "${item.video.title}" from this playlist?`)) return;

        try {
            const response = await fetch(
                `/api/custom-playlists/${this.playlistId}/videos/${item.video.id}`,
                { method: 'DELETE' });

            if (response.ok) {
                this.playlistVideos.remove(item);
                if (this.currentVideo()?.id === item.video.id) {
                    this.currentVideo(null);
                    this.currentVideoUrl(null);
                }
            } else {
                alert('Failed to remove video from playlist.');
            }
        } catch (error) {
            console.error('Error removing video:', error);
            alert('An error occurred. Please try again.');
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomPlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylist();
});
