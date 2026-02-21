class PlaylistDetailsViewModel {
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
    }

    loadPlaylist = async () => {
        try {
            const response = await fetch('/odata/PlaylistOData(' + this.playlistId + ')?$expand=Channel');

            if (!response.ok) {
                throw new Error('Playlist not found');
            }

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

        // Load playlist
        await fetch('/odata/PlaylistOData(' + this.playlistId + ')?$expand=Channel')
            .then(response => response.json())
            .then(playlistData => {
                this.playlist(playlistData);
            })
            .catch(error => {
                console.error('Error loading playlist:', error);
            });

        // Load videos with proper ordering
        await fetch(`/api/playlists/${this.playlistId}/videos?useCustomOrder=${this.useCustomOrder()}`)
            .then(response => response.json())
            .then(data => {
                this.playlistVideos(data.videos || []);
                this.loadingVideos(false);

                // Initialize drag-drop after data is loaded
                setTimeout(() => {
                    this.initializeSortable();
                }, 100);

                // Auto-play first downloaded video
                if (this.playlistVideos().length > 0) {
                    var firstDownloaded = this.playlistVideos().find(function (v) { return v.downloadedAt; });
                    if (firstDownloaded) {
                        this.playVideo(firstDownloaded);
                    }
                }
            })
            .catch(error => {
                console.error('Error loading playlist videos:', error);
                this.loadingVideos(false);
            });
    };

    playVideo = (video) => {
        if (!video.downloadedAt) {
            return; // Just don't play if not downloaded, don't show alert
        }

        this.currentVideo(video);
        this.currentVideoUrl('/api/videos/' + video.id + '/stream');

        // Scroll to top
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    downloadVideo = async (video, event) => {
        event.stopPropagation(); // Prevent triggering playVideo

        if (video.isQueued) {
            alert('This video is already queued for download.');
            return;
        }

        if (!confirm('Queue "' + video.title + '" for download?')) {
            return;
        }

        try {
            const response = await fetch('/api/channels/' + video.channelId + '/videos/download', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoIds: [video.id] })
            });

            const data = await response.json();
            alert(data.message);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error queueing video:', error);
            alert('Error queueing video for download. Please try again.');
        }
    };

    toggleOrderMode = () => {
        // The checked binding already toggles the value
        var isNowCustomOrder = this.useCustomOrder(); // Get the NEW value (after toggle)

        if (!isNowCustomOrder) {
            // Switching FROM custom TO original - delete custom orders and reload
            this.clearCustomOrder();
        } else {
            // Switching FROM original TO custom
            // If there's no saved custom order, create one with current order
            // Otherwise, just reload to get the saved custom order
            this.enableCustomOrder();
        }

        return true;
    };

    enableCustomOrder = async () => {
        try {
            const response = await fetch('/api/playlists/' + this.playlistId + '/order-setting');

            const data = await response.json();

            if (!data.useCustomOrder) {
                // No custom order exists, save current order
                await this.saveCustomOrder(true);
            } else {
                // Custom order exists, reload to show it
                await this.loadPlaylistVideos();
            }
        } catch (error) {
            console.error('Error checking custom order:', error);
            // Just save current order as fallback
            await this.saveCustomOrder(true);
        }
    };

    clearCustomOrder = async () => {
        // Delete custom orders by saving with useCustomOrder: false
        await this.saveCustomOrder(true);
    };

    initializeSortable = () => {
        var container = document.getElementById('videoListContainer');
        if (!container) return;

        // Destroy existing instance if any
        if (this.sortableInstance) {
            this.sortableInstance.destroy();
        }

        // Create new Sortable instance
        this.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
            animation: 150,
            disabled: !this.useCustomOrder(),
            onEnd: async (evt) => {
                // Get the new order from the DOM
                var items = container.querySelectorAll('.playlist-video-item');
                var newOrder = [];

                items.forEach((item) => {
                    var videoId = parseInt(item.getAttribute('data-video-id'));
                    var video = this.playlistVideos().find(function (v) { return v.id === videoId; });
                    if (video) {
                        newOrder.push(video);
                    }
                });

                // Update the observable array with the new order
                this.playlistVideos(newOrder);

                // Save the new order
                await this.saveCustomOrder();
            }
        });
    };

    saveCustomOrder = async (reloadAfterSave) => {
        var videoOrders = [];

        if (this.useCustomOrder()) {
            // Build the order array
            videoOrders = this.playlistVideos().map(function (video, index) {
                return {
                    videoId: video.id,
                    order: index + 1
                };
            });
        }

        try {
            const response = await fetch('/api/playlists/' + this.playlistId + '/reorder', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    useCustomOrder: this.useCustomOrder(),
                    videoOrders: videoOrders
                })
            });

            if (!response.ok) {
                return response.text().then(text => {
                    throw new Error('Server error: ' + text);
                });
            }

            const data = await response.json();

            // Update sortable state
            if (this.sortableInstance) {
                this.sortableInstance.option('disabled', !this.useCustomOrder());
            }

            // Reload videos if requested (when toggling order mode)
            if (reloadAfterSave) {
                await this.loadPlaylistVideos();
            }
        } catch (error) {
            console.error('Error saving order:', error);
            alert('Error saving custom order: ' + error.message);
        }
    };

    formatDate = (dateString) => {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    formatDuration = (duration) => {
        if (!duration) return 'N/A';

        // Parse ISO 8601 duration format (e.g., PT10M13S)
        if (duration.startsWith('PT')) {
            var hours = 0, minutes = 0, seconds = 0;

            var match = duration.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
            if (match) {
                hours = parseInt(match[1]) || 0;
                minutes = parseInt(match[2]) || 0;
                seconds = parseInt(match[3]) || 0;
            }

            if (hours > 0) {
                return hours + ':' + minutes.toString().padStart(2, '0') + ':' + seconds.toString().padStart(2, '0');
            } else {
                return minutes + ':' + seconds.toString().padStart(2, '0');
            }
        }

        // If it's already in HH:MM:SS format
        if (duration.includes(':')) {
            return duration.substring(0, 8);
        }

        return duration;
    };

    formatNumber = (num) => {
        if (!num) return '0';
        return num.toLocaleString();
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new PlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylist();
});