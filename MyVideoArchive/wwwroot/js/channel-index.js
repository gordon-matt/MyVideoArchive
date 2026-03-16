import { formatDate } from './utils.js';

class ChannelsViewModel {
    constructor() {
        this.channels = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.platformFilter = ko.observable('');
        this.platformFilter.subscribe(() => this.loadChannels());
        this.selectedPlatform = ko.observable('YouTube');
        this.newChannelUrl = ko.observable('');
        this.customChannelName = ko.observable('');
        this.customChannelDescription = ko.observable('');

        this.isAdmin = window.isAdminUser === true;
        this.isAddingChannel = ko.observable(false);
        this.channelToDelete = ko.observable(null);
        this.adminDeleteMetadata = ko.observable(false);
        this.adminDeleteFiles = ko.observable(false);

        // ── Assign users to channel modal ─────────────────────────────────────
        this.assignUsersChannel = ko.observable(null);  // the channel being edited
        this.assignUsersLoading = ko.observable(false);
        this.assignUsersAll = ko.observableArray([]);   // { userId, username, email, isSubscribed: ko.observable }
        this.assignUsersSaving = ko.observable(false);

        this.formatDate = formatDate;

        // Platforms that are added via a URL (not the custom manual flow)
        this.urlBasedPlatforms = ['YouTube', 'BitChute'];

        this.isUrlBasedPlatform = ko.computed(() => this.urlBasedPlatforms.includes(this.selectedPlatform()));

        this.canSubmit = ko.computed(() => {
            if (this.isUrlBasedPlatform()) {
                return this.newChannelUrl().length > 0;
            }
            return this.customChannelName().length > 0;
        });

        // ── Channel view mode (banner list vs avatar grid) ────────────────────
        const savedViewMode = localStorage.getItem('channelIndexViewMode') || 'banner';
        this.channelViewMode = ko.observable(savedViewMode);

        // ── Thumbnail picker state ─────────────────────────────────────────────
        this.thumbnailPickerChannelId = ko.observable(null); // id of the newly added channel
        this.thumbnailPickerItems = ko.observableArray([]);
        this.thumbnailPickerLoading = ko.observable(false);
        this.thumbnailPickerAssignTo = ko.observable('banner'); // 'banner' | 'avatar' — which slot the next thumbnail click will fill
        this.selectedBannerUrl = ko.observable(null);
        this.selectedAvatarUrl = ko.observable(null);
        this.bannerUploadFile = ko.observable(null);
        this.avatarUploadFile = ko.observable(null);
        this.bannerUploadPreview = ko.observable(null);
        this.avatarUploadPreview = ko.observable(null);
    }

    setChannelViewMode = (mode) => {
        this.channelViewMode(mode);
        localStorage.setItem('channelIndexViewMode', mode);
    };

    loadChannels = async () => {
        this.loading(true);

        var url = '/odata/ChannelOData?$orderby=Name';
        if (this.platformFilter()) {
            url += `&$filter=Platform eq '${this.platformFilter()}'`;
        }

        await fetch(url)
            .then(response => response.json())
            .then(data => {
                this.channels(data.value || []);
                this.loading(false);
            })
            .catch(error => {
                console.error('Error loading channels:', error);
                this.loading(false);
            });
    };

    addChannel = async () => {
        if (this.isUrlBasedPlatform()) {
            await this.addPlatformChannel();
        } else {
            await this.addCustomChannel();
        }
    };

    addPlatformChannel = async () => {
        const url = this.newChannelUrl();
        const platform = this.selectedPlatform();
        if (!url) return;

        const channelId = this.extractChannelId(url);

        const newChannel = {
            Platform: platform,
            ChannelId: channelId,
            Name: 'Loading...',
            Url: url,
            SubscribedAt: new Date().toISOString()
        };

        this.isAddingChannel(true);
        try {
            const response = await fetch('/odata/ChannelOData', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newChannel)
            });

            if (!response.ok) {
                toast.error('Failed to add channel. Please try again.');
                return;
            }

            const data = await response.json();
            this.channels.push(data);
            this.newChannelUrl('');
            bootstrap.Modal.getInstance(document.getElementById('addChannelModal')).hide();

            // Show thumbnail picker for the newly added channel
            this.thumbnailPickerChannelId(data.Id);
            this.thumbnailPickerAssignTo('banner');
            this.selectedBannerUrl(null);
            this.selectedAvatarUrl(null);
            this.bannerUploadFile(null);
            this.avatarUploadFile(null);
            this.bannerUploadPreview(null);
            this.avatarUploadPreview(null);
            this.thumbnailPickerItems([]);

            const pickerModal = bootstrap.Modal.getOrCreateInstance(document.getElementById('thumbnailPickerModal'));
            pickerModal.show();

