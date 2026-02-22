import { formatDate, formatDuration } from './utils.js';

class ChannelDetailsViewModel {
    constructor(channelId) {
        this.channelId = channelId;
        this.channel = ko.observable(null);
        this.videos = ko.observableArray([]);
        this.availableVideos = ko.observableArray([]);
        this.playlists = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.availableLoading = ko.observable(false);
        this.playlistsLoading = ko.observable(false);
        this.refreshingPlaylists = ko.observable(false);

        // Available videos state
        this.currentPage = ko.observable(1);
        this.pageSize = 20;
        this.totalPages = ko.observable(1);
        this.totalCount = ko.observable(0);
        this.showIgnored = ko.observable(false);
        this.selectAll = ko.observable(false);

        // Playlists state
        this.showIgnoredPlaylists = ko.observable(false);
        this.selectAllPlaylists = ko.observable(false);

        // When selectAll changes, sync individual video checkboxes
        this.selectAll.subscribe((newValue) => {
            this.availableVideos().forEach(v => v.selected(newValue));
        });

        // When selectAllPlaylists changes, sync individual playlist checkboxes
        this.selectAllPlaylists.subscribe((newValue) => {
            this.playlists().forEach(p => p.selected(newValue));
        });

        this.pageNumbers = ko.computed(() => {
            const pages = [];
            const current = this.currentPage();
            const total = this.totalPages();

            // Show max 5 page numbers
            let start = Math.max(1, current - 2);
            let end = Math.min(total, start + 4);
            start = Math.max(1, end - 4);

            for (let i = start; i <= end; i++) {
                pages.push(i);
            }

            return pages;
        });

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
    }

    loadChannel = async () => {
        await fetch(`/odata/ChannelOData(${this.channelId})`)
            .then(response => response.json())
            .then(data => {
                this.channel(data);
            })
            .catch(error => {
                console.error('Error loading channel:', error);
            });
    };

    loadVideos = async () => {
        await fetch(`/odata/VideoOData?$filter=ChannelId eq ${this.channelId} and DownloadedAt ne null&$orderby=UploadDate desc`)
            .then(response => response.json())
            .then(async data => {
                // Initialise watched as an observable on every video so the binding
                // is always defined, even before the watched-status API responds.
                const videos = (data.value || []).map(v => {
                    v.watched = ko.observable(false);
                    return v;
                });
                this.videos(videos);
                this.loading(false);

                // Load watched status and update the observables
                if (videos.length > 0) {
                    const params = new URLSearchParams();
                    videos.forEach(v => params.append("videoIds", v.Id));
                    await fetch(`/api/user/videos/watched?${params.toString()}`)
                        .then(r => r.json())
                        .then(watchedData => {
                            const watchedSet = new Set(watchedData.watchedIds || []);
                            this.videos().forEach(v => v.watched(watchedSet.has(v.Id)));
                        })
                        .catch(() => {});
                }
            })
            .catch(error => {
                console.error('Error loading videos:', error);
                this.loading(false);
            });
    };

