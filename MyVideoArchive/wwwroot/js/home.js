class VideoViewModel {
    constructor() {
        this.videos = ko.observableArray([]);
        this.loading = ko.observable(true);
    }

    loadVideos = async () => {
        this.loading(true);

        await fetch('/odata/VideoOData?$filter=DownloadedAt ne null&$orderby=DownloadedAt desc&$top=20&$expand=Channel')
            .then(response => response.json())
            .then(data => {
                this.videos(data.value || []);
                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading videos:', error);
                this.loading(false);
            });
    };

    formatDate = (dateString) => {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
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

    formatFileSize = function (bytes) {
        if (!bytes) return 'N/A';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new VideoViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadVideos();
});