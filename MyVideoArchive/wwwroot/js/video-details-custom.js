import { formatDate, formatDuration, formatFileSize } from './utils.js';

class CustomVideoViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.loading = ko.observable(true);
        this.videoUrl = ko.observable(null);
        this.showEditForm = ko.observable(false);

        // Edit form fields
        this.editTitle = ko.observable('');
        this.editDescription = ko.observable('');
        this.editUploadDate = ko.observable('');
        this.editDuration = ko.observable('');
        this.editThumbnailUrl = ko.observable('');
        this.editFilePath = ko.observable('');

        this.hasFile = ko.computed(() => {
            const v = this.video();
            return v && v.FilePath && v.FilePath.length > 0;
        });

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatFileSize = formatFileSize;
    }

    loadVideo = async () => {
        try {
            const response = await fetch(`/odata/VideoOData(${this.videoId})?$expand=Channel`);
            if (!response.ok) throw new Error('Video not found');

            const data = await response.json();
            this.video(data);

            if (data.FilePath) {
                this.videoUrl(`/api/videos/${data.Id}/stream`);
            }

            this.populateEditForm(data);
        } catch (error) {
            console.error('Error loading video:', error);
        } finally {
            this.loading(false);
        }
    };

    populateEditForm = (video) => {
        this.editTitle(video.Title || '');
        this.editDescription(video.Description || '');
        this.editThumbnailUrl(video.ThumbnailUrl || '');
        this.editFilePath(video.FilePath || '');

        if (video.UploadDate) {
            // Format as YYYY-MM-DD for date input
            const d = new Date(video.UploadDate);
            this.editUploadDate(d.toISOString().split('T')[0]);
        } else {
            this.editUploadDate('');
        }

        if (video.Duration) {
            // Duration comes as ISO 8601 duration string (PT1H23M45S) or HH:MM:SS
            this.editDuration(this.durationToHms(video.Duration));
        } else {
            this.editDuration('');
        }
    };

    durationToHms = (duration) => {
        if (!duration) return '';
        // Handle ISO 8601 (PT1H23M45S)
        if (typeof duration === 'string' && duration.startsWith('P')) {
            const match = duration.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
            if (match) {
                const h = (match[1] || 0).toString().padStart(2, '0');
                const m = (match[2] || 0).toString().padStart(2, '0');
                const s = (match[3] || 0).toString().padStart(2, '0');
                return `${h}:${m}:${s}`;
            }
        }
        return duration;
    };

    hmsToDuration = (hms) => {
        if (!hms) return null;
        const parts = hms.split(':').map(Number);
        if (parts.length === 3) {
            return `PT${parts[0]}H${parts[1]}M${parts[2]}S`;
        }
        return null;
    };

    toggleEditForm = () => {
        this.showEditForm(!this.showEditForm());
    };

    resetEditForm = () => {
        const v = this.video();
        if (v) this.populateEditForm(v);
        this.showEditForm(false);
    };

    saveMetadata = async () => {
        try {
            const response = await fetch(`/api/custom/videos/${this.videoId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title: this.editTitle(),
                    description: this.editDescription() || null,
                    thumbnailUrl: this.editThumbnailUrl() || null,
                    uploadDate: this.editUploadDate() ? new Date(this.editUploadDate()).toISOString() : null,
                    duration: this.hmsToDuration(this.editDuration()),
                    filePath: this.editFilePath() || null
                })
            });

            if (response.ok) {
                this.showEditForm(false);
                await this.loadVideo();
            } else {
                alert('Failed to save metadata. Please try again.');
            }
        } catch (error) {
            console.error('Error saving metadata:', error);
            alert('Failed to save metadata.');
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomVideoViewModel(videoId);
    ko.applyBindings(viewModel);
    await viewModel.loadVideo();
});
