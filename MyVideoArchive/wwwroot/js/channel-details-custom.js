import { formatDate, formatFileSize, encodeArchiveUrlForHtml } from './utils.js';
import { getTagifyOptions } from './tagify-options.js';

class CustomChannelViewModel {
    constructor(channelId) {
        this.channelId = channelId;
        this.isAdmin = window.isAdmin === true;
        this.channel = ko.observable(null);

        // ── Videos tab ───────────────────────────────────────────────────────
        this.videos = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.videosCurrentPage = ko.observable(1);
        this.videosPageSize = 24;
        this.videosTotalPages = ko.observable(1);
        this.videosTotalCount = ko.observable(0);
        this.videosSearch = ko.observable('');
        this.videosSearchInput = ko.observable('');
        this.videoViewMode = ko.observable('list'); // 'list' | 'grid'

        // ── Videos bulk actions ───────────────────────────────────────────────
        this.videosSelectAll = ko.observable(false);
        this.videosBulkDeleting = ko.observable(false);
        this.videosBulkAddingToPlaylist = ko.observable(false);
        this.videosBulkTargetPlaylistId = ko.observable('');

        this.selectedVideos = ko.computed(() => this.videos().filter(v => v._selected && v._selected()));

        this.videosSelectAll.subscribe(val => this.videos().forEach(v => v._selected && v._selected(val)));

        // ── Playlists tab ─────────────────────────────────────────────────────
        this.playlists = ko.observableArray([]);
        this.playlistsLoading = ko.observable(false);
        this.playlistsCurrentPage = ko.observable(1);
        this.playlistsPageSize = 24;
        this.playlistsTotalPages = ko.observable(1);
        this.playlistsTotalCount = ko.observable(0);

        // ── Series tab ────────────────────────────────────────────────────────
        this.seriesList = ko.observableArray([]);
        this.seriesLoading = ko.observable(false);
        this.seriesLoaded = false;
        this.seriesCount = ko.observable(0);
        this.seriesEditId = ko.observable(null);
        this.seriesEditIsNew = ko.observable(true);
        this.seriesEditName = ko.observable('');
        this.seriesEditPlaylistIds = ko.observableArray([]);
        this.seriesAvailablePlaylists = ko.observableArray([]);
        this.seriesPlaylistsLoading = ko.observable(false);
        this.seriesSaving = ko.observable(false);

        // ── Subscribers tab (admin only) ──────────────────────────────────────
        this.subscribers = ko.observableArray([]);
        this.subscribersLoading = ko.observable(false);
        this.subscribersCount = ko.computed(() => this.subscribers().length);

        // ── Additional Content tab ─────────────────────────────────────────────
        this.extrasItems = ko.observableArray([]);
        this.extrasLoading = ko.observable(false);
        this.extrasLoaded = false;

        // Upload state
        this.extrasUploadFiles = ko.observableArray([]);
        this.extrasUploadPlaylistIds = ko.observableArray([]);
        this.extrasUploading = ko.observable(false);

        // Edit state
        this.extrasEditId = ko.observable(null);
        this.extrasEditFileName = ko.observable('');
        this.extrasEditPlaylistIds = ko.observableArray([]);

        // Edit channel form
        this.editName = ko.observable('');
        this.editDescription = ko.observable('');
        this.editBannerUrl = ko.observable('');

        // Channel thumbnail upload (in edit modal)
        this.channelThumbnailPreviewUrl = ko.observable(null);
        this.channelThumbnailFile = ko.observable(null);

        // ── Thumbnail picker (banner / avatar) ────────────────────────────────
        this.thumbnailPickerMode = ko.observable('banner'); // 'banner' | 'avatar'
        this.thumbnailPickerUploadFile = ko.observable(null);
        this.thumbnailPickerUploadPreview = ko.observable(null);
        this.canConfirmThumbnailPicker = ko.computed(() => !!this.thumbnailPickerUploadFile());

        // Create playlist form
        this.newPlaylistName = ko.observable('');
        this.newPlaylistDescription = ko.observable('');

        // Playlist thumbnail upload state
        this.thumbnailTargetPlaylist = ko.observable(null);
        this.thumbnailPreviewUrl = ko.observable(null);
        this.thumbnailFile = ko.observable(null);
        this.uploadingThumbnail = ko.observable(false);
        this.thumbnailCacheBust = ko.observable(Date.now());

        // ── Tags ──────────────────────────────────────────────────────────────
        this._tagifyInstance = null;

        // ── Computed page numbers ─────────────────────────────────────────────
        this.videosPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.videosCurrentPage(), this.videosTotalPages()));

        this.playlistsPageNumbers = ko.computed(() => this._buildPageNumbers(
            this.playlistsCurrentPage(), this.playlistsTotalPages()));

        this.formatDate = formatDate;
        this.formatFileSize = formatFileSize;
    }

    _buildPageNumbers(current, total) {
        const pages = [];
        let start = Math.max(1, current - 2);
        let end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);
        for (let i = start; i <= end; i++) pages.push(i);
        return pages;
    }

    load = async () => {
        await Promise.all([
            this.loadChannel(),
            this.loadVideos(),
            this.loadPlaylists()
        ]);
    };

    // ── Channel ───────────────────────────────────────────────────────────────

    loadChannel = async () => {
        try {
            const response = await fetch(`/odata/ChannelOData(${this.channelId})`);
            if (response.ok) {
                const ch = await response.json();
                const banner = ch.BannerUrl ?? ch.bannerUrl;
                const avatar = ch.AvatarUrl ?? ch.avatarUrl;
                this.channel({
                    ...ch,
                    BannerUrl: banner ? encodeArchiveUrlForHtml(banner) : banner,
                    bannerUrl: banner ? encodeArchiveUrlForHtml(banner) : banner,
                    AvatarUrl: avatar ? encodeArchiveUrlForHtml(avatar) : avatar,
                    avatarUrl: avatar ? encodeArchiveUrlForHtml(avatar) : avatar
                });
            }
        } catch (error) {
            console.error('Error loading channel:', error);
        }
    };

    initTags = async () => {
        try {
            const tagsResponse = await fetch('/api/tags');
            const tagsData = await tagsResponse.json();
            const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

            const channelTagsResponse = await fetch(`/api/channels/${this.channelId}/tags`);
            const channelTagsData = await channelTagsResponse.json();
            const currentTags = (channelTagsData.tags || []).map(t => t.Name ?? t.name);

            const input = document.getElementById('channelTagsInput');
            if (!input) return;

            this._tagifyInstance = new Tagify(input, getTagifyOptions(allTagNames));

            if (currentTags.length > 0) {
                this._tagifyInstance.addTags(currentTags);
            }

            let saveTimeout = null;
            this._tagifyInstance.on('change', () => {
                clearTimeout(saveTimeout);
                saveTimeout = setTimeout(() => this.saveTags(), 600);
            });
        } catch (error) {
            console.error('Error initialising channel tags:', error);
        }
    };

    saveTags = async () => {
        if (!this._tagifyInstance) return;
        const tagNames = this._tagifyInstance.value.map(t => t.value);

        try {
            await fetch(`/api/channels/${this.channelId}/tags`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tagNames })
            });
        } catch (error) {
            console.error('Error saving channel tags:', error);
        }
    };

    // ── Videos tab ───────────────────────────────────────────────────────────

    loadVideos = async () => {
        this.loading(true);
        try {
            const skip = (this.videosCurrentPage() - 1) * this.videosPageSize;
            let filter = `ChannelId eq ${this.channelId} and IsIgnored ne true`;
            const search = this.videosSearch().trim();
            if (search) {
                const escaped = search.replace(/'/g, "''");
                filter += ` and contains(tolower(Title), '${escaped.toLowerCase()}')`;
            }

            const url = `/odata/VideoOData?$filter=${encodeURIComponent(filter)}&$orderby=UploadDate desc&$top=${this.videosPageSize}&$skip=${skip}&$count=true&$select=Id,Title,ThumbnailUrl,UploadDate,DownloadedAt,NeedsMetadataReview`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.videos((data.value || []).map(v => {
                    const rawThumb = v.ThumbnailUrl ?? v.thumbnailUrl;
                    const thumb = rawThumb ? encodeArchiveUrlForHtml(rawThumb) : rawThumb;
                    return { ...v, ThumbnailUrl: thumb, thumbnailUrl: thumb, _selected: ko.observable(false) };
                }));
                this.videosSelectAll(false);
                const total = data['@odata.count'] ?? 0;
                this.videosTotalCount(total);
                this.videosTotalPages(Math.max(1, Math.ceil(total / this.videosPageSize)));
            }
        } catch (error) {
            console.error('Error loading videos:', error);
        } finally {
            this.loading(false);
        }
    };

    searchVideos = async () => {
        this.videosSearch(this.videosSearchInput());
        this.videosCurrentPage(1);
        await this.loadVideos();
    };

    clearVideoSearch = async () => {
        this.videosSearchInput('');
        this.videosSearch('');
        this.videosCurrentPage(1);
        await this.loadVideos();
    };

    setVideoViewMode = async (mode) => {
        this.videoViewMode(mode);
        await this._saveViewModeSettings();
    };

    // ── Bulk video actions ────────────────────────────────────────────────────

    deleteSelectedVideos = async () => {
        const selected = this.selectedVideos();
        if (selected.length === 0) return;
        if (!confirm(`Delete ${selected.length} video file(s)? This cannot be undone.`)) return;

        this.videosBulkDeleting(true);
        let deleted = 0;
        try {
            for (const video of selected) {
                try {
                    const res = await fetch(`/api/channels/${this.channelId}/videos/${video.Id}/file`, { method: 'DELETE' });
                    if (res.ok) deleted++;
                } catch { /* continue */ }
            }
            toast.success(`Deleted ${deleted} of ${selected.length} video file(s).`);
            await this.loadVideos();
        } finally {
            this.videosBulkDeleting(false);
        }
    };

    addSelectedToPlaylist = async () => {
        const playlistId = this.videosBulkTargetPlaylistId();
        if (!playlistId) { toast.warning('Please select a playlist first.'); return; }
        const selected = this.selectedVideos();
        if (selected.length === 0) { toast.warning('No videos selected.'); return; }

        this.videosBulkAddingToPlaylist(true);
        try {
            const res = await fetch(`/api/custom/playlists/${playlistId}/videos/bulk`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoIds: selected.map(v => v.Id) })
            });
            if (res.ok) {
                const data = await res.json();
                toast.success(`Added ${data.added} video(s) to playlist.`);
                selected.forEach(v => v._selected(false));
                this.videosSelectAll(false);
            } else {
                toast.error('Failed to add videos to playlist.');
            }
        } catch (error) {
            console.error('Bulk add error:', error);
            toast.error('An error occurred.');
        } finally {
            this.videosBulkAddingToPlaylist(false);
        }
    };

    loadUserSettings = async () => {
        try {
            const response = await fetch('/api/user/settings');
            if (response.ok) {
                const settings = await response.json();
                this.videoViewMode(settings.videosTabViewMode || 'list');
            }
        } catch (error) {
            console.error('Error loading user settings:', error);
        }
    };

    _saveViewModeSettings = async () => {
        try {
            await fetch('/api/user/settings', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videosTabViewMode: this.videoViewMode() })
            });
        } catch (error) {
            console.error('Error saving user settings:', error);
        }
    };

    // Videos pagination
    videosGoToPage = async (page) => {
        if (page >= 1 && page <= this.videosTotalPages()) {
            this.videosCurrentPage(page);
            await this.loadVideos();
        }
    };

    videosPreviousPage = async () => {
        if (this.videosCurrentPage() > 1) {
            this.videosCurrentPage(this.videosCurrentPage() - 1);
            await this.loadVideos();
        }
    };

    videosNextPage = async () => {
        if (this.videosCurrentPage() < this.videosTotalPages()) {
            this.videosCurrentPage(this.videosCurrentPage() + 1);
            await this.loadVideos();
        }
    };

    // ── Playlists tab ─────────────────────────────────────────────────────────

    loadPlaylists = async () => {
        this.playlistsLoading(true);
        try {
            const skip = (this.playlistsCurrentPage() - 1) * this.playlistsPageSize;
            const url = `/odata/PlaylistOData?$filter=ChannelId eq ${this.channelId}&$orderby=Name&$top=${this.playlistsPageSize}&$skip=${skip}&$count=true`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.playlists((data.value || []).map(p => {
                    const raw = p.ThumbnailUrl ?? p.thumbnailUrl;
                    const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                    return { ...p, ThumbnailUrl: thumb, thumbnailUrl: thumb };
                }));
                const total = data['@odata.count'] ?? 0;
                this.playlistsTotalCount(total);
                this.playlistsTotalPages(Math.max(1, Math.ceil(total / this.playlistsPageSize)));
            }
        } catch (error) {
            console.error('Error loading playlists:', error);
        } finally {
            this.playlistsLoading(false);
        }
    };

    playlistsGoToPage = async (page) => {
        if (page >= 1 && page <= this.playlistsTotalPages()) {
            this.playlistsCurrentPage(page);
            await this.loadPlaylists();
        }
    };

    playlistsPreviousPage = async () => {
        if (this.playlistsCurrentPage() > 1) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() - 1);
            await this.loadPlaylists();
        }
    };

    playlistsNextPage = async () => {
        if (this.playlistsCurrentPage() < this.playlistsTotalPages()) {
            this.playlistsCurrentPage(this.playlistsCurrentPage() + 1);
            await this.loadPlaylists();
        }
    };

    // ── Edit channel ──────────────────────────────────────────────────────────

    openEditChannel = () => {
        const ch = this.channel();
        if (!ch) return;
        this.editName(ch.Name || '');
        this.editDescription(ch.Description || '');
        this.editBannerUrl(ch.BannerUrl || '');
        this.channelThumbnailPreviewUrl(ch.BannerUrl ? ch.BannerUrl + '?t=' + Date.now() : null);
        this.channelThumbnailFile(null);
        new bootstrap.Modal(document.getElementById('editChannelModal')).show();
    };

    saveChannel = async () => {
        try {
            let bannerUrl = this.editBannerUrl() || null;
            const file = this.channelThumbnailFile();
            if (file) {
                const form = new FormData();
                form.append('file', file);
                const uploadRes = await fetch(`/api/custom/channels/${this.channelId}/thumbnail`, {
                    method: 'POST',
                    body: form
                });
                if (!uploadRes.ok) {
                    toast.error('Failed to upload thumbnail. Please try again.');
                    return;
                }
                const data = await uploadRes.json();
                bannerUrl = data.thumbnailUrl ? encodeArchiveUrlForHtml(data.thumbnailUrl) : data.thumbnailUrl;
            }

            const response = await fetch(`/api/custom/channels/${this.channelId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.editName(),
                    description: this.editDescription() || null,
                    bannerUrl
                })
            });
            if (response.ok) {
                bootstrap.Modal.getInstance(document.getElementById('editChannelModal')).hide();
                await this.loadChannel();
            } else {
                toast.error('Failed to update channel. Please try again.');
            }
        } catch (error) {
            console.error('Error saving channel:', error);
            toast.error('Failed to update channel.');
        }
    };

    // ── Thumbnail picker (banner / avatar via upload) ─────────────────────────

    editBanner = () => this._openThumbnailPicker('banner');
    editAvatar = () => this._openThumbnailPicker('avatar');

    _openThumbnailPicker = (mode) => {
        this.thumbnailPickerMode(mode);
        this.thumbnailPickerUploadFile(null);
        this.thumbnailPickerUploadPreview(null);
        bootstrap.Modal.getOrCreateInstance(document.getElementById('thumbnailPickerModal')).show();
    };

    confirmThumbnailPicker = async () => {
        const mode = this.thumbnailPickerMode();
        const uploadFile = this.thumbnailPickerUploadFile();
        if (!uploadFile) return;

        try {
            const form = new FormData();
            form.append('file', uploadFile);
            const endpoint = mode === 'banner' ? 'banner' : 'avatar';
            const res = await fetch(`/api/channels/${this.channelId}/${endpoint}/upload`, {
                method: 'POST', body: form
            });
            if (!res.ok) {
                toast.error('Failed to upload image.');
                return;
            }
            const d = await res.json();
            const ch = this.channel();
            if (ch) {
                if (mode === 'banner') ch.BannerUrl = d.bannerUrl;
                else ch.AvatarUrl = d.avatarUrl;
                this.channel.valueHasMutated();
            }
            bootstrap.Modal.getInstance(document.getElementById('thumbnailPickerModal')).hide();
        } catch (error) {
            console.error('Error uploading image:', error);
            toast.error('Failed to upload image.');
        }
    };

    clearUploadPreview = () => {
        this.thumbnailPickerUploadFile(null);
        this.thumbnailPickerUploadPreview(null);
    };

    onImageDragOver = (data, event) => { event.preventDefault(); return true; };
    onImageDrop = (data, event) => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setPickerFile(file);
        return true;
    };
    onImageFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setPickerFile(file);
        event.target.value = '';
    };
    _setPickerFile = (file) => {
        this.thumbnailPickerUploadFile(file);
        const reader = new FileReader();
        reader.onload = e => this.thumbnailPickerUploadPreview(e.target.result);
        reader.readAsDataURL(file);
    };

    // Channel thumbnail upload (in edit modal)
    onChannelThumbnailDragOver = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        return true;
    };

    onChannelThumbnailDrop = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setChannelThumbnailFile(file);
        return true;
    };

    triggerChannelThumbnailInput = () => {
        document.getElementById('channelThumbnailInput').click();
    };

    onChannelThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setChannelThumbnailFile(file);
        event.target.value = '';
    };

    _setChannelThumbnailFile = (file) => {
        this.channelThumbnailFile(file);
        const reader = new FileReader();
        reader.onload = (e) => this.channelThumbnailPreviewUrl(e.target.result);
        reader.readAsDataURL(file);
    };

    // ── Create playlist ───────────────────────────────────────────────────────

    openCreatePlaylist = () => {
        this.newPlaylistName('');
        this.newPlaylistDescription('');
        new bootstrap.Modal(document.getElementById('createPlaylistModal')).show();
    };

    createPlaylist = async () => {
        try {
            const response = await fetch(`/api/custom/channels/${this.channelId}/playlists`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: this.newPlaylistName(),
                    description: this.newPlaylistDescription() || null
                })
            });
            if (response.ok) {
                const data = await response.json();
                bootstrap.Modal.getInstance(document.getElementById('createPlaylistModal')).hide();
                window.location.href = `/playlists/${data.id}`;
            } else {
                toast.error('Failed to create playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error creating playlist:', error);
            toast.error('Failed to create playlist.');
        }
    };

    // ── Subscribers tab ───────────────────────────────────────────────────────

    loadSubscribers = async () => {
        this.subscribersLoading(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/subscribers`);
            if (response.ok) {
                const data = await response.json();
                this.subscribers(data.subscribers || []);
            }
        } catch (error) {
            console.error('Error loading subscribers:', error);
        } finally {
            this.subscribersLoading(false);
        }
    };

    // ── Additional Content tab ────────────────────────────────────────────────

    formatExtrasPlaylistNames = (item) => {
        const names = item?.playlistNames;
        if (names && names.length) return names.join(', ');
        return '—';
    };

    loadAdditionalContent = async () => {
        if (this.extrasLoaded) return;
        this.extrasLoading(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/additional-content`);
            if (response.ok) {
                const data = await response.json();
                this.extrasItems(data.items || []);
                this.extrasLoaded = true;
            }
        } catch (error) {
            console.error('Error loading additional content:', error);
        } finally {
            this.extrasLoading(false);
        }
    };

    openUploadExtras = async () => {
        this.extrasUploadFiles([]);
        this.extrasUploadPlaylistIds([]);
        document.getElementById('extrasUploadInput').value = '';
        if (this.playlists().length === 0) {
            await this.loadPlaylists();
        }
        new bootstrap.Modal(document.getElementById('uploadExtrasModal')).show();
    };

    onExtrasFileSelected = (data, event) => {
        const files = Array.from(event.target.files || []);
        this.extrasUploadFiles(files);
    };

    confirmUploadExtras = async () => {
        const files = this.extrasUploadFiles();
        if (!files.length) return;

        this.extrasUploading(true);
        let anyFailed = false;
        try {
            for (const file of files) {
                const form = new FormData();
                form.append('file', file);
                for (const pid of this.extrasUploadPlaylistIds()) {
                    form.append('playlistIds', String(pid));
                }

                const response = await fetch(`/api/channels/${this.channelId}/additional-content`, {
                    method: 'POST',
                    body: form
                });

                if (response.ok) {
                    const data = await response.json();
                    this.extrasItems.push(data.item);
                } else {
                    anyFailed = true;
                    const data = await response.json().catch(() => ({}));
                    toast.error(data.message || `Failed to upload "${file.name}".`);
                }
            }

            if (!anyFailed) {
                bootstrap.Modal.getInstance(document.getElementById('uploadExtrasModal')).hide();
                toast.success(`${files.length} file(s) uploaded successfully.`);
            }
        } catch (error) {
            console.error('Error uploading additional content:', error);
            toast.error('An error occurred while uploading files.');
        } finally {
            this.extrasUploading(false);
        }
    };

    openEditExtras = async (item) => {
        this.extrasEditId(item.id);
        this.extrasEditFileName(item.fileName);
        this.extrasEditPlaylistIds((item.playlistIds || []).slice());
        if (this.playlists().length === 0) {
            await this.loadPlaylists();
        }
        new bootstrap.Modal(document.getElementById('editExtrasModal')).show();
    };

    confirmEditExtras = async () => {
        const id = this.extrasEditId();
        if (!id) return;
        try {
            const response = await fetch(`/api/additional-content/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    fileName: this.extrasEditFileName(),
                    playlistIds: this.extrasEditPlaylistIds().slice()
                })
            });

            if (response.ok) {
                // Reload the list to reflect changes
                this.extrasLoaded = false;
                await this.loadAdditionalContent();
                bootstrap.Modal.getInstance(document.getElementById('editExtrasModal')).hide();
            } else {
                const data = await response.json().catch(() => ({}));
                toast.error(data.message || 'Failed to update item.');
            }
        } catch (error) {
            console.error('Error updating additional content:', error);
            toast.error('An error occurred while updating the item.');
        }
    };

    deleteExtras = async (item) => {
        if (!confirm(`Delete "${item.fileName}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/api/additional-content/${item.id}`, { method: 'DELETE' });
            if (response.ok) {
                this.extrasItems.remove(item);
                toast.success('Item deleted.');
            } else {
                toast.error('Failed to delete item.');
            }
        } catch (error) {
            console.error('Error deleting additional content:', error);
            toast.error('An error occurred while deleting the item.');
        }
    };

    // ── Delete video file ─────────────────────────────────────────────────────

    deleteVideoFile = async (video) => {
        if (!confirm(`Delete the file for "${video.Title}"? This cannot be undone.`)) {
            return;
        }

        try {
            const response = await fetch(`/api/channels/${this.channelId}/videos/${video.Id}/file`, {
                method: 'DELETE'
            });

            const data = await response.json();

            if (response.ok) {
                this.videos.remove(video);
                this.videosTotalCount(this.videosTotalCount() - 1);
                toast.success('Video file deleted.');
            } else {
                toast.error(data.message || 'Failed to delete video file.');
            }
        } catch (error) {
            console.error('Error deleting video file:', error);
            toast.error('Error deleting video file. Please try again.');
        }
    };

    // ── Delete playlist ───────────────────────────────────────────────────────

    deletePlaylist = async (playlist) => {
        if (!confirm(`Delete playlist "${playlist.name}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/api/custom/playlists/${playlist.id}`, { method: 'DELETE' });
            if (response.ok) {
                await this.loadPlaylists();
                toast.success('Playlist deleted successfully.');
            } else {
                toast.error('Failed to delete playlist. Please try again.');
            }
        } catch (error) {
            console.error('Error deleting playlist:', error);
            toast.error('Failed to delete playlist.');
        }
    };

    // ── Playlist thumbnail ────────────────────────────────────────────────────

    openThumbnailModal = (playlist) => {
        this.thumbnailTargetPlaylist(playlist);
        const raw = playlist.thumbnailUrl ?? playlist.ThumbnailUrl;
        this.thumbnailPreviewUrl(raw ? encodeArchiveUrlForHtml(raw) + '?t=' + Date.now() : null);
        this.thumbnailFile(null);
        new bootstrap.Modal(document.getElementById('playlistThumbnailModal')).show();
    };

    onThumbnailDragOver = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        return true;
    };

    onThumbnailDrop = (data, event) => {
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setThumbnailFile(file);
        return true;
    };

    triggerThumbnailInput = () => {
        document.getElementById('playlistThumbnailInput').click();
    };

    onThumbnailFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setThumbnailFile(file);
        event.target.value = '';
    };

    _setThumbnailFile = (file) => {
        this.thumbnailFile(file);
        const reader = new FileReader();
        reader.onload = (e) => this.thumbnailPreviewUrl(e.target.result);
        reader.readAsDataURL(file);
    };

    uploadPlaylistThumbnail = async () => {
        const playlist = this.thumbnailTargetPlaylist();
        const file = this.thumbnailFile();
        if (!playlist || !file) return;

        this.uploadingThumbnail(true);
        try {
            const form = new FormData();
            form.append('file', file);
            const response = await fetch(`/api/custom/playlists/${playlist.Id}/thumbnail`, {
                method: 'POST',
                body: form
            });

            if (response.ok) {
                this.thumbnailCacheBust(Date.now());
                const data = await response.json();
                const idx = this.playlists().indexOf(playlist);
                if (idx >= 0) {
                    const u = data.thumbnailUrl ?? data.ThumbnailUrl;
                    const enc = u ? encodeArchiveUrlForHtml(u) : u;
                    const updated = Object.assign({}, playlist, { ThumbnailUrl: enc, thumbnailUrl: enc });
                    this.playlists.splice(idx, 1, updated);
                }
                bootstrap.Modal.getInstance(document.getElementById('playlistThumbnailModal')).hide();
            } else {
                toast.error('Failed to upload thumbnail. Please try again.');
            }
        } catch (error) {
            console.error('Error uploading thumbnail:', error);
            toast.error('Failed to upload thumbnail.');
        } finally {
            this.uploadingThumbnail(false);
        }
    };

    // ── Series tab ────────────────────────────────────────────────────────────

    loadSeriesCount = async () => {
        try {
            const response = await fetch(`/api/channels/${this.channelId}/series`);
            if (response.ok) {
                const data = await response.json();
                this.seriesCount((data.series || []).length);
            }
        } catch (error) {
            console.error('Error loading series count:', error);
        }
    };

    loadSeries = async () => {
        if (this.seriesLoaded) return;
        this.seriesLoading(true);
        try {
            const response = await fetch(`/api/channels/${this.channelId}/series`);
            if (response.ok) {
                const data = await response.json();
                this.seriesList((data.series || []).map(s => this._normalizeSeries(s)));
                this.seriesCount(this.seriesList().length);
                this.seriesLoaded = true;
            }
        } catch (error) {
            console.error('Error loading series:', error);
        } finally {
            this.seriesLoading(false);
        }
    };

    _normalizeSeries = (s) => {
        const urls = (s.playlists || []).slice(0, 4).map(p => {
            const raw = p.thumbnailUrl ?? p.ThumbnailUrl;
            return raw ? encodeArchiveUrlForHtml(raw) : null;
        });
        while (urls.length < 4) urls.push(null);
        return { ...s, collageImages: urls };
    };

    _loadSeriesAvailablePlaylists = async () => {
        this.seriesPlaylistsLoading(true);
        try {
            const url = `/odata/PlaylistOData?$filter=ChannelId eq ${this.channelId}&$orderby=Name&$top=200&$select=Id,Name,ThumbnailUrl`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.seriesAvailablePlaylists((data.value || []).map(p => {
                    const raw = p.ThumbnailUrl ?? p.thumbnailUrl;
                    const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                    return {
                        id: p.Id,
                        name: p.Name,
                        thumbnailUrl: thumb
                    };
                }));
            }
        } catch (error) {
            console.error('Error loading playlists for series:', error);
        } finally {
            this.seriesPlaylistsLoading(false);
        }
    };

    openCreateSeries = async () => {
        this.seriesEditId(null);
        this.seriesEditIsNew(true);
        this.seriesEditName('');
        this.seriesEditPlaylistIds([]);
        await this._loadSeriesAvailablePlaylists();
        new bootstrap.Modal(document.getElementById('seriesEditModal')).show();
    };

    openEditSeries = async (series) => {
        this.seriesEditId(series.id);
        this.seriesEditIsNew(false);
        this.seriesEditName(series.name);
        this.seriesEditPlaylistIds((series.playlists || []).map(p => p.id));
        await this._loadSeriesAvailablePlaylists();
        new bootstrap.Modal(document.getElementById('seriesEditModal')).show();
    };

    confirmSaveSeriesEdit = async () => {
        const name = this.seriesEditName().trim();
        if (!name) return;

        this.seriesSaving(true);
        try {
            if (this.seriesEditIsNew()) {
                const response = await fetch(`/api/channels/${this.channelId}/series`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name })
                });
                if (!response.ok) { toast.error('Failed to create series.'); return; }
                const data = await response.json();
                await fetch(`/api/series/${data.series.id}/playlists`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ playlistIds: this.seriesEditPlaylistIds().slice() })
                });
                toast.success('Series created.');
            } else {
                const seriesId = this.seriesEditId();
                const [nameRes, playlistsRes] = await Promise.all([
                    fetch(`/api/series/${seriesId}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name })
                    }),
                    fetch(`/api/series/${seriesId}/playlists`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ playlistIds: this.seriesEditPlaylistIds().slice() })
                    })
                ]);
                if (!nameRes.ok || !playlistsRes.ok) { toast.error('Failed to update series.'); return; }
                toast.success('Series updated.');
            }
            bootstrap.Modal.getInstance(document.getElementById('seriesEditModal')).hide();
            this.seriesLoaded = false;
            await this.loadSeries();
        } catch (error) {
            console.error('Error saving series:', error);
            toast.error('An error occurred while saving.');
        } finally {
            this.seriesSaving(false);
        }
    };

    deleteSeries = async (series) => {
        if (!confirm(`Delete series "${series.name}"? The playlists themselves will not be deleted.`)) return;
        try {
            const response = await fetch(`/api/series/${series.id}`, { method: 'DELETE' });
            if (response.ok) {
                this.seriesList.remove(s => s.id === series.id);
                this.seriesCount(this.seriesList().length);
                toast.success('Series deleted.');
            } else {
                toast.error('Failed to delete series.');
            }
        } catch (error) {
            console.error('Error deleting series:', error);
            toast.error('An error occurred while deleting.');
        }
    };
}

document.addEventListener('DOMContentLoaded', async () => {
    const viewModel = new CustomChannelViewModel(channelId);
    ko.applyBindings(viewModel);
    await viewModel.loadUserSettings();
    await viewModel.load();
    await viewModel.initTags();
    await viewModel.loadSeriesCount();

    // Programmatic Tab.show() does not run the tab button's click: loadSeries binding,
    // so load series data explicitly before showing that tab.
    const hasSeries = viewModel.seriesCount() > 0;
    if (hasSeries) {
        await viewModel.loadSeries();
    }
    const initialTabId = hasSeries ? 'series-tab' : 'playlists-tab';
    bootstrap.Tab.getOrCreateInstance(document.getElementById(initialTabId)).show();

    // Allow pressing Enter in the search box
    document.getElementById('videosSearchInput')?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') viewModel.searchVideos();
    });

    // Load subscribers when the tab is clicked (admin only)
    if (isAdmin) {
        $('#subscribers-tab').on('shown.bs.tab', function () {
            if (viewModel.subscribers().length === 0) {
                viewModel.loadSubscribers();
            }
        });
    }
});