    // Delete physical file of a downloaded video
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
            } else {
                alert(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file:', error);
            alert('Error deleting video file. Please try again.');
        }
    };

    loadAvailableVideos = async () => {
        this.availableLoading(true);
        await fetch(`/api/channels/${this.channelId}/videos/available?page=${this.currentPage()}&pageSize=${this.pageSize}&showIgnored=${this.showIgnored()}`)
            .then(response => response.json())
            .then(data => {
                var videos = data.videos.map(function (v) {
                    v.selected = ko.observable(false);
                    return v;
                });
                this.availableVideos(videos);
                this.totalPages(data.pagination.totalPages);
                this.totalCount(data.pagination.totalCount);
                this.availableLoading(false);
                this.selectAll(false);
            })
            .catch(error => {
                console.error('Error loading available videos:', error);
                this.availableLoading(false);
            });
    };

    loadPlaylists = async () => {
        this.playlistsLoading(true);
        await fetch(`/api/channels/${this.channelId}/playlists/available?showIgnored=${this.showIgnoredPlaylists()}`)
            .then(response => response.json())
            .then(data => {
                var playlists = data.playlists.map(function (p) {
                    p.selected = ko.observable(false);
                    return p;
                });
                this.playlists(playlists);
                this.playlistsLoading(false);
                this.selectAllPlaylists(false);
            })
            .catch(error => {
                console.error('Error loading playlists:', error);
                this.playlistsLoading(false);
            });
    };

    // Playlist functions

    subscribeSelectedPlaylists = async () => {
        var selectedPlaylists = this.playlists().filter(function (p) {
            return p.selected();
        });

        var selectedIds = selectedPlaylists.map(function (p) {
            return p.id;
        });

        if (selectedIds.length === 0) {
            alert('Please select at least one playlist to subscribe.');
            return;
        }

        await fetch(`/api/channels/${this.channelId}/playlists/subscribe`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ playlistIds: selectedIds })
        })
        .then(response => response.json())
        .then(data => {
            alert(data.message);
            this.loadPlaylists();
        })
        .catch(error => {
            console.error('Error subscribing to playlists:', error);
            alert('Error subscribing to playlists. Please try again.');
        });
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
            const response = await fetch(`/api/channels/${this.channelId}/playlists/${playlist.id}/ignore`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isIgnored: !playlist.isIgnored })
            });

            const data = await response.json();
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error ignoring playlist:', error);
            alert('Error updating playlist status. Please try again.');
        }
    };

    toggleShowIgnoredPlaylists = async () => {
        // The checked binding already toggles the value, so just reload
        await this.loadPlaylists();
        return true; // Allow default checkbox behavior
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

    // Pagination
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

    // Download Selected
    downloadSelected = async () => {
        var selectedVideos = this.availableVideos().filter(function (v) {
            return v.selected();
        });

        var selectedIds = selectedVideos.map(function (v) {
            return v.id;
        });

        if (selectedIds.length === 0) {
            alert('Please select at least one video to download.');
            return;
        }

        await fetch(`/api/channels/${this.channelId}/videos/download`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: selectedIds })
        })
        .then(response => response.json())
        .then(data => {
            // Remove downloaded videos from the UI immediately
            selectedVideos.forEach((video) => {
                this.availableVideos.remove(video);
            });
            this.selectAll(false);

            alert(data.message);
        })
        .catch(error => {
            console.error('Error downloading videos:', error);
            alert('Error queueing downloads. Please try again.');
        });
    };

    // Download All
    downloadAll = async () => {
        if (!confirm('Are you sure you want to download all available videos for this channel?')) {
            return;
        }

        await fetch(`/api/channels/${this.channelId}/videos/download-all`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
        .then(response => response.json())
        .then(data => {
            // Clear all videos from the available list
            this.availableVideos([]);
            alert(data.message);
        })
        .catch(error => {
            console.error('Error downloading all videos:', error);
            alert('Error queueing downloads. Please try again.');
        });
    };

    // Ignore Video
    ignoreVideo = async (video) => {
        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/${video.id}/ignore`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isIgnored: !video.isIgnored })
            });

            const data = await response.json();
            await this.loadAvailableVideos();
        } catch (error) {
            console.error('Error ignoring video:', error);
            alert('Error updating video status. Please try again.');
        }
    };

    // Toggle show ignored
    toggleShowIgnored = async () => {
        // The checked binding already toggles the value, so just reload
        this.currentPage(1);
        await this.loadAvailableVideos();
        return true; // Allow default checkbox behavior
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new ChannelDetailsViewModel(channelId);
    ko.applyBindings(viewModel);
    await viewModel.loadChannel();
    await viewModel.loadVideos();
    await viewModel.loadPlaylists();

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
});