            await this.loadThumbnailPickerItems(data.Id);

        } catch (error) {
            console.error(`Error adding ${platform} channel:`, error);
            toast.error('Failed to add channel. Please try again.');
        } finally {
            this.isAddingChannel(false);
        }
    };

    loadThumbnailPickerItems = async (channelId) => {
        this.thumbnailPickerLoading(true);
        try {
            const response = await fetch(`/api/channels/${channelId}/images/available`);
            if (response.ok) {
                const data = await response.json();
                this.thumbnailPickerItems(data.thumbnails || []);
                // Pre-select the default banner if available
                if (!this.selectedBannerUrl() && data.defaultBannerUrl) {
                    this.selectedBannerUrl(data.defaultBannerUrl);
                }
            }
        } catch (error) {
            console.error('Error loading thumbnail picker items:', error);
        } finally {
            this.thumbnailPickerLoading(false);
        }
    };

    setThumbnailAssignToBanner = () => this.thumbnailPickerAssignTo('banner');
    setThumbnailAssignToAvatar = () => this.thumbnailPickerAssignTo('avatar');

    selectThumbnailForIndex = (thumbnail) => {
        const url = thumbnail.url;
        if (this.thumbnailPickerAssignTo() === 'banner') {
            this.selectedBannerUrl(url);
        } else {
            this.selectedAvatarUrl(url);
        }
    };

    clearBannerSelection = () => this.selectedBannerUrl(null);
    clearAvatarSelection = () => this.selectedAvatarUrl(null);

    confirmThumbnailPicker = async () => {
        const channelId = this.thumbnailPickerChannelId();
        if (!channelId) return;

        try {
            // Handle file uploads first
            let bannerUrl = this.selectedBannerUrl();
            let avatarUrl = this.selectedAvatarUrl();

            const bannerFile = this.bannerUploadFile();
            if (bannerFile) {
                const form = new FormData();
                form.append('file', bannerFile);
                const res = await fetch(`/api/channels/${channelId}/banner/upload`, { method: 'POST', body: form });
                if (res.ok) {
                    const d = await res.json();
                    bannerUrl = d.bannerUrl;
                }
            }

            const avatarFile = this.avatarUploadFile();
            if (avatarFile) {
                const form = new FormData();
                form.append('file', avatarFile);
                const res = await fetch(`/api/channels/${channelId}/avatar/upload`, { method: 'POST', body: form });
                if (res.ok) {
                    const d = await res.json();
                    avatarUrl = d.avatarUrl;
                }
            }

            // Update URLs
            if (bannerUrl !== null || avatarUrl !== null) {
                await fetch(`/api/channels/${channelId}/images`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ bannerUrl, avatarUrl })
                });
            }

            // Refresh the channel in the list
            const updated = this.channels().find(c => c.Id === channelId);
            if (updated) {
                updated.BannerUrl = bannerUrl;
                updated.AvatarUrl = avatarUrl;
                this.channels.valueHasMutated();
            }

            bootstrap.Modal.getInstance(document.getElementById('thumbnailPickerModal')).hide();
        } catch (error) {
            console.error('Error applying thumbnail selection:', error);
            toast.error('Failed to apply image selection.');
        }
    };

    skipThumbnailPicker = () => {
        bootstrap.Modal.getInstance(document.getElementById('thumbnailPickerModal')).hide();
    };

    // ── Drag & drop / file input for banner ──────────────────────────────────

    onBannerDragOver = (data, event) => { event.preventDefault(); return true; };
    onBannerDrop = (data, event) => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setBannerFile(file);
        return true;
    };
    onBannerFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setBannerFile(file);
        event.target.value = '';
    };
    _setBannerFile = (file) => {
        this.bannerUploadFile(file);
        const reader = new FileReader();
        reader.onload = e => this.bannerUploadPreview(e.target.result);
        reader.readAsDataURL(file);
    };
    clearBannerUpload = () => { this.bannerUploadFile(null); this.bannerUploadPreview(null); };

    // ── Drag & drop / file input for avatar ──────────────────────────────────

    onAvatarDragOver = (data, event) => { event.preventDefault(); return true; };
    onAvatarDrop = (data, event) => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (file && file.type.startsWith('image/')) this._setAvatarFile(file);
        return true;
    };
    onAvatarFileSelected = (data, event) => {
        const file = event.target.files?.[0];
        if (file) this._setAvatarFile(file);
        event.target.value = '';
    };
    _setAvatarFile = (file) => {
        this.avatarUploadFile(file);
        const reader = new FileReader();
        reader.onload = e => this.avatarUploadPreview(e.target.result);
        reader.readAsDataURL(file);
    };
    clearAvatarUpload = () => { this.avatarUploadFile(null); this.avatarUploadPreview(null); };

    addCustomChannel = async () => {
        const name = this.customChannelName().trim();
        if (!name) return;

        this.isAddingChannel(true);
        try {
            const response = await fetch('/api/custom/channels', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    description: this.customChannelDescription().trim() || null
                })
            });
            if (!response.ok) throw new Error('Failed to create custom channel');
            const data = await response.json();
            window.location.href = `/channels/${data.id}`;
        } catch (error) {
            console.error('Error adding custom channel:', error);
            toast.error('Failed to create custom channel. Please try again.');
            this.isAddingChannel(false);
        }
    };

    extractChannelId = (url) => {
        var match = url.match(/channel\/([^\/\?]+)/);
        if (match) return match[1];

        match = url.match(/c\/([^\/\?]+)/);
        if (match) return match[1];

        match = url.match(/user\/([^\/\?]+)/);
        if (match) return match[1];

        match = url.match(/\/@@([^\/\?]+)/);
        if (match) return match[1];

        return url;
    };

    viewChannel = (channel) => {
        window.location.href = `/channels/${channel.Id}`;
    };

    deleteChannel = (channel) => {
        if (this.isAdmin) {
            this.channelToDelete(channel);
            this.adminDeleteMetadata(false);
            this.adminDeleteFiles(false);
            const modalEl = document.getElementById('adminDeleteChannelModal');
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        } else {
            this._unsubscribeChannel(channel);
        }
    };

    confirmAdminDelete = async () => {
        const channel = this.channelToDelete();
        if (!channel) return;

        const deleteMetadata = this.adminDeleteMetadata();
        const deleteFiles = this.adminDeleteFiles();

        const params = new URLSearchParams({ deleteMetadata, deleteFiles });

        const modalEl = document.getElementById('adminDeleteChannelModal');
        bootstrap.Modal.getOrCreateInstance(modalEl).hide();

        await fetch(`/api/channels/${channel.Id}?${params}`, {
            method: 'DELETE'
        })
        .then(response => {
            if (response.ok) {
                this.channels.remove(channel);
                this.channelToDelete(null);
                toast.success('Channel deleted successfully.');
            } else {
                toast.error('Failed to delete channel.');
            }
        })
        .catch(error => {
            console.error('Error deleting channel:', error);
            toast.error('Failed to delete channel.');
        });
    };

    openAssignUsersModal = async (channel) => {
        this.assignUsersChannel(channel);
        this.assignUsersAll([]);
        this.assignUsersLoading(true);
        bootstrap.Modal.getOrCreateInstance(document.getElementById('assignUsersModal')).show();

        try {
            const response = await fetch(`/api/channels/${channel.Id}/user-subscriptions`);
            if (response.ok) {
                const data = await response.json();
                const users = (data.users || []).map(u => ({
                    ...u,
                    isSubscribed: ko.observable(u.isSubscribed)
                }));
                this.assignUsersAll(users);
            } else {
                toast.error('Failed to load user list.');
            }
        } catch (error) {
            console.error('Error loading user subscriptions:', error);
            toast.error('Failed to load user list.');
        } finally {
            this.assignUsersLoading(false);
        }
    };

    confirmAssignUsers = async () => {
        const channel = this.assignUsersChannel();
        if (!channel) return;

        const subscribedUserIds = this.assignUsersAll()
            .filter(u => u.isSubscribed())
            .map(u => u.userId);

        this.assignUsersSaving(true);
        try {
            const response = await fetch(`/api/channels/${channel.Id}/user-subscriptions`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ subscribedUserIds })
            });

            if (response.ok) {
                bootstrap.Modal.getInstance(document.getElementById('assignUsersModal')).hide();
                toast.success('User subscriptions updated successfully.');
            } else {
                const data = await response.json().catch(() => ({}));
                toast.error(data.detail || data.message || 'Failed to update subscriptions.');
            }
        } catch (error) {
            console.error('Error updating subscriptions:', error);
            toast.error('Failed to update subscriptions.');
        } finally {
            this.assignUsersSaving(false);
        }
    };

    _unsubscribeChannel = async (channel) => {
        if (!confirm(`Are you sure you want to unsubscribe from ${channel.Name}?`)) {
            return;
        }

        await fetch(`/odata/ChannelOData(${channel.Id})`, {
            method: 'DELETE'
        })
        .then(response => {
            if (response.ok) {
                this.channels.remove(channel);
                toast.success(`Unsubscribed from ${channel.Name}.`);
            } else {
                toast.error('Failed to unsubscribe from channel.');
            }
        })
        .catch(error => {
            console.error('Error unsubscribing from channel:', error);
            toast.error('Failed to unsubscribe from channel.');
        });
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new ChannelsViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadChannels();
});
