const POLL_INTERVAL_MS = 1000;

class AdminViewModel {
    constructor() {
        this.scanning = ko.observable(false);
        this.syncing = ko.observable(false);
        this.scanProgress = ko.observable(null);
        this.scanResult = ko.observable(null);

        this._pollTimer = null;
    }

    // ── File System Scan ─────────────────────────────────────────────────────

    scanFileSystem = async () => {
        this.scanning(true);
        this.scanProgress(null);
        this.scanResult(null);
        hideAlert();

        try {
            const response = await fetch('/api/admin/scan-filesystem', { method: 'POST' });

            if (response.status === 409) {
                showAlert('warning', 'A scan is already in progress. Attaching to it…');
                this._startPolling();
                return;
            }

            if (!response.ok) {
                const data = await response.json().catch(() => ({}));
                showAlert('danger', data.message || 'Failed to start scan.');
                this.scanning(false);
                return;
            }

            // 202 Accepted - scan started in background
            this._startPolling();
        } catch (error) {
            console.error('Error starting file system scan:', error);
            showAlert('danger', 'An unexpected error occurred.');
            this.scanning(false);
        }
    };

    cancelScan = async () => {
        try {
            await fetch('/api/admin/scan-filesystem/cancel', { method: 'POST' });
            showAlert('info', 'Cancellation requested. The scan will stop after the current channel.');
        } catch (error) {
            console.error('Error cancelling scan:', error);
        }
    };

    _startPolling = () => {
        this._stopPolling();
        this._pollTimer = setInterval(() => this._pollStatus(), POLL_INTERVAL_MS);
    };

    _stopPolling = () => {
        if (this._pollTimer !== null) {
            clearInterval(this._pollTimer);
            this._pollTimer = null;
        }
    };

    _pollStatus = async () => {
        try {
            const response = await fetch('/api/admin/scan-filesystem/status');
            if (!response.ok) return;

            const data = await response.json();

            if (data.progress) {
                this.scanProgress(data.progress);
            }

            if (!data.isRunning) {
                this._stopPolling();
                this.scanning(false);

                if (data.lastResult) {
                    this.scanResult(data.lastResult);
                    showAlert('success', `Scan complete. ${data.lastResult.newVideos} new, ${data.lastResult.updatedVideos} updated, ${data.lastResult.missingFiles} missing from disk.`);
                } else if (data.errorMessage) {
                    showAlert('danger', data.errorMessage);
                }
            }
        } catch (error) {
            console.error('Error polling scan status:', error);
        }
    };

    // ── Channel / Playlist Sync ───────────────────────────────────────────────

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

function hideAlert() {
    const el = document.getElementById('scanStatus');
    el.className = 'alert d-none';
}

document.addEventListener('DOMContentLoaded', async () => {
    const viewModel = new AdminViewModel();
    ko.applyBindings(viewModel);

    // If a scan is already running when the page loads, attach to it immediately
    try {
        const response = await fetch('/api/admin/scan-filesystem/status');
        if (response.ok) {
            const data = await response.json();
            if (data.isRunning) {
                viewModel.scanning(true);
                if (data.progress) viewModel.scanProgress(data.progress);
                showAlert('info', 'A scan is currently in progress…');
                viewModel._startPolling();
            }
        }
    } catch {
        // Non-critical; ignore errors on initial status check
    }
});
