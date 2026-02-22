class AdminViewModel {
    constructor() {
        this.scanning = ko.observable(false);
        this.syncing = ko.observable(false);
        this.scanResult = ko.observable(null);
    }

    scanFileSystem = async () => {
        this.scanning(true);
        this.scanResult(null);

        try {
            const response = await fetch('/api/admin/scan-filesystem', { method: 'POST' });
            const data = await response.json();

            if (response.ok) {
                this.scanResult(data);
                showAlert('success', `Scan complete. ${data.newVideos} new video(s) imported.`);
            } else {
                showAlert('danger', data.message || 'Scan failed. Please check the logs.');
            }
        } catch (error) {
            console.error('Error during file system scan:', error);
            showAlert('danger', 'An unexpected error occurred during the scan.');
        } finally {
            this.scanning(false);
        }
    };

    syncAllChannels = async () => {
        this.syncing(true);
        try {
            const response = await fetch('/api/channels/sync-all', { method: 'POST' });
            if (response.ok) {
                showAlert('success', 'Channel sync job queued successfully.');
            } else {
                showAlert('danger', 'Failed to queue channel sync.');
            }
        } catch (error) {
            console.error('Error syncing channels:', error);
            showAlert('danger', 'An unexpected error occurred.');
        } finally {
            this.syncing(false);
        }
    };

    syncAllPlaylists = async () => {
        this.syncing(true);
        try {
            const response = await fetch('/api/playlists/sync-all', { method: 'POST' });
            if (response.ok) {
                showAlert('success', 'Playlist sync job queued successfully.');
            } else {
                showAlert('danger', 'Failed to queue playlist sync.');
            }
        } catch (error) {
            console.error('Error syncing playlists:', error);
            showAlert('danger', 'An unexpected error occurred.');
        } finally {
            this.syncing(false);
        }
    };
}

function showAlert(type, message) {
    const el = document.getElementById('scanStatus');
    el.className = `alert alert-${type}`;
    el.textContent = message;
}

document.addEventListener('DOMContentLoaded', () => {
    const viewModel = new AdminViewModel();
    ko.applyBindings(viewModel);
});