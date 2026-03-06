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

        // Channel playlist checkboxes (used in Playlists section)
        this.channelPlaylists = ko.observableArray([]);
        this.editPlaylistIds = ko.observableArray([]);

        // My custom playlists
        this.customPlaylists = ko.observableArray([]);
        this.videoInPlaylists = ko.observableArray([]);
        this.selectedPlaylistId = ko.observable('');
        this.addingToPlaylist = ko.observable(false);

        // Thumbnail upload
        this.thumbnailFile = ko.observable(null);
        this.thumbnailCacheBust = ko.observable(Date.now());
        this.deleting = ko.observable(false);

        this.hasFile = ko.computed(() => {
            const v = this.video();
            return v && v.FilePath && v.FilePath.length > 0;
        });

        this._tagifyInstance = null;

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

            if (data.ChannelId) {
                const [playlistsRes, membershipRes] = await Promise.all([
                    fetch(`/api/custom/channels/${data.ChannelId}/playlists`),
                    fetch(`/api/custom/videos/${this.videoId}/playlists`)
                ]);

                if (playlistsRes.ok) this.channelPlaylists(await playlistsRes.json());
                if (membershipRes.ok) this.editPlaylistIds(await membershipRes.json());
            }
        } catch (error) {
            console.error('Error loading video:', error);
        } finally {
            this.loading(false);
        }
    };

    loadCustomPlaylists = async () => {
        try {
            const response = await fetch('/api/custom-playlists?pageSize=100');
            const data = await response.json();
            this.customPlaylists(data.playlists || []);
        } catch (error) {
            console.error('Error loading custom playlists:', error);
        }
    };

    loadVideoPlaylists = async () => {
        try {
            const response = await fetch(`/api/custom-playlists/for-video/${this.videoId}`);
            const data = await response.json();
            this.videoInPlaylists(data.playlists || []);
        } catch (error) {
            console.error('Error loading video playlists:', error);
        }
    };

    initTags = async () => {
        try {
            const [tagsRes, videoTagsRes] = await Promise.all([
                fetch('/api/tags'),
                fetch(`/api/videos/${this.videoId}/tags`)
            ]);

            const allTagNames = tagsRes.ok
                ? (await tagsRes.json()).tags?.map(t => t.name) ?? []
                : [];
            const currentTags = videoTagsRes.ok
                ? (await videoTagsRes.json()).tags?.map(t => t.name) ?? []
                : [];

            const input = document.getElementById('videoTagsInput');
            if (!input) return;

            this._tagifyInstance = new Tagify(input, {
                whitelist: allTagNames,
                enforceWhitelist: false,
                maxTags: 20,
                dropdown: { maxItems: 20, classname: 'tags-look', enabled: 1, closeOnSelect: false }
            });

            if (currentTags.length > 0) this._tagifyInstance.addTags(currentTags);

            let saveTimeout = null;
            this._tagifyInstance.on('change', () => {
                clearTimeout(saveTimeout);
                saveTimeout = setTimeout(() => this.saveTags(), 600);
            });
        } catch (error) {
            console.error('Error initialising tags:', error);
        }
    };

    saveTags = async () => {
        if (!this._tagifyInstance) return;
        const tagNames = this._tagifyInstance.value.map(t => t.value);
        try {
            await fetch(`/api/videos/${this.videoId}/tags`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tagNames })
            });
        } catch (error) {
            console.error('Error saving tags:', error);
        }
    };

    populateEditForm = (video) => {
        this.editTitle(video.Title || '');
        this.editDescription(video.Description || '');
        this.editFilePath(video.FilePath || '');
        this.thumbnailFile(null);

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
                    thumbnailUrl: this.video()?.ThumbnailUrl ?? null,
                    uploadDate: this.editUploadDate() ? new Date(this.editUploadDate()).toISOString() : null,
                    duration: this.hmsToDuration(this.editDuration()),
                    filePath: this.editFilePath() || null,
                    playlistIds: this.editPlaylistIds()
                })
            });

            if (!metaResponse.ok) {
                toast.error('Failed to save metadata. Please try again.');
                return;
            }

            if (this.thumbnailFile()) {
                await this._uploadThumbnail();
            } else {
                await this.loadVideo();
            }

            this.showEditForm(false);
        } catch (error) {
            console.error('Error saving metadata:', error);
            toast.error('Failed to save metadata.');
        }
    };

    saveChannelPlaylistMemberships = async () => {
        try {
            await fetch(`/api/custom/videos/${this.videoId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title: this.editTitle() || this.video()?.Title,
                    description: this.editDescription() || this.video()?.Description || null,
                    thumbnailUrl: this.video()?.ThumbnailUrl ?? null,
                    uploadDate: this.video()?.UploadDate ?? null,
                    duration: this.video()?.Duration ?? null,
                    filePath: this.editFilePath() || this.video()?.FilePath || null,
                    playlistIds: this.editPlaylistIds()
                })
            });
        } catch (error) {
            console.error('Error saving playlist memberships:', error);
        }
    };

    deleteVideoFile = async () => {
        const video = this.video();
        if (!video) {
            return;
        }

        const title = video.Title || 'this video';
        if (!confirm(`Delete the file for "${title}"? This cannot be undone. The video will be marked as ignored.`)) {
            return;
        }

        const channelId = video.ChannelId;
        const id = video.Id || this.videoId;
        if (!channelId || !id) {
            console.error('Missing channel or video ID for delete', video);
            toast.error('Unable to delete this video file.');
            return;
        }

        this.deleting(true);
        try {
            const response = await fetch(`/api/channels/${channelId}/videos/${id}/file`, {
                method: 'DELETE'
            });

            const data = await response.json().catch(() => ({}));

            if (response.ok) {
                toast.success(data.message || 'Video file deleted successfully.');
                window.location.href = `/channels/${channelId}`;
            } else {
                toast.error(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file from custom details:', error);
            toast.error('Error deleting video file. Please try again.');
        } finally {
            this.deleting(false);
        }
    };

    addToPlaylist = async () => {
        const playlistId = this.selectedPlaylistId();
        if (!playlistId) return;

        this.addingToPlaylist(true);
        try {
            const response = await fetch(`/api/custom-playlists/${playlistId}/videos/${this.videoId}`, {
                method: 'POST'
            });

            if (response.ok) {
                this.selectedPlaylistId('');
                await this.loadVideoPlaylists();
            }
        } catch (error) {
            console.error('Error adding to playlist:', error);
        } finally {
            this.addingToPlaylist(false);
        }
    };

    removeFromPlaylist = async (playlist) => {
        try {
            const response = await fetch(`/api/custom-playlists/${playlist.id}/videos/${this.videoId}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                this.videoInPlaylists.remove(playlist);
            }
        } catch (error) {
            console.error('Error removing from playlist:', error);
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
        event.target.value = '';
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
            toast.warning('Metadata saved, but thumbnail upload failed. Please try again.');
            await this.loadVideo();
        }
    };
}

document.addEventListener('DOMContentLoaded', async () => {
    window.viewModel = new CustomVideoViewModel(videoId);
    ko.applyBindings(window.viewModel);
    await window.viewModel.loadVideo();
    await Promise.all([
        window.viewModel.loadCustomPlaylists(),
        window.viewModel.loadVideoPlaylists(),
        window.viewModel.initTags()
    ]);
});
