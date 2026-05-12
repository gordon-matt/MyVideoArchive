class SeriesDetailsViewModel {
    constructor(seriesId) {
        this.seriesId = seriesId;
        this.series = ko.observable(null);
        this.loading = ko.observable(true);

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
            const response = await fetch(`/api/series/${this.seriesId}`);
            if (response.ok) {
                this.series(await response.json());
            }
        } catch (error) {
            console.error('Error loading series:', error);
        } finally {
            this.loading(false);
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
