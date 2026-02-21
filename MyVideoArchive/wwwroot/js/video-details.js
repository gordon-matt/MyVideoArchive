class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.loading = ko.observable(true);
        this.videoUrl = ko.observable(null);
    }

    loadVideo = function () {
        fetch('/odata/VideoOData(' + this.videoId + ')?$expand=Channel')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Video not found');
                }
                return response.json();
            })
            .then(data => {
                this.video(data);

                // Set up video URL for streaming
                if (data.Id) {
                    this.videoUrl('/api/videos/' + data.Id + '/stream');
                }

                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading video:', error);
                this.loading(false);
            });
    };

    formatDate = function (dateString) {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    formatDuration = function (duration) {
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

    formatNumber = function (num) {
        if (!num) return '0';
        return num.toLocaleString();
    };

    formatFileSize = function (bytes) {
        if (!bytes) return 'N/A';
        var sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
        if (bytes == 0) return '0 Byte';
        var i = parseInt(Math.floor(Math.log(bytes) / Math.log(1024)));
        return Math.round(bytes / Math.pow(1024, i), 2) + ' ' + sizes[i];
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);
    viewModel.loadVideo();
});