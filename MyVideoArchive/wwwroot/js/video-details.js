import { formatDate, formatDuration, formatFileSize, formatNumber } from './utils.js';

class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.loading = ko.observable(true);
        this.videoUrl = ko.observable(null);

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatFileSize = formatFileSize;
        this.formatNumber = formatNumber;
    }

    loadVideo = async () => {
        await fetch(`/odata/VideoOData(${this.videoId})?$expand=Channel`)
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
                    this.videoUrl(`/api/videos/${data.Id}/stream`);
                }

                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading video:', error);
                this.loading(false);
            });
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);
    await viewModel.loadVideo();
});