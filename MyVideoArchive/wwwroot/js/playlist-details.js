function PlaylistDetailsViewModel(playlistId) {
    var self = this;

    self.playlistId = playlistId;
    self.playlist = ko.observable(null);
    self.playlistVideos = ko.observableArray([]);
    self.currentVideo = ko.observable(null);
    self.currentVideoUrl = ko.observable(null);
    self.loading = ko.observable(true);
    self.loadingVideos = ko.observable(false);
    self.useCustomOrder = ko.observable(false);
    self.sortableInstance = null;

    self.loadPlaylist = function () {
        fetch('/odata/PlaylistApi(' + self.playlistId + ')?$expand=Channel')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Playlist not found');
                }
                return response.json();
            })
            .then(data => {
                self.playlist(data);
                self.loading(false);
                self.loadPlaylistVideos();
            })
            .catch(error => {
                console.error('Error loading playlist:', error);
                self.loading(false);
            });
    };

    self.loadPlaylistVideos = function () {
        self.loadingVideos(true);

        // Load playlist
        fetch('/odata/PlaylistApi(' + self.playlistId + ')?$expand=Channel')
            .then(response => response.json())
            .then(playlistData => {
                self.playlist(playlistData);
            })
            .catch(error => {
                console.error('Error loading playlist:', error);
            });

        // Load videos with proper ordering
        fetch(`/api/playlists/${self.playlistId}/videos?useCustomOrder=${self.useCustomOrder()}`)
            .then(response => response.json())
            .then(data => {
                self.playlistVideos(data.videos || []);
                self.loadingVideos(false);

                // Initialize drag-drop after data is loaded
                setTimeout(function () {
                    self.initializeSortable();
                }, 100);

                // Auto-play first downloaded video
                if (self.playlistVideos().length > 0) {
                    var firstDownloaded = self.playlistVideos().find(function (v) { return v.downloadedAt; });
                    if (firstDownloaded) {
                        self.playVideo(firstDownloaded);
                    }
                }
            })
            .catch(error => {
                console.error('Error loading playlist videos:', error);
                self.loadingVideos(false);
            });
    };

    self.playVideo = function (video) {
        if (!video.downloadedAt) {
            return; // Just don't play if not downloaded, don't show alert
        }

        self.currentVideo(video);
        self.currentVideoUrl('/api/videos/' + video.id + '/stream');

        // Scroll to top
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    self.downloadVideo = function (video, event) {
        event.stopPropagation(); // Prevent triggering playVideo

        if (video.isQueued) {
            alert('This video is already queued for download.');
            return;
        }

        if (!confirm('Queue "' + video.title + '" for download?')) {
            return;
        }

        fetch('/api/channels/' + video.channelId + '/videos/download', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: [video.id] })
        })
            .then(response => response.json())
            .then(data => {
                alert(data.message);
                self.loadPlaylistVideos();
            })
            .catch(error => {
                console.error('Error queueing video:', error);
                alert('Error queueing video for download. Please try again.');
            });
    };

    self.toggleOrderMode = function () {
        // The checked binding already toggles the value
        var isNowCustomOrder = self.useCustomOrder(); // Get the NEW value (after toggle)

        if (!isNowCustomOrder) {
            // Switching FROM custom TO original - delete custom orders and reload
            self.clearCustomOrder();
        } else {
            // Switching FROM original TO custom
            // If there's no saved custom order, create one with current order
            // Otherwise, just reload to get the saved custom order
            self.enableCustomOrder();
        }

        return true;
    };

    self.enableCustomOrder = function () {
        // Check if custom order already exists, if not, save current order as custom
        fetch('/api/playlists/' + self.playlistId + '/order-setting')
            .then(response => response.json())
            .then(data => {
                if (!data.useCustomOrder) {
                    // No custom order exists, save current order
                    self.saveCustomOrder(true);
                } else {
                    // Custom order exists, reload to show it
                    self.loadPlaylistVideos();
                }
            })
            .catch(error => {
                console.error('Error checking custom order:', error);
                // Just save current order as fallback
                self.saveCustomOrder(true);
            });
    };

    self.clearCustomOrder = function () {
        // Delete custom orders by saving with useCustomOrder: false
        self.saveCustomOrder(true);
    };

    self.initializeSortable = function () {
        var container = document.getElementById('videoListContainer');
        if (!container) return;

        // Destroy existing instance if any
        if (self.sortableInstance) {
            self.sortableInstance.destroy();
        }

        // Create new Sortable instance
        self.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
            animation: 150,
            disabled: !self.useCustomOrder(),
            onEnd: function (evt) {
                // Get the new order from the DOM
                var items = container.querySelectorAll('.playlist-video-item');
                var newOrder = [];

                items.forEach(function (item) {
                    var videoId = parseInt(item.getAttribute('data-video-id'));
                    var video = self.playlistVideos().find(function (v) { return v.id === videoId; });
                    if (video) {
                        newOrder.push(video);
                    }
                });

                // Update the observable array with the new order
                self.playlistVideos(newOrder);

                // Save the new order
                self.saveCustomOrder();
            }
        });
    };

    self.saveCustomOrder = function (reloadAfterSave) {
        var videoOrders = [];

        if (self.useCustomOrder()) {
            // Build the order array
            videoOrders = self.playlistVideos().map(function (video, index) {
                return {
                    videoId: video.id,
                    order: index + 1
                };
            });
        }

        fetch('/api/playlists/' + self.playlistId + '/reorder', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                useCustomOrder: self.useCustomOrder(),
                videoOrders: videoOrders
            })
        })
            .then(response => {
                if (!response.ok) {
                    return response.text().then(text => {
                        throw new Error('Server error: ' + text);
                    });
                }
                return response.json();
            })
            .then(data => {
                // Update sortable state
                if (self.sortableInstance) {
                    self.sortableInstance.option('disabled', !self.useCustomOrder());
                }

                // Reload videos if requested (when toggling order mode)
                if (reloadAfterSave) {
                    self.loadPlaylistVideos();
                }
            })
            .catch(error => {
                console.error('Error saving order:', error);
                alert('Error saving custom order: ' + error.message);
            });
    };

    self.formatDate = function (dateString) {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    self.formatDuration = function (duration) {
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

    self.formatNumber = function (num) {
        if (!num) return '0';
        return num.toLocaleString();
    };
}

var viewModel;

$(document).ready(function () {
    viewModel = new PlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    viewModel.loadPlaylist();
});