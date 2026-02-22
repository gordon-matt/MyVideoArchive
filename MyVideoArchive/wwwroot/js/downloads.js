import { delay, formatDate, formatDuration } from './utils.js';

class DownloadsViewModel {
    constructor() {
        this.videos = ko.observableArray([]);
        this.channels = ko.observableArray([]);
        this.selectedChannelId = ko.observable('');
        this.selectedChannelId.subscribe(() => this.loadVideos());
        this.statusFilter = ko.observable('all');
        this.loading = ko.observable(true);
        this.checking = ko.observable(false);

        // Pagination
        this.currentPage = ko.observable(1);
        this.pageSize = 20;
        this.totalPages = ko.observable(1);
        this.totalCount = ko.observable(0);

        this.pageNumbers = ko.computed(() => {
            var pages = [];
            var current = this.currentPage();
            var total = this.totalPages();

            // Show max 5 page numbers
            var start = Math.max(1, current - 2);
            var end = Math.min(total, start + 4);
            start = Math.max(1, end - 4);

            for (var i = start; i <= end; i++) {
                pages.push(i);
            }
            return pages;
        });

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
    }

    loadChannels = async () => {
        await fetch('/odata/ChannelOData?$orderby=Name')
            .then(response => response.json())
            .then(data => {
                this.channels(data.value || []);
            })
            .catch(error => {
                console.error('Error loading channels:', error);
            });
    };

    loadVideos = async () => {
        this.loading(true);
        this.currentPage(1);
        await this.fetchVideos();
    };

    fetchVideos = async () => {
        this.loading(true);

        var filter = 'DownloadedAt eq null';

        // Add status filter
        if (this.statusFilter() === 'available') {
            filter += ' and IsQueued eq false';
        } else if (this.statusFilter() === 'queued') {
            filter += ' and IsQueued eq true';
        }

        // Add channel filter
        if (this.selectedChannelId()) {
            filter += ' and ChannelId eq ' + this.selectedChannelId();
        }

        var url = '/odata/VideoOData?$filter=' + filter +
            '&$expand=Channel' +
            '&$orderby=UploadDate desc' +
            '&$skip=' + ((this.currentPage() - 1) * this.pageSize) +
            '&$top=' + this.pageSize +
            '&$count=true';

        await fetch(url)
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

                this.videos(videos);
                this.totalCount(data['@odata.count'] || 0);
                this.totalPages(Math.ceil(this.totalCount() / this.pageSize));
                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading videos:', error);
                this.loading(false);
            });
    };

    goToPage = async (page) => {
        if (page >= 1 && page <= this.totalPages()) {
            this.currentPage(page);
            await this.fetchVideos();
        }
    };

    previousPage = async () => {
        if (this.currentPage() > 1) {
            this.currentPage(this.currentPage() - 1);
            await this.fetchVideos();
        }
    };

    nextPage = async () => {
        if (this.currentPage() < this.totalPages()) {
            this.currentPage(this.currentPage() + 1);
            await this.fetchVideos();
        }
    };

    checkForNewVideos = async () => {
        this.checking(true);

        try {
            const response = await fetch('/api/channels/sync-all', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });

            const data = await response.json();

            this.checking(false);
            alert(data.message || 'Sync job queued successfully!');

            await delay(2000);
            await this.loadVideos();
        } catch (error) {
            console.error('Error syncing channels:', error);
            this.checking(false);
            alert('Error syncing channels. Please try again.');
        }
    };

    downloadVideo = async (video) => {
        if (video.isQueued) {
            alert('This video is already queued for download.');
            return;
        }

        if (!confirm(`Queue "${video.title}" for download?`)) {
            return;
        }

        await fetch(`/api/channels/${video.channelId}/videos/download`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: [video.id] })
        })
        .then(response => response.json())
        .then(data => {
            alert(data.message);
            // Remove from UI or mark as queued
            video.isQueued = true;
            this.videos.remove(video);
        })
        .catch(error => {
            console.error('Error queueing video:', error);
            alert('Error queueing video for download. Please try again.');
        });
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new DownloadsViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadChannels();
    await viewModel.loadVideos();
});