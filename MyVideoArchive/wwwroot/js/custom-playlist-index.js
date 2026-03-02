import { formatDate, formatSeconds } from './utils.js';

const PAGE_SIZE = 60;

class CustomPlaylistIndexViewModel {
    constructor() {
        this.playlists = ko.observableArray([]);
        this.loading = ko.observable(true);

        // Paging
        this.currentPage = ko.observable(1);
        this.totalPages = ko.observable(0);
        this.totalCount = ko.observable(0);
        this.pageNumbers = ko.computed(() => this._buildPageNumbers());

        // New playlist form
        this.newPlaylistName = ko.observable('');
        this.newPlaylistDescription = ko.observable('');
        this.newPlaylistThumbnailFile = ko.observable(null);
        this.newPlaylistThumbnailPreview = ko.observable(null);

        // Edit playlist form
        this.editingPlaylist = ko.observable(null);
        this.editPlaylistName = ko.observable('');
        this.editPlaylistDescription = ko.observable('');
        this.editPlaylistThumbnailFile = ko.observable(null);
        this.editPlaylistThumbnailPreview = ko.observable(null);

        // Clone playlist — state machine: 'input' | 'fetching' | 'select' | 'cloning' | 'success' | 'error'
        this.cloneUrl = ko.observable('');
        this.cloneState = ko.observable('input');
        this.cloneError = ko.observable('');

        // Preview data (populated after fetch)
        this.clonePreviewName = ko.observable('');
        this.clonePreviewDescription = ko.observable('');
        this.clonePreviewThumbnail = ko.observable('');
        this.clonePreviewVideos = ko.observableArray([]); // each item: { videoId, title, thumbnailUrl, durationSeconds (observable), channelName, isInLibrary, selected (observable) }
        this.cloneSelectedCount = ko.computed(() =>
            this.clonePreviewVideos().filter(v => v.selected()).length
        );

        // Result data (populated after clone)
        this.cloneResultId = ko.observable(0);
        this.cloneResultName = ko.observable('');
        this.cloneResultTotal = ko.observable(0);
        this.cloneResultNew = ko.observable(0);
        this.cloneResultExisting = ko.observable(0);

        this.formatDate = formatDate;
        this.formatDuration = formatSeconds;
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

    loadPlaylists = async () => {
        this.loading(true);
        try {
            const params = new URLSearchParams({ page: this.currentPage(), pageSize: PAGE_SIZE });
            const response = await fetch(`/api/custom-playlists?${params}`);
            const data = await response.json();
            this.playlists(data.playlists || []);
            this.totalCount(data.pagination?.totalCount ?? 0);
            this.totalPages(data.pagination?.totalPages ?? 0);
        } catch (error) {
            console.error('Error loading playlists:', error);
        } finally {
            this.loading(false);
        }
    };

    openPlaylist = (playlist) => {
        window.location.href = `/my-playlists/${playlist.id}`;
    };

    createPlaylist = async () => {
        const name = this.newPlaylistName().trim();
        if (!name) return;

        try {
            const response = await fetch('/api/custom-playlists', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    description: this.newPlaylistDescription().trim() || null
                })
            });

            if (!response.ok) {
                alert('Failed to create playlist. Please try again.');
                return;
            }

            const newPlaylist = await response.json();

            // Upload thumbnail if one was selected
            if (this.newPlaylistThumbnailFile()) {
                await this._uploadThumbnail(newPlaylist.id, this.newPlaylistThumbnailFile());
            }

