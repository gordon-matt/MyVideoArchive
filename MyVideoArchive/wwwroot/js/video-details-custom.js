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
        this.editFilePath = ko.observable('');

        // Playlist checkboxes
        this.channelPlaylists = ko.observableArray([]);  // [{ Id, Name }]
        this.editPlaylistIds = ko.observableArray([]);   // currently selected playlist IDs

        // Thumbnail upload
        this.thumbnailFile = ko.observable(null);
        this.thumbnailCacheBust = ko.observable(Date.now());

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
            if (data.FilePath) this.videoUrl(`/api/videos/${data.Id}/stream`);
            this.populateEditForm(data);

            // Load channel playlists and current memberships in parallel
            if (data.ChannelId) {
                const [playlistsRes, membershipRes] = await Promise.all([
                    fetch(`/api/custom/channels/${data.ChannelId}/playlists`),
                    fetch(`/api/custom/videos/${this.videoId}/playlists`)
                ]);

                if (playlistsRes.ok) {
                    this.channelPlaylists(await playlistsRes.json());
                }

                if (membershipRes.ok) {
                    this.editPlaylistIds(await membershipRes.json());
                }
            }
        } catch (error) {
            console.error('Error loading video:', error);
        } finally {
            this.loading(false);
        }
    };

    populateEditForm = (video) => {
        this.editTitle(video.Title || '');
        this.editDescription(video.Description || '');
        this.editFilePath(video.FilePath || '');
        this.thumbnailFile(null);
        // editPlaylistIds is populated separately after fetching memberships from the API

        if (video.UploadDate) {
            const d = new Date(video.UploadDate);
            this.editUploadDate(d.toISOString().split('T')[0]);
        } else {
            this.editUploadDate('');
        }

        this.editDuration(video.Duration ? this.durationToHms(video.Duration) : '');
    };

    durationToHms = (duration) => {
        if (!duration) return '';
        if (typeof duration === 'string' && duration.startsWith('P')) {
            const m = duration.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
            if (m) {
                const h = (m[1] || 0).toString().padStart(2, '0');
                const min = (m[2] || 0).toString().padStart(2, '0');
                const s = (m[3] || 0).toString().padStart(2, '0');
                return `${h}:${min}:${s}`;
            }
        }
        return duration;
    };

    hmsToDuration = (hms) => {
        if (!hms) return null;
        const parts = hms.split(':').map(Number);
        if (parts.length === 3) return `PT${parts[0]}H${parts[1]}M${parts[2]}S`;
        return null;
    };

    toggleEditForm = () => this.showEditForm(!this.showEditForm());

    resetEditForm = () => {
        const v = this.video();
        if (v) this.populateEditForm(v);
        this.showEditForm(false);
    };

    saveMetadata = async () => {
        try {
            const metaResponse = await fetch(`/api/custom/videos/${this.videoId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title: this.editTitle(),
                    description: this.editDescription() || null,
                    thumbnailUrl: this.video()?.ThumbnailUrl ?? null, // preserve existing
                    uploadDate: this.editUploadDate() ? new Date(this.editUploadDate()).toISOString() : null,
                    duration: this.hmsToDuration(this.editDuration()),
                    filePath: this.editFilePath() || null,
                    playlistIds: this.editPlaylistIds()
                })
            });

            if (!metaResponse.ok) {
                alert('Failed to save metadata. Please try again.');
                return;
            }

            // If a thumbnail file was chosen, upload it now
            if (this.thumbnailFile()) {
                await this._uploadThumbnail();
            } else {
                await this.loadVideo();
            }

            this.showEditForm(false);
        } catch (error) {
            console.error('Error saving metadata:', error);
            alert('Failed to save metadata.');
        }
    };

    // ── Thumbnail helpers ─────────────────────────────────────────────────────

    onThumbnailDragOver = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        return true;
    };

    onThumbnailDrop = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this.thumbnailFile(file);
        return true;
    };

    triggerThumbnailInput = () => {
        document.getElementById('videoThumbnailInput').click();
    };

    onThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this.thumbnailFile(file);
        event.target.value = ''; // allow re-selecting same file
    };

    _uploadThumbnail = async () => {
        const file = this.thumbnailFile();
        if (!file) return;

        const form = new FormData();
        form.append('file', file);
        const response = await fetch(`/api/custom/videos/${this.videoId}/thumbnail`, {
            method: 'POST',
            body: form
        });

        if (response.ok) {
            this.thumbnailFile(null);
            this.thumbnailCacheBust(Date.now());
            await this.loadVideo();
        } else {
            alert('Metadata saved, but thumbnail upload failed. Please try again.');
            await this.loadVideo();
        }
    };
}

document.addEventListener('DOMContentLoaded', async () => {
    window.viewModel = new CustomVideoViewModel(videoId);
    ko.applyBindings(window.viewModel);
    await window.viewModel.loadVideo();
});
