function VideoViewModel() {
    var self = this;

    self.videos = ko.observableArray([]);
    self.loading = ko.observable(true);

    self.loadVideos = function () {
        self.loading(true);

        fetch('/odata/VideoOData?$filter=DownloadedAt ne null&$orderby=DownloadedAt desc&$top=20&$expand=Channel')
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

    self.formatDate = function (dateString) {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
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

    self.formatFileSize = function (bytes) {
        if (!bytes) return 'N/A';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
    };
}

var viewModel;

$(document).ready(function () {
    viewModel = new VideoViewModel();
    ko.applyBindings(viewModel);
    viewModel.loadVideos();
});