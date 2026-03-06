const POLL_INTERVAL_MS = 1000;

class AdminViewModel {
    constructor() {
        this.scanning = ko.observable(false);
        this.syncing = ko.observable(false);
        this.scanProgress = ko.observable(null);
        this.scanResult = ko.observable(null);

        // Failed downloads tab
        this.failedVideos = ko.observableArray([]);
        this.failedLoading = ko.observable(false);
        this.failedLoaded = false;
        this.failedCount = ko.observable(0);

        // Tags tab
        this.globalTags = ko.observableArray([]);
        this.tagsLoading = ko.observable(false);
        this.tagsLoaded = false;
        this.newTagName = ko.observable('');
        this.addingTag = ko.observable(false);

        // Users tab
        this.users = ko.observableArray([]);
        this.availableRoles = ko.observableArray([]);
        this.usersSearchTerm = ko.observable('');
        this.usersLoading = ko.observable(false);
        this.usersLoaded = false;
        this.userSaving = ko.observable(false);
        this.currentUserId = ko.observable('');
        this.isCreatingUser = ko.observable(true);
        this.currentUser = {
            id: ko.observable(''),
            email: ko.observable(''),
            password: ko.observable(''),
            role: ko.observable('')
        };
        this.userModalTitle = ko.computed(() => this.isCreatingUser() ? 'Add User' : 'Edit User');
        this.filteredUsers = ko.computed(() => {
            const term = this.usersSearchTerm().toLowerCase().trim();
            if (!term) return this.users();
            return this.users().filter(u =>
                (u.email || '').toLowerCase().includes(term) ||
                (u.userName || '').toLowerCase().includes(term) ||
                (u.roles || []).some(r => (r || '').toLowerCase().includes(term))
            );
        });

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

    // ── Failed Downloads ──────────────────────────────────────────────────────

    loadFailedDownloads = async () => {
        if (this.failedLoaded) return; // only fetch once per page load
        this.failedLoading(true);
        try {
            const response = await fetch('/api/admin/failed-downloads');
            if (!response.ok) return;
            const data = await response.json();
            this.failedVideos(data.videos || []);
            this.failedCount(data.videos?.length ?? 0);
            this.failedLoaded = true;
        } catch (error) {
            console.error('Error loading failed downloads:', error);
        } finally {
            this.failedLoading(false);
        }
    };

    // ── Global Tags ───────────────────────────────────────────────────────────

    loadGlobalTags = async () => {
        if (this.tagsLoaded) return;
        this.tagsLoading(true);
        try {
            const response = await fetch('/api/admin/tags');
            if (!response.ok) return;
            const data = await response.json();
            this.globalTags(data.tags || []);
            this.tagsLoaded = true;
        } catch (error) {
            console.error('Error loading global tags:', error);
        } finally {
            this.tagsLoading(false);
        }
    };

    addGlobalTag = async () => {
        const name = this.newTagName().trim();
        if (!name) return;

        this.addingTag(true);
        try {
            const response = await fetch('/api/admin/tags', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name })
            });
            const data = await response.json();
            if (response.ok) {
                this.globalTags.push(data);
                this.globalTags.sort((a, b) => a.name.localeCompare(b.name));
                this.newTagName('');
                toast.success(`Tag "${data.name}" added.`);
            } else {
                toast.error(data.message || 'Failed to add tag.');
            }
        } catch (error) {
            console.error('Error adding global tag:', error);
            toast.error('An unexpected error occurred.');
        } finally {
            this.addingTag(false);
        }
    };

    deleteGlobalTag = async (tag) => {
        if (!confirm(`Delete global tag "${tag.name}"? This will also remove it from all tagged videos.`)) return;

        try {
            const response = await fetch(`/api/admin/tags/${tag.id}`, { method: 'DELETE' });
            if (response.ok) {
                this.globalTags.remove(tag);
            } else {
                const data = await response.json().catch(() => ({}));
                toast.error(data.message || 'Failed to delete tag.');
            }
        } catch (error) {
            console.error('Error deleting global tag:', error);
            toast.error('An unexpected error occurred.');
        }
    };

    onNewTagKeyDown = (data, event) => {
        if (event.key === 'Enter') this.addGlobalTag();
        return true;
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

    // ── Users tab ───────────────────────────────────────────────────────────────

    loadUsersTab = async () => {
        if (this.usersLoaded) return;
        this.usersLoading(true);
        try {
            await this._loadCurrentUser();
            await this._loadRoles();
            await this._loadUsers();
            this.usersLoaded = true;
        } catch (error) {
            console.error('Error loading users tab:', error);
            toast.error('Error loading users.');
        } finally {
            this.usersLoading(false);
        }
    };

    _loadCurrentUser = async () => {
        try {
            const response = await fetch('/api/admin/users/current');
            const result = await response.json();
            if (result.success && result.data) this.currentUserId(result.data.id || '');
        } catch (error) {
            console.error('Error loading current user:', error);
        }
    };

    _loadRoles = async () => {
        try {
            const response = await fetch('/api/admin/roles');
            const result = await response.json();
            if (result.success && result.data)
                this.availableRoles(result.data.map(r => ({ id: r.id, name: r.name })));
        } catch (error) {
            console.error('Error loading roles:', error);
        }
    };

    _loadUsers = async () => {
        try {
            const response = await fetch('/api/admin/users');
            const result = await response.json();
            if (!result.success) {
                toast.error(result.message || 'Failed to load users.');
                return;
            }
            const list = (result.data || []).map(u => ({
                id: u.id,
                userName: u.userName,
                email: u.email,
                isActive: u.isActive,
                roles: u.roles || [],
                initials: this._getInitials(u.userName || u.email)
            }));
            this.users(list);
        } catch (error) {
            console.error('Error loading users:', error);
            toast.error('Error loading users.');
        }
    };

    _getInitials = (name) => {
        if (!name) return '??';
        const parts = String(name).split(/[@\s]+/);
        if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
        return name.substring(0, 2).toUpperCase();
    };

    getRoleBadgeClass = (role) => {
        switch (role) {
            case 'Administrator': return 'bg-danger bg-opacity-10 text-danger';
            case 'User': return 'bg-primary bg-opacity-10 text-primary';
            default: return 'bg-secondary bg-opacity-10 text-secondary';
        }
    };

    isCurrentUser = (user) => user && user.id === this.currentUserId();

    canEditUser = (user) => !this.isCurrentUser(user);

    canToggleUser = (user) => !this.isCurrentUser(user);

    canDeleteUser = (user) => !this.isCurrentUser(user);

    showCreateUserModal = () => {
        this.isCreatingUser(true);
        this.currentUser.id('');
        this.currentUser.email('');
        this.currentUser.password('');
        this.currentUser.role('');
        const modal = new bootstrap.Modal(document.getElementById('userModal'));
        modal.show();
    };

    editUser = async (user) => {
        if (this.isCurrentUser(user)) {
            toast.warning('You cannot edit your own account.');
            return;
        }
        this.isCreatingUser(false);
        this.currentUser.id(user.id);
        this.currentUser.email(user.email);
        this.currentUser.password('');
        this.currentUser.role(user.roles && user.roles.length > 0 ? user.roles[0] : '');
        const modal = new bootstrap.Modal(document.getElementById('userModal'));
        modal.show();
    };

    saveUser = async (vm, event) => {
        const form = event && event.target;
        if (form && !form.checkValidity()) {
            form.reportValidity();
            return false;
        }
        this.userSaving(true);
        try {
            const url = this.isCreatingUser()
                ? '/api/admin/users'
                : `/api/admin/users/${this.currentUser.id()}`;
            const body = this.isCreatingUser()
                ? { email: this.currentUser.email(), password: this.currentUser.password(), role: this.currentUser.role() || null }
                : { email: this.currentUser.email(), role: this.currentUser.role() || null };
            const response = await fetch(url, {
                method: this.isCreatingUser() ? 'POST' : 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            const result = await response.json();
            if (result.success) {
                toast.success(result.message);
                await this._loadUsers();
                bootstrap.Modal.getInstance(document.getElementById('userModal')).hide();
                this.currentUser.id('');
                this.currentUser.email('');
                this.currentUser.password('');
                this.currentUser.role('');
            } else {
                toast.error(result.message || 'Failed to save user.');
            }
        } catch (error) {
            console.error('Error saving user:', error);
            toast.error('An unexpected error occurred.');
        } finally {
            this.userSaving(false);
        }
        return false;
    };

    toggleUserStatus = async (user) => {
        if (this.isCurrentUser(user)) {
            toast.warning('You cannot disable your own account.');
            return;
        }
        const action = user.isActive ? 'disable' : 'enable';
        if (!confirm(`Are you sure you want to ${action} this user?`)) return;
        try {
            const response = await fetch(`/api/admin/users/${user.id}/toggle-status`, { method: 'POST' });
            const result = await response.json();
            if (result.success) {
                toast.success(result.message);
                await this._loadUsers();
            } else {
                toast.error(result.message || 'Failed to update status.');
            }
        } catch (error) {
            console.error('Error toggling user status:', error);
            toast.error('An unexpected error occurred.');
        }
    };

    deleteUser = async (user) => {
        if (this.isCurrentUser(user)) {
            toast.warning('You cannot delete your own account.');
            return;
        }
        if (!confirm(`Are you sure you want to delete the user "${user.email}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/api/admin/users/${user.id}`, { method: 'DELETE' });
            const result = await response.json();
            if (result.success) {
                toast.success(result.message);
                await this._loadUsers();
            } else {
                toast.error(result.message || 'Failed to delete user.');
            }
        } catch (error) {
            console.error('Error deleting user:', error);
            toast.error('An unexpected error occurred.');
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

    // Load the failed download count for the badge (non-blocking)
    fetch('/api/admin/failed-downloads')
        .then(r => r.ok ? r.json() : null)
        .then(data => {
            if (data) {
                viewModel.failedVideos(data.videos || []);
                viewModel.failedCount(data.videos?.length ?? 0);
                viewModel.failedLoaded = true;
            }
        })
        .catch(() => { });

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