            this.newPlaylistName('');
            this.newPlaylistDescription('');
            this.newPlaylistThumbnailFile(null);
            this.newPlaylistThumbnailPreview(null);
            bootstrap.Modal.getInstance(document.getElementById('createPlaylistModal')).hide();
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error creating playlist:', error);
            alert('An error occurred. Please try again.');
        }
    };

    deletePlaylist = async (playlist) => {
        if (!confirm(`Delete playlist "${playlist.name}"? This cannot be undone.`)) return;

        try {
            const response = await fetch(`/api/custom-playlists/${playlist.id}`, { method: 'DELETE' });
            if (response.ok) {
                this.playlists.remove(playlist);
                this.totalCount(this.totalCount() - 1);
            } else {
                alert('Failed to delete playlist.');
            }
        } catch (error) {
            console.error('Error deleting playlist:', error);
            alert('An error occurred. Please try again.');
        }
    };

    // ── Edit playlist ─────────────────────────────────────────────────────────

    openEditPlaylist = (playlist) => {
        this.editingPlaylist(playlist);
        this.editPlaylistName(playlist.name);
        this.editPlaylistDescription(playlist.description || '');
        this.editPlaylistThumbnailFile(null);
        this.editPlaylistThumbnailPreview(playlist.thumbnailUrl || null);
        const modal = new bootstrap.Modal(document.getElementById('editPlaylistModal'));
        modal.show();
    };

    saveEditPlaylist = async () => {
        const playlist = this.editingPlaylist();
        if (!playlist) return;

        const name = this.editPlaylistName().trim();
        if (!name) return;

        try {
            const response = await fetch(`/api/custom-playlists/${playlist.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    description: this.editPlaylistDescription().trim() || null
                })
            });

            if (!response.ok) {
                alert('Failed to update playlist. Please try again.');
                return;
            }

            if (this.editPlaylistThumbnailFile()) {
                await this._uploadThumbnail(playlist.id, this.editPlaylistThumbnailFile());
            }

            bootstrap.Modal.getInstance(document.getElementById('editPlaylistModal')).hide();
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error updating playlist:', error);
            alert('An error occurred. Please try again.');
        }
    };

    // ── Clone playlist ────────────────────────────────────────────────────────

    resetCloneModal = () => {
        this.cloneUrl('');
        this.cloneState('input');
        this.cloneError('');
        this.clonePreviewName('');
        this.clonePreviewDescription('');
        this.clonePreviewThumbnail('');
        this.clonePreviewVideos([]);
        this.cloneResultId(0);
        this.cloneResultName('');
        this.cloneResultTotal(0);
        this.cloneResultNew(0);
        this.cloneResultExisting(0);
    };

    fetchClonePreview = async () => {
        const url = this.cloneUrl().trim();
        if (!url) return;

        this.cloneState('fetching');

        try {
            const response = await fetch('/api/custom-playlists/preview', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url })
            });

            const data = await response.json();

            if (!response.ok) {
                this.cloneError(data.message || 'Failed to fetch playlist. Please check the URL and try again.');
                this.cloneState('error');
                return;
            }

            this.clonePreviewName(data.name || '');
            this.clonePreviewDescription(data.description || '');
            this.clonePreviewThumbnail(data.thumbnailUrl || '');

            const videos = (data.videos || []).map(v => ({
                videoId: v.videoId,
                title: v.title,
                thumbnailUrl: v.thumbnailUrl || null,
                durationSeconds: ko.observable(v.durationSeconds),
                channelName: v.channelName || '',
                isInLibrary: v.isInLibrary,
                selected: ko.observable(true)
            }));
            this.clonePreviewVideos(videos);
            this.cloneState('select');
        } catch (error) {
            console.error('Error fetching playlist preview:', error);
            this.cloneError('An unexpected error occurred. Please try again.');
            this.cloneState('error');
        }
    };

    selectAllVideos = () => this.clonePreviewVideos().forEach(v => v.selected(true));
    deselectAllVideos = () => this.clonePreviewVideos().forEach(v => v.selected(false));

    confirmClone = async () => {
        const selectedIds = this.clonePreviewVideos()
            .filter(v => v.selected())
            .map(v => v.videoId);

        if (selectedIds.length === 0) return;

        this.cloneState('cloning');

        try {
            const response = await fetch('/api/custom-playlists/clone', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url: this.cloneUrl().trim(), selectedVideoIds: selectedIds })
            });

            const data = await response.json();

            if (!response.ok) {
                this.cloneError(data.message || 'Failed to clone playlist. Please try again.');
                this.cloneState('error');
                return;
            }

            this.cloneResultId(data.id);
            this.cloneResultName(data.name);
            this.cloneResultTotal(data.totalVideos);
            this.cloneResultNew(data.newVideos);
            this.cloneResultExisting(data.alreadyInLibrary);
            this.cloneState('success');
            await this.loadPlaylists();
        } catch (error) {
            console.error('Error cloning playlist:', error);
            this.cloneError('An unexpected error occurred. Please try again.');
            this.cloneState('error');
        }
    };

    // ── Thumbnail helpers ─────────────────────────────────────────────────────

    _uploadThumbnail = async (playlistId, file) => {
        const form = new FormData();
        form.append('file', file);
        try {
            await fetch(`/api/custom-playlists/${playlistId}/thumbnail`, { method: 'POST', body: form });
        } catch (e) {
            console.error('Error uploading thumbnail:', e);
        }
    };

    onNewThumbnailDragOver = (data, event) => { event.preventDefault(); return true; };
    onNewThumbnailDrop = (data, event) => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (file?.type.startsWith('image/')) {
            this.newPlaylistThumbnailFile(file);
            this.newPlaylistThumbnailPreview(URL.createObjectURL(file));
        }
        return true;
    };
    triggerNewThumbnailInput = () => document.getElementById('newPlaylistThumbnailInput').click();
    onNewThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) {
            this.newPlaylistThumbnailFile(file);
            this.newPlaylistThumbnailPreview(URL.createObjectURL(file));
        }
        event.target.value = '';
    };

    onEditThumbnailDragOver = (data, event) => { event.preventDefault(); return true; };
    onEditThumbnailDrop = (data, event) => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (file?.type.startsWith('image/')) {
            this.editPlaylistThumbnailFile(file);
            this.editPlaylistThumbnailPreview(URL.createObjectURL(file));
        }
        return true;
    };
    triggerEditThumbnailInput = () => document.getElementById('editPlaylistThumbnailInput').click();
    onEditThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) {
            this.editPlaylistThumbnailFile(file);
            this.editPlaylistThumbnailPreview(URL.createObjectURL(file));
        }
        event.target.value = '';
    };

    // Paging
    previousPage = () => {
        if (this.currentPage() > 1) {
            this.currentPage(this.currentPage() - 1);
            this.loadPlaylists();
        }
    };

    nextPage = () => {
        if (this.currentPage() < this.totalPages()) {
            this.currentPage(this.currentPage() + 1);
            this.loadPlaylists();
        }
    };

    goToPage = (page) => {
        if (page >= 1 && page <= this.totalPages()) {
            this.currentPage(page);
            this.loadPlaylists();
        }
    };
}

var viewModel;

document.addEventListener('DOMContentLoaded', async () => {
    viewModel = new CustomPlaylistIndexViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadPlaylists();
});
