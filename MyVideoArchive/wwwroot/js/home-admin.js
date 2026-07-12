import { formatFileSize, formatNumber, formatDate, encodeArchiveUrlForHtml } from './utils.js';

function formatTotalDuration(hours) {
    if (!hours) return '0h';
    if (hours < 24) return `${hours.toFixed(1)}h`;
    const days = hours / 24;
    if (days < 30) return `${days.toFixed(1)}d`;
    const months = days / 30.44;
    return `${months.toFixed(1)}mo`;
}

const PLATFORM_ICONS = {
    YouTube: 'bi-youtube',
    BitChute: 'bi-play-btn-fill',
    Odysee: 'bi-broadcast-pin',
    Rumble: 'bi-collection-play-fill',
    Custom: 'bi-folder-fill'
};

const PLATFORM_BAR_CLASSES = {
    YouTube: 'bg-danger',
    BitChute: 'bg-warning',
    Odysee: 'bg-info',
    Rumble: 'bg-success',
    Custom: 'bg-secondary'
};

class AdminDashboardViewModel {
    constructor() {
        this.loading = ko.observable(true);
        this.error = ko.observable(false);
        this.stats = ko.observable(null);

        this.recentVideos = ko.observableArray([]);
        this.recentVideosLoading = ko.observable(true);

        this.formatFileSize = formatFileSize;
        this.formatNumber = formatNumber;
        this.formatDate = formatDate;

        this.totalDurationText = ko.pureComputed(() => {
            const s = this.stats();
            return s ? formatTotalDuration(s.totalDurationHours) : '0h';
        });

        this.maxPlatformVideoCount = ko.pureComputed(() => {
            const s = this.stats();
            if (!s || !s.channelsByPlatform || s.channelsByPlatform.length === 0) return 1;
            return Math.max(1, ...s.channelsByPlatform.map(p => p.videoCount));
        });

        this.hasAlerts = ko.pureComputed(() => {
            const s = this.stats();
            if (!s) return false;
            return s.failedDownloadsCount > 0 || s.needsMetadataReviewCount > 0 || s.queuedDownloadsCount > 0;
        });
    }

    load = async () => {
        this.loading(true);
        this.error(false);
        try {
            const response = await fetch('/api/admin/dashboard-stats');
            if (!response.ok) throw new Error('Failed to load dashboard statistics');
            const data = await response.json();
            this.stats(data);
        } catch (error) {
            console.error('Error loading admin dashboard stats:', error);
            this.error(true);
        } finally {
            this.loading(false);
        }
    };

    loadRecentVideos = async () => {
        this.recentVideosLoading(true);
        try {
            const response = await fetch('/odata/VideoOData?$filter=DownloadedAt ne null&$orderby=DownloadedAt desc&$top=6&$expand=Channel');
            const data = await response.json();
            this.recentVideos((data.value || []).map(v => {
                const raw = v.ThumbnailUrl ?? v.thumbnailUrl;
                const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                return { ...v, ThumbnailUrl: thumb, thumbnailUrl: thumb };
            }));
        } catch (error) {
            console.error('Error loading recent videos:', error);
        } finally {
            this.recentVideosLoading(false);
        }
    };

    platformIcon = (platform) => PLATFORM_ICONS[platform] || 'bi-broadcast';

    platformBarClass = (platform) => PLATFORM_BAR_CLASSES[platform] || 'bg-primary';
}

document.addEventListener('DOMContentLoaded', async () => {
    const viewModel = new AdminDashboardViewModel();
    ko.applyBindings(viewModel);
    await Promise.all([viewModel.load(), viewModel.loadRecentVideos()]);
});
