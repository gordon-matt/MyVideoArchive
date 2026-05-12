import { formatDuration, formatFileSize, encodeArchiveUrlForHtml } from './utils.js';

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
                this.videos((data.value || []).map(v => {
                    const raw = v.ThumbnailUrl ?? v.thumbnailUrl;
                    const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                    return { ...v, ThumbnailUrl: thumb, thumbnailUrl: thumb };
                }));
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