function ChannelDetailsViewModel(channelId) {
    var self = this;

    self.channelId = channelId;
    self.channel = ko.observable(null);
    self.videos = ko.observableArray([]);
    self.availableVideos = ko.observableArray([]);
    self.playlists = ko.observableArray([]);
    self.loading = ko.observable(true);
    self.availableLoading = ko.observable(false);
    self.playlistsLoading = ko.observable(false);
    self.refreshingPlaylists = ko.observable(false);

    // Available videos state
    self.currentPage = ko.observable(1);
    self.pageSize = 20;
    self.totalPages = ko.observable(1);
    self.totalCount = ko.observable(0);
    self.showIgnored = ko.observable(false);
    self.selectAll = ko.observable(false);

    // Playlists state
    self.showIgnoredPlaylists = ko.observable(false);
    self.selectAllPlaylists = ko.observable(false);

    self.loadChannel = function () {
        fetch('/odata/ChannelOData(' + self.channelId + ')')
            .then(response => response.json())
            .then(data => {
                self.channel(data);
            })
            .catch(error => {
                console.error('Error loading channel:', error);
            });
    };

    self.loadVideos = function () {
        fetch('/odata/VideoOData?$filter=ChannelId eq ' + self.channelId + ' and DownloadedAt ne null&$orderby=UploadDate desc')
            .then(response => response.json())
            .then(data => {
                self.videos(data.value || []);
                self.loading(false);
            })
            .catch(error => {
                console.error('Error loading videos:', error);
                self.loading(false);
            });
    };

    self.loadAvailableVideos = function () {
        self.availableLoading(true);
        var url = '/api/channels/' + self.channelId + '/videos/available?page=' + self.currentPage() +
            '&pageSize=' + self.pageSize + '&showIgnored=' + self.showIgnored();

        fetch(url)
            .then(response => response.json())
            .then(data => {
                var videos = data.videos.map(function (v) {
                    v.selected = ko.observable(false);
                    return v;
                });
                self.availableVideos(videos);
                self.totalPages(data.pagination.totalPages);
                self.totalCount(data.pagination.totalCount);
                self.availableLoading(false);
                self.selectAll(false);
            })
            .catch(error => {
                console.error('Error loading available videos:', error);
                self.availableLoading(false);
            });
    };

    self.loadPlaylists = function () {
        self.playlistsLoading(true);
        var url = '/api/channels/' + self.channelId + '/playlists/available?showIgnored=' + self.showIgnoredPlaylists();

        fetch(url)
            .then(response => response.json())
            .then(data => {
                var playlists = data.playlists.map(function (p) {
                    p.selected = ko.observable(false);
                    return p;
                });
                self.playlists(playlists);
                self.playlistsLoading(false);
                self.selectAllPlaylists(false);
            })
            .catch(error => {
                console.error('Error loading playlists:', error);
                self.playlistsLoading(false);
            });
    };

    // Playlist functions
    self.toggleSelectAllPlaylists = function () {
        var selected = self.selectAllPlaylists();
        self.playlists().forEach(function (playlist) {
            playlist.selected(selected);
        });
    };

    self.subscribeSelectedPlaylists = function () {
        var selectedPlaylists = self.playlists()
            .filter(function (p) { return p.selected(); });

        var selectedIds = selectedPlaylists.map(function (p) { return p.id; });

        if (selectedIds.length === 0) {
            alert('Please select at least one playlist to subscribe.');
            return;
        }

        fetch('/api/channels/' + self.channelId + '/playlists/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ playlistIds: selectedIds })
        })
            .then(response => response.json())
            .then(data => {
                alert(data.message);
                self.loadPlaylists();
            })
            .catch(error => {
                console.error('Error subscribing to playlists:', error);
                alert('Error subscribing to playlists. Please try again.');
            });
    };

    self.subscribeAllPlaylists = function () {
        if (!confirm('Are you sure you want to subscribe to all playlists for this channel?')) {
            return;
        }

        fetch('/api/channels/' + self.channelId + '/playlists/subscribe-all', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(response => response.json())
            .then(data => {
                alert(data.message);
                self.loadPlaylists();
            })
            .catch(error => {
                console.error('Error subscribing to all playlists:', error);
                alert('Error subscribing to playlists. Please try again.');
            });
    };

    self.ignorePlaylist = function (playlist) {
        fetch('/api/channels/' + self.channelId + '/playlists/' + playlist.id + '/ignore', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ isIgnored: !playlist.isIgnored })
        })
            .then(response => response.json())
            .then(data => {
                self.loadPlaylists();
            })
            .catch(error => {
                console.error('Error ignoring playlist:', error);
                alert('Error updating playlist status. Please try again.');
            });
    };

    self.toggleShowIgnoredPlaylists = function () {
        // The checked binding already toggles the value, so just reload
        self.loadPlaylists();
        return true; // Allow default checkbox behavior
    };

    self.refreshPlaylists = function () {
        if (!confirm('This will fetch the latest playlists from YouTube. Continue?')) {
            return;
        }

        self.refreshingPlaylists(true);

        fetch('/api/channels/' + self.channelId + '/playlists/refresh', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(response => response.json())
            .then(data => {
                self.refreshingPlaylists(false);
                alert(data.message);
                self.loadPlaylists();
            })
            .catch(error => {
                console.error('Error refreshing playlists:', error);
                self.refreshingPlaylists(false);
                alert('Error refreshing playlists from YouTube. Please try again.');
            });
    };

    // Pagination
    self.goToPage = function (page) {
        if (page >= 1 && page <= self.totalPages()) {
            self.currentPage(page);
            self.loadAvailableVideos();
        }
    };

    self.previousPage = function () {
        if (self.currentPage() > 1) {
            self.currentPage(self.currentPage() - 1);
            self.loadAvailableVideos();
        }
    };

    self.nextPage = function () {
        if (self.currentPage() < self.totalPages()) {
            self.currentPage(self.currentPage() + 1);
            self.loadAvailableVideos();
        }
    };

    self.pageNumbers = ko.computed(function () {
        var pages = [];
        var current = self.currentPage();
        var total = self.totalPages();

        // Show max 5 page numbers
        var start = Math.max(1, current - 2);
        var end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);

        for (var i = start; i <= end; i++) {
            pages.push(i);
        }
        return pages;
    });

    // Select All
    self.toggleSelectAll = function () {
        var selected = self.selectAll();
        self.availableVideos().forEach(function (video) {
            video.selected(selected);
        });
    };

    // Download Selected
    self.downloadSelected = function () {
        var selectedVideos = self.availableVideos()
            .filter(function (v) { return v.selected(); });

        var selectedIds = selectedVideos.map(function (v) { return v.id; });

        if (selectedIds.length === 0) {
            alert('Please select at least one video to download.');
            return;
        }

        fetch('/api/channels/' + self.channelId + '/videos/download', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: selectedIds })
        })
            .then(response => response.json())
            .then(data => {
                // Remove downloaded videos from the UI immediately
                selectedVideos.forEach(function (video) {
                    self.availableVideos.remove(video);
                });
                self.selectAll(false);

                alert(data.message);
            })
            .catch(error => {
                console.error('Error downloading videos:', error);
                alert('Error queueing downloads. Please try again.');
            });
    };

    // Download All
    self.downloadAll = function () {
        if (!confirm('Are you sure you want to download all available videos for this channel?')) {
            return;
        }

        fetch('/api/channels/' + self.channelId + '/videos/download-all', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(response => response.json())
            .then(data => {
                // Clear all videos from the available list
                self.availableVideos([]);
                alert(data.message);
            })
            .catch(error => {
                console.error('Error downloading all videos:', error);
                alert('Error queueing downloads. Please try again.');
            });
    };

    // Ignore Video
    self.ignoreVideo = function (video) {
        fetch('/api/channels/' + self.channelId + '/videos/' + video.id + '/ignore', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ isIgnored: !video.isIgnored })
        })
            .then(response => response.json())
            .then(data => {
                self.loadAvailableVideos();
            })
            .catch(error => {
                console.error('Error ignoring video:', error);
                alert('Error updating video status. Please try again.');
            });
    };

    // Toggle show ignored
    self.toggleShowIgnored = function () {
        // The checked binding already toggles the value, so just reload
        self.currentPage(1);
        self.loadAvailableVideos();
        return true; // Allow default checkbox behavior
    };

    self.formatDate = function (dateString) {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString();
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
}

var viewModel;

$(document).ready(function () {
    viewModel = new ChannelDetailsViewModel(channelId);
    ko.applyBindings(viewModel);
    viewModel.loadChannel();
    viewModel.loadVideos();
    viewModel.loadPlaylists();

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