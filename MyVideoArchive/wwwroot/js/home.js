import { formatDuration, formatFileSize } from './utils.js';

class VideoViewModel {
    constructor() {
        this.videos = ko.observableArray([]);
        this.loading = ko.observable(true);

        this.formatDuration = formatDuration;
        this.formatFileSize = formatFileSize;
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
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new VideoViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadVideos();
});