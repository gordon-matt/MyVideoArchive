import { formatDate, formatDuration, formatFileSize, formatNumber } from './utils.js';
import {
    initVideoPageTags,
    saveVideoPageTags,
    fetchVideoSubtitles,
    createVideoDetailsPlayer,
    attachSubtitleTracks,
    bindPlaybackPositionPersistence,
    initVideoExtras,
    loadVideoExtras
} from './video-details-shared.js';
import { buildVideoStreamSource, fetchVideoPlaybackContentType } from './video-player.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.playlists = ko.observableArray([]);
        this.subtitles = [];
        this.watched = ko.observable(false);
        this.loading = ko.observable(true);
        this.retrying = ko.observable(false);
        this.deleting = ko.observable(false);

        // Standalone banner
        this.isStandalone = ko.observable(false);
        this.standaloneInfo = ko.observable({ channelVideoCount: 0, isSubscribed: false });
        this.subscribing = ko.observable(false);

        // Custom playlists
        this.customPlaylists = ko.observableArray([]);
        this.videoInPlaylists = ko.observableArray([]);
        this.selectedPlaylistId = ko.observable('');
        this.addingToPlaylist = ko.observable(false);

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatFileSize = formatFileSize;
        this.formatNumber = formatNumber;

        this._tagifyInstance = null;

        initVideoExtras(this);
    }

    loadVideo = async () => {
        await fetch(`/odata/VideoOData(${this.videoId})?$expand=Channel`)
            .then(response => {
                if (!response.ok) throw new Error('Video not found');
                return response.json();
            })
            .then(async data => {
                this.video(data);

                if (data.Id && data.DownloadedAt) {
                    this._pendingVideoUrl = `/api/videos/${data.Id}/stream`;
                    this._pendingVideoMimeHint = data.FilePath ?? data.filePath ?? null;
                    this._pendingStreamContentType = await fetchVideoPlaybackContentType(data.Id);
                    await this.markWatched();
                }

                await loadVideoExtras(this, this.videoId);
                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading video:', error);
                this.loading(false);
            });
    };

    loadSubtitles = async () => {
        this.subtitles = await fetchVideoSubtitles(this.videoId);
    };

    loadPlaylists = async () => {
        await fetch(`/api/videos/${this.videoId}/playlists`)
            .then(response => response.json())
            .then(data => {
                this.playlists(data.playlists || []);
            })
            .catch(error => {
                console.error('Error loading playlists:', error);
            });
    };

    loadWatchedStatus = async () => {
        await fetch(`/api/user/videos/watched?videoIds=${this.videoId}`)
            .then(response => response.json())
            .then(data => {
                this.watched((data.watchedIds || []).includes(this.videoId));
            })
            .catch(error => {
                console.error('Error loading watched status:', error);
            });
    };

    loadStandaloneInfo = async () => {
        try {
            const response = await fetch(`/api/videos/${this.videoId}/standalone-info`);
            if (!response.ok) return;
            const data = await response.json();
            this.isStandalone(data.isStandalone);
            this.standaloneInfo(data);
        } catch (error) {
            console.error('Error loading standalone info:', error);
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

    initTags = async () => initVideoPageTags(this);

    saveTags = async () => saveVideoPageTags(this, { afterSave: () => this.loadStandaloneInfo() });

    subscribeToChannel = async () => {
        const info = this.standaloneInfo();
        const video = this.video();
        if (!info || !video) return;

        this.subscribing(true);
        try {
            const channel = video.Channel;
            const response = await fetch('/odata/ChannelOData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    Platform: channel.Platform,
                    ChannelId: channel.ChannelId,
                    Name: channel.Name,
                    Url: channel.Url,
                    SubscribedAt: new Date().toISOString()
                })
            });

            if (response.ok) {
                this.isStandalone(false);
                this.standaloneInfo({ ...info, isSubscribed: true });
            } else {
                const data = await response.json().catch(() => ({}));
                toast.error(data.message || 'Failed to subscribe. Please try again.');
            }
        } catch (error) {
            console.error('Error subscribing to channel:', error);
            toast.error('An error occurred while subscribing. Please try again.');
        } finally {
            this.subscribing(false);
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

            const data = await response.json();

            if (response.ok) {
                this.selectedPlaylistId('');
                await this.loadVideoPlaylists();
                toast.success(data.message || 'Video added to playlist.');
            } else {
                toast.error(data.message || 'Failed to add to playlist.');
            }
        } catch (error) {
            console.error('Error adding to playlist:', error);
            toast.error('An error occurred. Please try again.');
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

    markWatched = async () => {
        try {
            await fetch(`/api/user/videos/${this.videoId}/watched`, { method: 'POST' });
            this.watched(true);
        } catch (error) {
            console.error('Error marking video as watched:', error);
        }
    };

    toggleWatched = async () => {
        try {
            if (this.watched()) {
                await fetch(`/api/user/videos/${this.videoId}/watched`, { method: 'DELETE' });
                this.watched(false);
            } else {
                await fetch(`/api/user/videos/${this.videoId}/watched`, { method: 'POST' });
                this.watched(true);
            }
        } catch (error) {
            console.error('Error toggling watched status:', error);
        }
    };

    retryMetadata = async () => {
        this.retrying(true);
        try {
            const response = await fetch(`/api/admin/videos/${this.videoId}/retry-metadata`, { method: 'POST' });
            const data = await response.json();
            if (data.success) {
                await this.loadVideo();
            } else {
                toast.warning(data.message || 'Metadata still unavailable from the platform. Please try again later.');
            }
        } catch (error) {
            console.error('Error retrying metadata:', error);
            toast.error('An error occurred while retrying. Please try again.');
        } finally {
            this.retrying(false);
        }
    };

    deleteVideoFile = async () => {
        const video = this.video();
        if (!video) return;

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
            console.error('Error deleting video file from details:', error);
            toast.error('Error deleting video file. Please try again.');
        } finally {
            this.deleting(false);
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);

    // Load video data first so the container is visible before Video.js measures it
    await viewModel.loadVideo();

    // Fetch subtitle list before initialising the player so the tracks appear in
    // the captions menu the first time the user opens it.
    if (viewModel._pendingVideoUrl) {
        await viewModel.loadSubtitles();
    }

    if (viewModel._pendingVideoUrl) {
        const mimeHint = viewModel._pendingStreamContentType ?? viewModel._pendingVideoMimeHint;
        player = createVideoDetailsPlayer('videoPlayer', mimeHint);
        bindPlaybackPositionPersistence(player, videoId);
        player.ready(() => {
            player.src(buildVideoStreamSource(videoId, mimeHint));
            attachSubtitleTracks(player, viewModel.subtitles);
        });
    }

    await Promise.all([
        viewModel.loadPlaylists(),
        viewModel.loadWatchedStatus(),
        viewModel.loadStandaloneInfo(),
        viewModel.loadCustomPlaylists(),
        viewModel.loadVideoPlaylists(),
        viewModel.initTags()
    ]);
});
