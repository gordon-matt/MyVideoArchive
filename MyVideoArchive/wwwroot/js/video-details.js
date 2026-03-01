import { formatDate, formatDuration, formatFileSize, formatNumber } from './utils.js';

class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.playlists = ko.observableArray([]);
        this.watched = ko.observable(false);
        this.loading = ko.observable(true);
        this.retrying = ko.observable(false);
        this.videoUrl = ko.observable(null);

        // Standalone banner
        this.isStandalone = ko.observable(false);
        this.standaloneInfo = ko.observable({ channelVideoCount: 0, isSubscribed: false });
        this.subscribing = ko.observable(false);

        // Custom playlists
        this.customPlaylists = ko.observableArray([]);
        this.videoInPlaylists = ko.observableArray([]);
        this.selectedPlaylistId = ko.observable('');
        this.addingToPlaylist = ko.observable(false);
        this.addToPlaylistMessage = ko.observable('');

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatFileSize = formatFileSize;
        this.formatNumber = formatNumber;

        this._tagifyInstance = null;
    }

    loadVideo = async () => {
        await fetch(`/odata/VideoOData(${this.videoId})?$expand=Channel`)
            .then(response => {
                if (!response.ok) throw new Error('Video not found');
                return response.json();
            })
            .then(async data => {
                this.video(data);

                if (data.Id) {
                    this.videoUrl(`/api/videos/${data.Id}/stream`);
                    if (data.DownloadedAt) {
                        await this.markWatched();
                    }
                }

                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading video:', error);
                this.loading(false);
            });
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

    initTags = async () => {
        try {
            // Load all user tags for autocomplete
            const tagsResponse = await fetch('/api/tags');
            const tagsData = await tagsResponse.json();
            const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

            // Load current video tags
            const videoTagsResponse = await fetch(`/api/videos/${this.videoId}/tags`);
            const videoTagsData = await videoTagsResponse.json();
            const currentTags = (videoTagsData.tags || []).map(t => t.name ?? t.Name);

            const input = document.getElementById('videoTagsInput');
            if (!input) return;

            this._tagifyInstance = new Tagify(input, {
                whitelist: allTagNames,
                enforceWhitelist: false,
                maxTags: 20,
                dropdown: {
                    maxItems: 20,
                    classname: 'tags-look',
                    enabled: 1,
                    closeOnSelect: false
                }
            });

            // Set existing tags
            if (currentTags.length > 0) {
                this._tagifyInstance.addTags(currentTags);
            }

            // Auto-save on change (debounced)
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

            // Refresh standalone info in case "standalone" tag was manually removed
            await this.loadStandaloneInfo();
        } catch (error) {
            console.error('Error saving tags:', error);
        }
    };

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
                // Server will have removed the standalone tags; refresh UI
                this.isStandalone(false);
                this.standaloneInfo({ ...info, isSubscribed: true });
            } else {
                const data = await response.json().catch(() => ({}));
                alert(data.message || 'Failed to subscribe. Please try again.');
            }
        } catch (error) {
            console.error('Error subscribing to channel:', error);
            alert('An error occurred while subscribing. Please try again.');
        } finally {
            this.subscribing(false);
        }
    };

    addToPlaylist = async () => {
        const playlistId = this.selectedPlaylistId();
        if (!playlistId) return;

        this.addingToPlaylist(true);
        this.addToPlaylistMessage('');

        try {
            const response = await fetch(`/api/custom-playlists/${playlistId}/videos/${this.videoId}`, {
                method: 'POST'
            });

            const data = await response.json();

            if (response.ok) {
                this.selectedPlaylistId('');
                await this.loadVideoPlaylists();
            } else {
                this.addToPlaylistMessage(data.message || 'Failed to add to playlist.');
                setTimeout(() => this.addToPlaylistMessage(''), 3000);
            }
        } catch (error) {
            console.error('Error adding to playlist:', error);
            this.addToPlaylistMessage('An error occurred. Please try again.');
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

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);
    await viewModel.loadVideo();
    await Promise.all([
        viewModel.loadPlaylists(),
        viewModel.loadWatchedStatus(),
        viewModel.loadStandaloneInfo(),
        viewModel.loadCustomPlaylists(),
        viewModel.loadVideoPlaylists(),
        viewModel.initTags()
    ]);
});
