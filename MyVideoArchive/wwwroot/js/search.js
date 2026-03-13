import { formatDate, formatDuration } from './utils.js';
import { getTagifyOptions } from './tagify-options.js';

class SearchViewModel {
    constructor() {
        this.query = ko.observable('');
        this.queryInput = ko.observable('');
        this.tagFilter = ko.observable('');

        // ── Channels ─────────────────────────────────────────────────────────
        this.channels = ko.observableArray([]);
        this.channelsTotalCount = ko.observable(0);
        this.channelsTotalPages = ko.observable(1);
        this.channelsCurrentPage = ko.observable(1);

        // ── Playlists ─────────────────────────────────────────────────────────
        this.playlists = ko.observableArray([]);
        this.playlistsTotalCount = ko.observable(0);
        this.playlistsTotalPages = ko.observable(1);
        this.playlistsCurrentPage = ko.observable(1);

        // ── Videos ────────────────────────────────────────────────────────────
        this.videos = ko.observableArray([]);
        this.videosTotalCount = ko.observable(0);
        this.videosTotalPages = ko.observable(1);
        this.videosCurrentPage = ko.observable(1);

        this.loading = ko.observable(false);
        this.searched = ko.observable(false);
        this.pageSize = 18;

        this.channelsPageNumbers = ko.computed(() =>
            this._buildPageNumbers(this.channelsCurrentPage(), this.channelsTotalPages()));
        this.playlistsPageNumbers = ko.computed(() =>
            this._buildPageNumbers(this.playlistsCurrentPage(), this.playlistsTotalPages()));
        this.videosPageNumbers = ko.computed(() =>
            this._buildPageNumbers(this.videosCurrentPage(), this.videosTotalPages()));

        this.hasResults = ko.computed(() =>
            this.channels().length > 0 ||
            this.playlists().length > 0 ||
            this.videos().length > 0);

        this.formatDate = formatDate;
        this.formatDuration = formatDuration;

        this._tagifyInstance = null;
    }

    _buildPageNumbers(current, total) {
        const pages = [];
        let start = Math.max(1, current - 2);
        let end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);
        for (let i = start; i <= end; i++) pages.push(i);
        return pages;
    }

    initTagify = (inputEl) => {
        if (!inputEl || this._tagifyInstance) return;

        fetch('/api/tags')
            .then(r => r.json())
            .then(data => {
                const whitelist = (data.tags || []).map(t => t.name);
                this._tagifyInstance = new Tagify(inputEl, getTagifyOptions(whitelist, { maxTags: 1, dropdown: { maxItems: 10 } }));
                this._tagifyInstance.on('change', () => {
                    const tags = this._tagifyInstance.value;
                    this.tagFilter(tags.length > 0 ? tags[0].value : '');
                });
            })
            .catch(() => { });
    };

    search = async () => {
        this.query(this.queryInput());
        this.channelsCurrentPage(1);
        this.playlistsCurrentPage(1);
        this.videosCurrentPage(1);
        await this._doSearch();
    };

    clearSearch = async () => {
        this.queryInput('');
        this.query('');
        this.tagFilter('');
        if (this._tagifyInstance) {
            this._tagifyInstance.removeAllTags();
        }
        this.channels([]);
        this.playlists([]);
        this.videos([]);
        this.searched(false);
    };

    _doSearch = async () => {
        const q = this.query();
        const tag = this.tagFilter();

        if (!q && !tag) {
            this.channels([]);
            this.playlists([]);
            this.videos([]);
            this.searched(false);
            return;
        }

        this.loading(true);
        try {
            const params = new URLSearchParams({
                channelPage: this.channelsCurrentPage(),
                playlistPage: this.playlistsCurrentPage(),
                videoPage: this.videosCurrentPage(),
                pageSize: this.pageSize
            });
            if (q) params.set('q', q);
            if (tag) params.set('tag', tag);

            const response = await fetch(`/api/search?${params}`);
            if (!response.ok) throw new Error('Search failed');

            const data = await response.json();

            this.channels(data.channels.items || []);
            this.channelsTotalCount(data.channels.totalCount || 0);
            this.channelsTotalPages(data.channels.totalPages || 1);

            this.playlists(data.playlists.items || []);
            this.playlistsTotalCount(data.playlists.totalCount || 0);
            this.playlistsTotalPages(data.playlists.totalPages || 1);

            this.videos(data.videos.items || []);
            this.videosTotalCount(data.videos.totalCount || 0);
            this.videosTotalPages(data.videos.totalPages || 1);

            this.searched(true);
        } catch (error) {
            console.error('Search error:', error);
            toast.error('Search failed. Please try again.');
        } finally {
            this.loading(false);
        }
    };

    // ── Channel pagination ────────────────────────────────────────────────────
    channelsGoToPage = async (page) => {
        if (page >= 1 && page <= this.channelsTotalPages()) {
            this.channelsCurrentPage(page);
            await this._doSearch();
        }
    };
    channelsPreviousPage = async () => {
        if (this.channelsCurrentPage() > 1) {
            this.channelsCurrentPage(this.channelsCurrentPage() - 1);
            await this._doSearch();
        }
    };
    channelsNextPage = async () => {
        if (this.channelsCurrentPage() < this.channelsTotalPages()) {
            this.channelsCurrentPage(this.channelsCurrentPage() + 1);
            await this._doSearch();
        }
    };

    // ── Playlist pagination ───────────────────────────────────────────────────
    playlistsGoToPage = async (page) => {
        if (page >= 1 && page <= this.playlistsTotalPages()) {
            this.playlistsCurrentPage(page);
            await this._doSearch();
        }
    };
    playlistsPreviousPage = async () => {
        if (this.playlistsCurrentPage() > 1) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() - 1);
            await this._doSearch();
        }
    };
    playlistsNextPage = async () => {
        if (this.playlistsCurrentPage() < this.playlistsTotalPages()) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() + 1);
            await this._doSearch();
        }
    };

    // ── Video pagination ──────────────────────────────────────────────────────
    videosGoToPage = async (page) => {
        if (page >= 1 && page <= this.videosTotalPages()) {
            this.videosCurrentPage(page);
            await this._doSearch();
        }
    };
    videosPreviousPage = async () => {
        if (this.videosCurrentPage() > 1) {
            this.videosCurrentPage(this.videosCurrentPage() - 1);
            await this._doSearch();
        }
    };
    videosNextPage = async () => {
        if (this.videosCurrentPage() < this.videosTotalPages()) {
            this.videosCurrentPage(this.videosCurrentPage() + 1);
            await this._doSearch();
        }
    };

    // ── Navigation ────────────────────────────────────────────────────────────
    viewChannel = (channel) => {
        window.location.href = `/channels/${channel.id}`;
    };

    viewPlaylist = (playlist) => {
        if (playlist.isCustom) {
            window.location.href = `/my-playlists/${playlist.id}`;
        } else {
            window.location.href = `/playlists/${playlist.id}`;
        }
    };

    viewVideo = (video) => {
        window.location.href = `/videos/${video.id}`;
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', () => {
    viewModel = new SearchViewModel();
    ko.applyBindings(viewModel);

    const tagInput = document.getElementById('searchTagInput');
    if (tagInput) {
        viewModel.initTagify(tagInput);
    }

    document.getElementById('searchQueryInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.search();
    });
});
