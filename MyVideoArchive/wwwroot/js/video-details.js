import { formatDate, formatDuration, formatFileSize, formatNumber } from './utils.js';

/** @type {import('video.js').VideoJsPlayer | null} */
let player = null;

const STORAGE_KEY_RATE = 'mva-playback-rate';

// ─── Playback position persistence ────────────────────────────────────────────
function savePosition(vid, time) {
    if (time > 5) localStorage.setItem(`mva-pos-${vid}`, Math.floor(time));
}
function loadSavedPosition(vid) {
    return parseInt(localStorage.getItem(`mva-pos-${vid}`) || '0', 10);
}
function clearPosition(vid) {
    localStorage.removeItem(`mva-pos-${vid}`);
}

class VideoPlayerViewModel {
    constructor(videoId) {
        this.videoId = videoId;
        this.video = ko.observable(null);
        this.playlists = ko.observableArray([]);
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

                if (data.Id && data.DownloadedAt) {
                    this._pendingVideoUrl = `/api/videos/${data.Id}/stream`;
                    await this.markWatched();
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
            const tagsResponse = await fetch('/api/tags');
            const tagsData = await tagsResponse.json();
            const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

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

            if (currentTags.length > 0) {
                this._tagifyInstance.addTags(currentTags);
            }

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
            alert('Unable to delete this video file.');
            return;
        }

        this.deleting(true);
        try {
            const response = await fetch(`/api/channels/${channelId}/videos/${id}/file`, {
                method: 'DELETE'
            });

            const data = await response.json().catch(() => ({}));

            if (response.ok) {
                alert(data.message || 'Video file deleted successfully.');
                window.location.href = `/channels/${channelId}`;
            } else {
                alert(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file from details:', error);
            alert('Error deleting video file. Please try again.');
        } finally {
            this.deleting(false);
        }
    };
}

var viewModel;

function initVideoJsPlayer() {
    player = videojs('videoPlayer', {
        controls: true,
        fluid: true,
        aspectRatio: '16:9',
        playbackRates: [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5],
        controlBar: {
            skipButtons: {
                forward: 10,
                backward: 10
            }
        },
        userActions: {
            hotkeys: true
        }
    });

    // Restore saved playback rate
    player.ready(() => {
        const savedRate = parseFloat(localStorage.getItem(STORAGE_KEY_RATE) || '1');
        player.playbackRate(savedRate);
    });

    // Persist playback rate across videos
    player.on('ratechange', () => {
        localStorage.setItem(STORAGE_KEY_RATE, player.playbackRate());
    });

    return player;
}

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new VideoPlayerViewModel(videoId);
    ko.applyBindings(viewModel);

    // Load video data first so the container is visible before Video.js measures it
    await viewModel.loadVideo();

    initVideoJsPlayer();

    // Set the video source once player is ready
    if (viewModel._pendingVideoUrl) {
        player.src({ type: 'video/mp4', src: viewModel._pendingVideoUrl });
    }

    // Restore saved playback position after metadata is available
    player.on('loadedmetadata', () => {
        const saved = loadSavedPosition(videoId);
        if (saved > 5 && isFinite(player.duration()) && saved < player.duration() - 5) {
            player.currentTime(saved);
        }
    });

    // Save position every 2 seconds while playing
    let _lastSave = 0;
    player.on('timeupdate', () => {
        const now = Date.now();
        if (now - _lastSave >= 2000) {
            _lastSave = now;
            savePosition(videoId, player.currentTime());
        }
    });

    // Clear saved position when the video ends (user finished watching)
    player.on('ended', () => clearPosition(videoId));

    await Promise.all([
        viewModel.loadPlaylists(),
        viewModel.loadWatchedStatus(),
        viewModel.loadStandaloneInfo(),
        viewModel.loadCustomPlaylists(),
        viewModel.loadVideoPlaylists(),
        viewModel.initTags()
    ]);
});
