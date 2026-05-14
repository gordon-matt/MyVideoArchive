import { encodeArchiveUrlForHtml } from './utils.js';

class SeriesDetailsViewModel {
    constructor(seriesId) {
        this.seriesId = seriesId;
        this.series = ko.observable(null);
        this.loading = ko.observable(true);
        this.saving = ko.observable(false);
        this._sortable = null;

        // Edit series state (used by _SeriesModals partial)
        this.seriesEditId = ko.observable(null);
        this.seriesEditIsNew = ko.observable(false);
        this.seriesEditName = ko.observable('');
        this.seriesEditPlaylistIds = ko.observableArray([]);
        this.seriesAvailablePlaylists = ko.observableArray([]);
        this.seriesPlaylistsLoading = ko.observable(false);
        this.seriesSaving = ko.observable(false);
    }

    load = async () => {
        this.loading(true);
        try {
            const response = await fetch(`/api/series/${this.seriesId}`, { credentials: 'same-origin' });
            if (response.ok) {
                const s = await response.json();
                if (s.playlists?.length) {
                    s.playlists = s.playlists.map(p => ({
                        ...p,
                        thumbnailUrl: p.thumbnailUrl ? encodeArchiveUrlForHtml(p.thumbnailUrl) : p.thumbnailUrl
                    }));
                    const needFallback = s.playlists.some(p => !p.thumbnailUrl);
                    if (needFallback && s.channelId) {
                        try {
                            const fbRes = await fetch(
                                `/api/custom/channels/${s.channelId}/playlist-thumbnail-fallbacks`,
                                { credentials: 'same-origin' });
                            if (fbRes.ok) {
                                const fb = await fbRes.json();
                                const map = fb.thumbnails || fb.Thumbnails || {};
                                s.playlists = s.playlists.map(p => {
                                    if (p.thumbnailUrl) return p;
                                    const rawUrl = map[p.id] ?? map[String(p.id)];
                                    if (!rawUrl) return p;
                                    return { ...p, thumbnailUrl: encodeArchiveUrlForHtml(rawUrl) };
                                });
                            }
                        } catch {
                            /* non-custom channel or network error — keep list as-is */
                        }
                    }
                }
                this.series(s);
                if (isAdmin) {
                    setTimeout(() => this._initSortable(), 100);
                }
            }
        } catch (error) {
            console.error('Error loading series:', error);
        } finally {
            this.loading(false);
        }
    };

    _initSortable = () => {
        const container = document.getElementById('seriesPlaylistList');
        if (!container) return;
        if (this._sortable) this._sortable.destroy();

        this._sortable = new Sortable(container, {
            handle: '.drag-handle',
            animation: 150,
            onEnd: async () => {
                const items = container.querySelectorAll('.series-playlist-item');
                const s = this.series();
                if (!s) return;

                const reordered = [];
                items.forEach(item => {
                    const raw = item.getAttribute('data-playlist-id');
                    const id = raw == null || raw === '' ? NaN : Number(raw);
                    const pl = s.playlists.find(p => Number(p.id) === id);
                    if (pl) reordered.push(pl);
                });

                if (reordered.length !== s.playlists.length) {
                    console.error('Reorder mismatch: DOM vs playlists', { domCount: reordered.length, listCount: s.playlists.length });
                    toast.error('Could not save order (mismatch). Reloading.');
                    await this.load();
                    return;
                }

                // Update the observable so $index() re-renders.
                // Knockout will re-render the list, so we re-init Sortable afterwards.
                const updated = { ...s, playlists: reordered };
                this.series(updated);
                setTimeout(() => this._initSortable(), 50);

                await this._saveOrder(reordered.map(p => p.id));
            }
        });
    };

    movePlaylistToTop = async (playlist) => {
        const s = this.series();
        if (!s?.playlists?.length || !playlist) return;
        const idx = s.playlists.findIndex(p => p.id === playlist.id);
        if (idx <= 0) return;
        const reordered = [...s.playlists];
        reordered.splice(idx, 1);
        reordered.unshift(playlist);
        this.series({ ...s, playlists: reordered });
        setTimeout(() => this._initSortable(), 50);
        await this._saveOrder(reordered.map(p => p.id));
    };

    _saveOrder = async (playlistIds) => {
        this.saving(true);
        try {
            const response = await fetch(`/api/series/${this.seriesId}/playlists`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify({ playlistIds })
            });
            if (!response.ok) {
                toast.error('Failed to save order.');
            }
        } catch (error) {
            console.error('Error saving order:', error);
            toast.error('An error occurred while saving.');
        } finally {
            this.saving(false);
        }
    };

    openEditSeries = async () => {
        const s = this.series();
        if (!s) return;
        this.seriesEditId(s.id);
        this.seriesEditIsNew(false);
        this.seriesEditName(s.name);
        this.seriesEditPlaylistIds((s.playlists || []).map(p => p.id));
        await this._loadSeriesAvailablePlaylists(s.channelId);
        new bootstrap.Modal(document.getElementById('seriesEditModal')).show();
    };

    _loadSeriesAvailablePlaylists = async (channelId) => {
        this.seriesPlaylistsLoading(true);
        try {
            const url = `/odata/PlaylistOData?$filter=ChannelId eq ${channelId}&$orderby=Name&$top=200&$select=Id,Name,ThumbnailUrl`;
            const response = await fetch(url);
            if (response.ok) {
                const data = await response.json();
                this.seriesAvailablePlaylists((data.value || []).map(p => ({
                    id: p.Id,
                    name: p.Name,
                    thumbnailUrl: p.ThumbnailUrl
                })));
            }
        } catch (error) {
            console.error('Error loading playlists:', error);
        } finally {
            this.seriesPlaylistsLoading(false);
        }
    };

    confirmSaveSeriesEdit = async () => {
        const id = this.seriesEditId();
        if (!id) return;
        this.seriesSaving(true);
        try {
            const [nameRes, playlistsRes] = await Promise.all([
                fetch(`/api/series/${id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: this.seriesEditName() })
                }),
                fetch(`/api/series/${id}/playlists`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({ playlistIds: this.seriesEditPlaylistIds().slice() })
                })
            ]);
            if (!nameRes.ok || !playlistsRes.ok) { toast.error('Failed to update series.'); return; }
            bootstrap.Modal.getInstance(document.getElementById('seriesEditModal')).hide();
            await this.load();
            toast.success('Series updated.');
        } catch (error) {
            console.error('Error saving series:', error);
            toast.error('An error occurred while saving.');
        } finally {
            this.seriesSaving(false);
        }
    };
}

document.addEventListener('DOMContentLoaded', async () => {
    const viewModel = new SeriesDetailsViewModel(seriesId);
    ko.applyBindings(viewModel);
    await viewModel.load();
});
