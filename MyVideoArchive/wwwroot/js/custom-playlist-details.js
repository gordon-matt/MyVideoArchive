import { formatDuration } from './utils.js';

const PAGE_SIZE = 60;

class CustomPlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.videos = ko.observableArray([]);
        this.loading = ko.observable(true);

        // Paging
        this.currentPage = ko.observable(1);
        this.totalPages = ko.observable(0);
        this.totalCount = ko.observable(0);
        this.pageNumbers = ko.computed(() => this._buildPageNumbers());

        this.formatDuration = formatDuration;
    }

    _buildPageNumbers() {
        const total = this.totalPages();
        const current = this.currentPage();
        if (total <= 1) return [];
        const delta = 2;
        const pages = [];
        for (let i = Math.max(1, current - delta); i <= Math.min(total, current + delta); i++) {
            pages.push(i);
        }
        return pages;
    }

    loadPlaylist = async () => {
        this.loading(true);
        try {
            const params = new URLSearchParams({ page: this.currentPage(), pageSize: PAGE_SIZE });
            const response = await fetch(`/api/custom-playlists/${this.playlistId}/videos?${params}`);

            if (!response.ok) {
                console.error('Playlist not found');
                return;
            }

            const data = await response.json();
            this.playlist(data.playlist);
            this.videos(data.videos || []);
            this.totalCount(data.pagination?.totalCount ?? 0);
            this.totalPages(data.pagination?.totalPages ?? 0);

            document.title = `${data.playlist?.name ?? 'Playlist'} - MyVideoArchive`;
        } catch (error) {
            console.error('Error loading playlist:', error);
        } finally {
            this.loading(false);
        }
    };

    openVideo = (item) => {
        window.location.href = `/videos/${item.video.id}`;
    };

    removeVideo = async (item) => {
        if (!confirm(`Remove "${item.video.title}" from this playlist?`)) return;

        try {
            const response = await fetch(
                `/api/custom-playlists/${this.playlistId}/videos/${item.video.id}`,
                { method: 'DELETE' });

            if (response.ok) {
                this.videos.remove(item);
                this.totalCount(this.totalCount() - 1);
            } else {
                alert('Failed to remove video from playlist.');
            }
        } catch (error) {
            console.error('Error removing video:', error);
            alert('An error occurred. Please try again.');
        }
    };

    // Paging
    previousPage = () => {
        if (this.currentPage() > 1) {
            this.currentPage(this.currentPage() - 1);
            this.loadPlaylist();
        }
    };

    nextPage = () => {
        if (this.currentPage() < this.totalPages()) {
            this.currentPage(this.currentPage() + 1);
            this.loadPlaylist();
        }
    };

    goToPage = (page) => {
        if (page >= 1 && page <= this.totalPages()) {
            this.currentPage(page);
            this.loadPlaylist();
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomPlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylist();
});
