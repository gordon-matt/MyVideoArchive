import { formatDate, formatDuration } from './utils.js';

const PAGE_SIZE = 60;

class VideoIndexViewModel {
    constructor() {
        this.videos = ko.observableArray([]);
        this.channels = ko.observableArray([]);
        this.loading = ko.observable(true);

        // Filters
        this.searchQuery = ko.observable('');
        this.selectedChannelId = ko.observable('');
        this.tagFilter = ko.observable('');

        // Paging
        this.currentPage = ko.observable(1);
        this.totalPages = ko.observable(0);
        this.totalCount = ko.observable(0);
        this.pageNumbers = ko.computed(() => this._buildPageNumbers());

        // Add video modal
        this.newVideoUrl = ko.observable('');
        this.addingVideo = ko.observable(false);
        this.addVideoError = ko.observable('');

        // Format helpers
        this.formatDate = formatDate;
        this.formatDuration = formatDuration;

        // Debounced search
        let searchTimeout = null;
        this.searchQuery.subscribe(() => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                this.currentPage(1);
                this.loadVideos();
            }, 350);
        });

        this.selectedChannelId.subscribe(() => {
            this.currentPage(1);
            this.loadVideos();
        });

        this._tagifyInstance = null;
    }

    _buildPageNumbers() {
        const total = this.totalPages();
        const current = this.currentPage();
        if (total <= 1) return [];
        const delta = 2;
        const pages = [];
        for (let i = Math.max(1, current - delta); i <= Math.min(total, current + delta); i++) {
            pages.push(i);
        }
        return pages;
    }

    initTagFilter = async () => {
        const input = document.getElementById('tagFilterInput');
        if (!input || this._tagifyInstance) return;

        let whitelist = [];
        try {
            const response = await fetch('/api/tags');
            const data = await response.json();
            whitelist = (data.tags || []).map(t => t.name);
        } catch (e) {
            console.error('Error loading tag whitelist', e);
        }

        this._tagifyInstance = new Tagify(input, {
            whitelist,
            enforceWhitelist: false,
            dropdown: { enabled: 1, maxItems: 20, classname: 'tags-look' }
        });

        this._tagifyInstance.on('change', () => {
            const tags = this._tagifyInstance.value.map(t => t.value);
            this.tagFilter(tags.join(','));
            this.currentPage(1);
            this.loadVideos();
        });
    };

    loadVideos = async () => {
        this.loading(true);
        try {
            const params = new URLSearchParams({
                page: this.currentPage(),
                pageSize: PAGE_SIZE
            });
            if (this.searchQuery()) params.set('search', this.searchQuery());
            if (this.selectedChannelId()) params.set('channelId', this.selectedChannelId());
            if (this.tagFilter()) params.set('tagFilter', this.tagFilter());

            const response = await fetch(`/api/videos/search?${params}`);
            const data = await response.json();

            this.videos(data.videos || []);
            this.totalCount(data.pagination?.totalCount ?? 0);
            this.totalPages(data.pagination?.totalPages ?? 0);
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loading(false);
        }
    };

    loadChannels = async () => {
        try {
            const response = await fetch('/api/videos/channels');
            const data = await response.json();
            this.channels(data.channels || []);
        } catch (error) {
            console.error('Error loading channels:', error);
        }
    };

    clearFilters = () => {
        this.searchQuery('');
        this.selectedChannelId('');
        this.tagFilter('');
        if (this._tagifyInstance) this._tagifyInstance.removeAllTags();
        this.currentPage(1);
        this.loadVideos();
    };

    openVideo = (video) => {
        window.location.href = `/videos/${video.id}`;
    };

    // Paging
    previousPage = () => {
        if (this.currentPage() > 1) {
            this.currentPage(this.currentPage() - 1);
            this.loadVideos();
        }
    };

    nextPage = () => {
        if (this.currentPage() < this.totalPages()) {
            this.currentPage(this.currentPage() + 1);
            this.loadVideos();
        }
    };

    goToPage = (page) => {
        if (page >= 1 && page <= this.totalPages()) {
            this.currentPage(page);
            this.loadVideos();
        }
    };

    // Add standalone video
    addStandaloneVideo = async () => {
        const url = this.newVideoUrl().trim();
        if (!url) return;

        this.addingVideo(true);
        this.addVideoError('');

        try {
            const response = await fetch('/api/videos/standalone', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url })
            });

            const data = await response.json();

            if (!response.ok) {
                this.addVideoError(data.message || 'Failed to add video. Please try again.');
                return;
            }

            this.newVideoUrl('');
            bootstrap.Modal.getInstance(document.getElementById('addVideoModal')).hide();
            this.currentPage(1);
            await this.loadVideos();
        } catch (error) {
            console.error('Error adding standalone video:', error);
            this.addVideoError('An unexpected error occurred. Please try again.');
        } finally {
            this.addingVideo(false);
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new VideoIndexViewModel();
    ko.applyBindings(viewModel);
    await Promise.all([viewModel.loadVideos(), viewModel.loadChannels()]);
    viewModel.initTagFilter();
});
