import { formatDate, formatDuration, formatFileSize, formatNumber } from './utils.js';

class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.loading = ko.observable(true);
        this.retrying = ko.observable(false);
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

    retryMetadata = async () => {
        this.retrying(true);
        try {
            const response = await fetch(`/api/admin/videos/${this.videoId}/retry-metadata`, { method: 'POST' });
            const data = await response.json();
            if (data.success) {
                await this.loadVideo();
            } else {
                alert(data.message || 'Metadata still unavailable from the platform. Please try again later.');
            }
        } catch (error) {
            console.error('Error retrying metadata:', error);
            alert('An error occurred while retrying. Please try again.');
        } finally {
            this.retrying(false);
        }
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);
    await viewModel.loadVideo();
});