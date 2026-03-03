import { formatDate, formatDuration, formatNumber } from './utils.js';

class PlaylistDetailsViewModel {
    constructor(playlistId) {
        this.playlistId = playlistId;
        this.playlist = ko.observable(null);
        this.playlistVideos = ko.observableArray([]);
        this.currentVideo = ko.observable(null);
        this.currentVideoUrl = ko.observable(null);
        this.loading = ko.observable(true);
        this.loadingVideos = ko.observable(false);
        this.useCustomOrder = ko.observable(false);
        this.showHidden = ko.observable(false);
        this.sortableInstance = null;

        this.visibleVideoCount = ko.computed(() =>
            this.playlistVideos().filter(v => !v.isHidden).length
        );

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;
        this.formatNumber = formatNumber;
    }

    loadPlaylist = async () => {
        try {
            // Load order setting and playlist data in parallel
            const [, orderSetting] = await Promise.all([
                this._fetchPlaylist(),
                this._loadOrderSetting()
            ]);

            this.loading(false);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error loading playlist:', error);
            this.loading(false);
        }
    };

    _fetchPlaylist = async () => {
        const response = await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`);
        if (!response.ok) throw new Error('Playlist not found');
        this.playlist(await response.json());
    };

    _loadOrderSetting = async () => {
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/order-setting`);
            if (response.ok) {
                const data = await response.json();
                this.useCustomOrder(data.useCustomOrder);
            }
        } catch (error) {
            console.error('Error loading order setting:', error);
        }
    };

    loadPlaylistVideos = async () => {
        this.loadingVideos(true);

        await fetch(`/odata/PlaylistOData(${this.playlistId})?$expand=Channel`)
            .then(response => response.json())
            .then(playlistData => {
                this.playlist(playlistData);
            })
            .catch(error => {
                console.error('Error loading playlist:', error);
            });

        await fetch(`/api/playlists/${this.playlistId}/videos?useCustomOrder=${this.useCustomOrder()}&showHidden=${this.showHidden()}`)
            .then(response => response.json())
            .then(async data => {
                const videos = (data.videos || []).map(v => {
                    v.watched = false;
                    return v;
                });
                this.playlistVideos(videos);
                this.loadingVideos(false);

                if (videos.length > 0) {
                    try {
                        const watchedResponse = await fetch(`/api/user/videos/watched/by-playlist/${this.playlistId}`);
                        const watchedData = await watchedResponse.json();
                        const watchedSet = new Set(watchedData.watchedIds || []);
                        this.playlistVideos().forEach(v => {
                            v.watched = watchedSet.has(v.id);
                        });
                        this.playlistVideos.valueHasMutated();
                    } catch (e) {
                        console.error('Error loading watched status:', e);
                    }
                }

                setTimeout(() => {
                    this.initializeSortable();
                }, 100);

                if (this.playlistVideos().length > 0 && !this.currentVideo()) {
                    var firstDownloaded = this.playlistVideos().find(function (v) { return v.downloadedAt && !v.isHidden; });
                    if (firstDownloaded) {
                        this.playVideo(firstDownloaded);
                    }
                }
            })
            .catch(error => {
                console.error('Error loading playlist videos:', error);
                this.loadingVideos(false);
            });
    };

    playVideo = async (video) => {
        if (!video.downloadedAt || video.isHidden) {
            return;
        }

        this.currentVideo(video);
        this.currentVideoUrl(`/api/videos/${video.id}/stream`);

        try {
            await fetch(`/api/user/videos/${video.id}/watched`, { method: 'POST' });
            video.watched = true;
        } catch (error) {
            console.error('Error marking video as watched:', error);
        }

        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    downloadVideo = async (video, event) => {
        event.stopPropagation();

        if (video.isQueued) {
            alert('This video is already queued for download.');
            return;
        }

        if (!confirm(`Queue "${video.title}" for download?`)) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${video.channelId}/videos/download`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoIds: [video.id] })
            });

            const data = await response.json();
            alert(data.message);
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error queueing video:', error);
            alert('Error queueing video for download. Please try again.');
        }
    };

    hideVideo = async (video) => {
        try {
            await fetch(`/api/playlists/${this.playlistId}/videos/${video.id}/hidden`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isHidden: true })
            });
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error hiding video:', error);
        }
    };

    unhideVideo = async (video) => {
        try {
            await fetch(`/api/playlists/${this.playlistId}/videos/${video.id}/hidden`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ isHidden: false })
            });
            await this.loadPlaylistVideos();
        } catch (error) {
            console.error('Error unhiding video:', error);
        }
    };

    toggleShowHidden = () => {
        // KO checked binding already toggled the value; reload to apply
        this.loadPlaylistVideos();
        return true;
    };

    toggleOrderMode = () => {
        var isNowCustomOrder = this.useCustomOrder();

        if (!isNowCustomOrder) {
            this.clearCustomOrder();
        } else {
            this.enableCustomOrder();
        }

        return true;
    };

    enableCustomOrder = async () => {
        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/order-setting`);
            const data = await response.json();

            if (!data.hasCustomOrder) {
                await this.saveCustomOrder(true);
            } else {
                await this.loadPlaylistVideos();
            }
        } catch (error) {
            console.error('Error checking custom order:', error);
            await this.saveCustomOrder(true);
        }
    };

    clearCustomOrder = async () => {
        await this.saveCustomOrder(true);
    };

    initializeSortable = () => {
        var container = document.getElementById('videoListContainer');
        if (!container) return;

        if (this.sortableInstance) {
            this.sortableInstance.destroy();
        }

        this.sortableInstance = new Sortable(container, {
            handle: '.drag-handle',
            animation: 150,
            disabled: !this.useCustomOrder(),
            onEnd: async (evt) => {
                var items = container.querySelectorAll('.playlist-video-item');
                var newOrder = [];

                items.forEach((item) => {
                    var videoId = parseInt(item.getAttribute('data-video-id'));
                    var video = this.playlistVideos().find(function (v) { return v.id === videoId; });
                    if (video) {
                        newOrder.push(video);
                    }
                });

                this.playlistVideos(newOrder);
                await this.saveCustomOrder();
            }
        });
    };

    saveCustomOrder = async (reloadAfterSave) => {
        var videoOrders = [];

        if (this.useCustomOrder()) {
            // Only include visible (non-hidden) videos in the order
            videoOrders = this.playlistVideos()
                .filter(v => !v.isHidden)
                .map(function (video, index) {
                    return {
                        videoId: video.id,
                        order: index + 1
                    };
                });
        }

        try {
            const response = await fetch(`/api/playlists/${this.playlistId}/reorder`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    useCustomOrder: this.useCustomOrder(),
                    videoOrders: videoOrders
                })
            });

            if (!response.ok) {
                return response.text().then(text => {
                    throw new Error('Server error: ' + text);
                });
            }

            if (this.sortableInstance) {
                this.sortableInstance.option('disabled', !this.useCustomOrder());
            }

            if (reloadAfterSave) {
                await this.loadPlaylistVideos();
            }
        } catch (error) {
            console.error('Error saving order:', error);
            alert('Error saving custom order: ' + error.message);
        }
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new PlaylistDetailsViewModel(playlistId);
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylist();
});
