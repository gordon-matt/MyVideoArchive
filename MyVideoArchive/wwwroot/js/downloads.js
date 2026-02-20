function DownloadsViewModel() {
    var self = this;

    self.videos = ko.observableArray([]);
    self.channels = ko.observableArray([]);
    self.selectedChannelId = ko.observable('');
    self.statusFilter = ko.observable('all');
    self.loading = ko.observable(true);
    self.checking = ko.observable(false);

    // Pagination
    self.currentPage = ko.observable(1);
    self.pageSize = 25;
    self.totalPages = ko.observable(1);
    self.totalCount = ko.observable(0);

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

    self.loadChannels = function () {
        fetch('/odata/ChannelOData?$orderby=Name')
            .then(response => response.json())
            .then(data => {
                self.channels(data.value || []);
            })
            .catch(error => {
                console.error('Error loading channels:', error);
            });
    };

    self.loadVideos = function () {
        self.loading(true);
        self.currentPage(1);
        self.fetchVideos();
    };

    self.fetchVideos = function () {
        self.loading(true);

        var filter = 'DownloadedAt eq null';

        // Add status filter
        if (self.statusFilter() === 'available') {
            filter += ' and IsQueued eq false';
        } else if (self.statusFilter() === 'queued') {
            filter += ' and IsQueued eq true';
        }

        // Add channel filter
        if (self.selectedChannelId()) {
            filter += ' and ChannelId eq ' + self.selectedChannelId();
        }

        var url = '/odata/VideoOData?$filter=' + filter +
            '&$expand=Channel' +
            '&$orderby=UploadDate desc' +
            '&$skip=' + ((self.currentPage() - 1) * self.pageSize) +
            '&$top=' + self.pageSize +
            '&$count=true';

        fetch(url)
            .then(response => response.json())
            .then(data => {
                var videos = (data.value || []).map(function (v) {
                    return {
                        id: v.Id,
                        videoId: v.VideoId,
                        title: v.Title,
                        description: v.Description,
                        url: v.Url,
                        thumbnailUrl: v.ThumbnailUrl,
                        duration: v.Duration,
                        uploadDate: v.UploadDate,
                        viewCount: v.ViewCount,
                        likeCount: v.LikeCount,
                        isQueued: v.IsQueued,
                        channelId: v.ChannelId,
                        channelName: v.Channel ? v.Channel.Name : 'Unknown Channel'
                    };
                });

                self.videos(videos);
                self.totalCount(data['@@odata.count'] || 0);
                self.totalPages(Math.ceil(self.totalCount() / self.pageSize));
                self.loading(false);
            })
            .catch(error => {
                console.error('Error loading videos:', error);
                self.loading(false);
            });
    };

    self.goToPage = function (page) {
        if (page >= 1 && page <= self.totalPages()) {
            self.currentPage(page);
            self.fetchVideos();
        }
    };

    self.previousPage = function () {
        if (self.currentPage() > 1) {
            self.currentPage(self.currentPage() - 1);
            self.fetchVideos();
        }
    };

    self.nextPage = function () {
        if (self.currentPage() < self.totalPages()) {
            self.currentPage(self.currentPage() + 1);
            self.fetchVideos();
        }
    };

    self.checkForNewVideos = function () {
        self.checking(true);

        fetch('/api/channels/sync-all', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        })
            .then(response => response.json())
            .then(data => {
                self.checking(false);
                alert(data.message || 'Sync job queued successfully!');
                setTimeout(function () {
                    self.loadVideos();
                }, 2000);
            })
            .catch(error => {
                console.error('Error syncing channels:', error);
                self.checking(false);
                alert('Error syncing channels. Please try again.');
            });
    };

    self.downloadVideo = function (video) {
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
                // Remove from UI or mark as queued
                video.isQueued = true;
                self.videos.remove(video);
            })
            .catch(error => {
                console.error('Error queueing video:', error);
                alert('Error queueing video for download. Please try again.');
            });
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
    viewModel = new DownloadsViewModel();
    ko.applyBindings(viewModel);
    viewModel.loadChannels();
    viewModel.loadVideos();
});