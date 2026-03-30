import { formatDate } from './utils.js';

class ChannelsViewModel {
    constructor() {
        this.channels = ko.observableArray([]);
        this.categories = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.platformFilter = ko.observable('');
        this.platformFilter.subscribe(() => this.loadChannels());
        this.selectedPlatform = ko.observable('YouTube');
        this.newChannelUrl = ko.observable('');
        this.customChannelName = ko.observable('');
        this.customChannelDescription = ko.observable('');

        this.isAdmin = window.isAdminUser === true;

        // ── Include Unsubbed (admin only) ─────────────────────────────────────
        const savedIncludeUnsubbed = this.isAdmin
            ? localStorage.getItem('channelIncludeUnsubbed') === 'true'
            : false;
        this.includeUnsubbed = ko.observable(savedIncludeUnsubbed);
        this.includeUnsubbed.subscribe(val => {
            if (this.isAdmin) localStorage.setItem('channelIncludeUnsubbed', String(val));
            this.loadChannels();
        });

        this.isAddingChannel = ko.observable(false);
        this.channelToDelete = ko.observable(null);
        this.adminDeleteMetadata = ko.observable(false);
        this.adminDeleteFiles = ko.observable(false);

        // ── Assign users to channel modal ─────────────────────────────────────
        this.assignUsersChannel = ko.observable(null);
        this.assignUsersLoading = ko.observable(false);
        this.assignUsersAll = ko.observableArray([]);
        this.assignUsersSaving = ko.observable(false);

        this.formatDate = formatDate;

        this.urlBasedPlatforms = ['YouTube', 'BitChute'];
        this.isUrlBasedPlatform = ko.computed(() => this.urlBasedPlatforms.includes(this.selectedPlatform()));
        this.canSubmit = ko.computed(() => {
            if (this.isUrlBasedPlatform()) return this.newChannelUrl().length > 0;
            return this.customChannelName().length > 0;
        });

        // ── Channel view mode ─────────────────────────────────────────────────
        const savedViewMode = localStorage.getItem('channelIndexViewMode') || 'avatar';
        this.channelViewMode = ko.observable(savedViewMode);

        // ── Category feature ──────────────────────────────────────────────────
        this.enableChannelCategories = ko.observable(false);

        // Derived list: categories with their channels
        this.categorizedGroups = ko.computed(() => {
            if (!this.enableChannelCategories()) return [];
            const cats = this.categories();
            const channels = this.channels();
            return cats.map(cat => ({
                category: cat,
                channels: channels.filter(c => c.categoryId === cat.id)
            }));
        });

        this.uncategorizedChannels = ko.computed(() => {
            if (!this.enableChannelCategories()) return this.channels();
            return this.channels().filter(c => !c.categoryId);
        });

        // When categories are enabled, force avatar view and persist + reload data
        this.enableChannelCategories.subscribe(async enabled => {
            if (enabled) {
                this.channelViewMode('avatar');
            }
            await this.saveEnableCategories(enabled);
            if (enabled) {
                await this.loadCategories();
            }
            await this.loadChannels();
        });

        // ── Edit category modal state ─────────────────────────────────────────
        this.editCategoryChannel = ko.observable(null);   // channel being edited
        this.editCategorySelectedId = ko.observable(null); // selected category id (null = uncategorized)
        this.editCategoryNewName = ko.observable('');      // name for a brand new category
        this.editCategoryCreatingNew = ko.observable(false);
        this.editCategorySaving = ko.observable(false);

        // Sentinel value for "create new"
        this.CREATE_NEW_SENTINEL = '__create_new__';

        this.editCategoryDropdownValue = ko.computed({
            read: () => {
                if (this.editCategoryCreatingNew()) return this.CREATE_NEW_SENTINEL;
                const id = this.editCategorySelectedId();
                return id === null ? '' : String(id);
            },
            write: (val) => {
                if (val === this.CREATE_NEW_SENTINEL) {
                    this.editCategoryCreatingNew(true);
                    this.editCategorySelectedId(null);
                } else {
                    this.editCategoryCreatingNew(false);
                    this.editCategorySelectedId(val === '' ? null : parseInt(val, 10));
                }
            }
        });

        // ── Thumbnail picker state ─────────────────────────────────────────────
        this.thumbnailPickerChannelId = ko.observable(null);
        this.thumbnailPickerItems = ko.observableArray([]);
        this.thumbnailPickerLoading = ko.observable(false);
        this.thumbnailPickerAssignTo = ko.observable('banner');
        this.selectedBannerUrl = ko.observable(null);
        this.selectedAvatarUrl = ko.observable(null);
        this.bannerUploadFile = ko.observable(null);
        this.avatarUploadFile = ko.observable(null);
        this.bannerUploadPreview = ko.observable(null);
        this.avatarUploadPreview = ko.observable(null);
    }

    // ── View mode ─────────────────────────────────────────────────────────────

    setChannelViewMode = (mode) => {
        this.channelViewMode(mode);
        localStorage.setItem('channelIndexViewMode', mode);
    };

    // ── Data loading ──────────────────────────────────────────────────────────

    loadSettings = async () => {
        try {
            const response = await fetch('/api/user/settings');
            if (response.ok) {
                const data = await response.json();
                this.enableChannelCategories(data.enableChannelCategories === true);
                if (!data.enableChannelCategories) {
                    const saved = localStorage.getItem('channelIndexViewMode') || 'banner';
                    this.channelViewMode(saved);
                }
            }
        } catch (e) {
            console.warn('Failed to load user settings:', e);
        }
    };

    saveEnableCategories = async (enabled) => {
        try {
            await fetch('/api/user/settings', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enableChannelCategories: enabled })
            });
        } catch (e) {
            console.warn('Failed to save category setting:', e);
        }
    };

    loadCategories = async () => {
        try {
            const response = await fetch('/api/channel-categories');
            if (response.ok) {
                const data = await response.json();
                this.categories(data);
            }
        } catch (e) {
            console.warn('Failed to load categories:', e);
        }
    };

    loadChannels = async () => {
        this.loading(true);

        if (this.isAdmin || this.enableChannelCategories()) {
            // Admins always use the richer API endpoint (includes subscriber counts,
            // categories, and proper cross-user visibility).
            // Non-admin users use it only when categories are enabled.
            const params = new URLSearchParams();
            if (this.platformFilter()) params.append('platform', this.platformFilter());
            if (this.isAdmin && this.includeUnsubbed()) params.append('includeUnsubbed', 'true');
            const qs = params.toString();
            const url = '/api/channels/my-channels' + (qs ? '?' + qs : '');
            try {
                const response = await fetch(url);
                if (response.ok) {
                    const data = await response.json();
                    this.channels(data);
                }
            } catch (e) {
                console.error('Error loading channels:', e);
            } finally {
                this.loading(false);
            }
        } else {
            // Original OData path for non-admin users without categories
            let url = '/odata/ChannelOData?$orderby=Name';
            if (this.platformFilter()) {
                url += `&$filter=Platform eq '${this.platformFilter()}'`;
            }
            try {
                const response = await fetch(url);
                const data = await response.json();
                // Map OData shape to camelCase so views work uniformly
                this.channels((data.value || []).map(c => ({
                    id: c.Id,
                    channelId: c.ChannelId,
                    name: c.Name,
                    url: c.Url,
                    avatarUrl: c.AvatarUrl,
                    bannerUrl: c.BannerUrl,
                    platform: c.Platform,
                    subscribedAt: c.SubscribedAt,
                    categoryId: null,
                    subscriberCount: null
                })));
            } catch (e) {
                console.error('Error loading channels:', e);
            } finally {
                this.loading(false);
            }
        }
    };

    // ── Toggle categories ─────────────────────────────────────────────────────

    toggleCategories = async () => {
        const newVal = !this.enableChannelCategories();
        this.enableChannelCategories(newVal);
        await this.saveEnableCategories(newVal);
        if (newVal) {
            await this.loadCategories();
        }
        await this.loadChannels();
    };

    // ── Add channel ───────────────────────────────────────────────────────────

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
            // Normalise to camelCase
            const channel = {
                id: data.Id,
                channelId: data.ChannelId,
                name: data.Name,
                url: data.Url,
                avatarUrl: data.AvatarUrl,
                bannerUrl: data.BannerUrl,
                platform: data.Platform,
                subscribedAt: data.SubscribedAt,
                categoryId: null
            };
            this.channels.push(channel);
            this.newChannelUrl('');
            bootstrap.Modal.getInstance(document.getElementById('addChannelModal')).hide();

            // Show thumbnail picker
            this.thumbnailPickerChannelId(channel.id);
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

            await this.loadThumbnailPickerItems(channel.id);

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

            if (bannerUrl !== null || avatarUrl !== null) {
                await fetch(`/api/channels/${channelId}/images`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ bannerUrl, avatarUrl })
                });
            }

            const updated = this.channels().find(c => c.id === channelId);
            if (updated) {
                updated.bannerUrl = bannerUrl;
                updated.avatarUrl = avatarUrl;
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

    // ── Drag & drop / file input ──────────────────────────────────────────────

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

    // ── Custom channel ────────────────────────────────────────────────────────

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

    // ── Navigation / delete ───────────────────────────────────────────────────

    viewChannel = (channel) => {
        window.location.href = `/channels/${channel.id}`;
    };

    deleteChannel = (channel) => {
        if (this.isAdmin) {
            this.channelToDelete(channel);
            this.adminDeleteMetadata(false);
            this.adminDeleteFiles(false);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('adminDeleteChannelModal')).show();
        } else {
            this._unsubscribeChannel(channel);
        }
    };

    confirmAdminDelete = async () => {
        const channel = this.channelToDelete();
        if (!channel) return;

        const params = new URLSearchParams({
            deleteMetadata: this.adminDeleteMetadata(),
            deleteFiles: this.adminDeleteFiles()
        });

        bootstrap.Modal.getOrCreateInstance(document.getElementById('adminDeleteChannelModal')).hide();

        await fetch(`/api/channels/${channel.id}?${params}`, { method: 'DELETE' })
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
            const response = await fetch(`/api/channels/${channel.id}/user-subscriptions`);
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
            const response = await fetch(`/api/channels/${channel.id}/user-subscriptions`, {
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
        if (!confirm(`Are you sure you want to unsubscribe from ${channel.name}?`)) return;

        await fetch(`/odata/ChannelOData(${channel.id})`, { method: 'DELETE' })
            .then(response => {
                if (response.ok) {
                    this.channels.remove(channel);
                    toast.success(`Unsubscribed from ${channel.name}.`);
                } else {
                    toast.error('Failed to unsubscribe from channel.');
                }
            })
            .catch(error => {
                console.error('Error unsubscribing from channel:', error);
                toast.error('Failed to unsubscribe from channel.');
            });
    };

    // ── Edit channel category modal ───────────────────────────────────────────

    openEditCategoryModal = (channel) => {
        this.editCategoryChannel(channel);
        this.editCategoryCreatingNew(false);
        this.editCategoryNewName('');
        this.editCategorySelectedId(channel.categoryId || null);
        bootstrap.Modal.getOrCreateInstance(document.getElementById('editChannelCategoryModal')).show();
    };

    confirmEditCategory = async () => {
        const channel = this.editCategoryChannel();
        if (!channel) return;

        this.editCategorySaving(true);
        try {
            let categoryId = this.editCategorySelectedId();

            if (this.editCategoryCreatingNew()) {
                const newName = this.editCategoryNewName().trim();
                if (!newName) {
                    toast.error('Please enter a category name.');
                    return;
                }
                const res = await fetch('/api/channel-categories', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: newName })
                });
                if (!res.ok) {
                    const d = await res.json().catch(() => ({}));
                    toast.error(d.message || 'Failed to create category.');
                    return;
                }
                const newCat = await res.json();
                this.categories.push(newCat);
                categoryId = newCat.id;
            }

            const res = await fetch(`/api/channels/${channel.id}/category`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ categoryId })
            });

            if (!res.ok) {
                toast.error('Failed to assign category.');
                return;
            }

            // Update in-memory
            channel.categoryId = categoryId;
            this.channels.valueHasMutated();

            bootstrap.Modal.getInstance(document.getElementById('editChannelCategoryModal')).hide();
            toast.success('Category updated.');
        } catch (error) {
            console.error('Error assigning category:', error);
            toast.error('Failed to assign category.');
        } finally {
            this.editCategorySaving(false);
        }
    };

    deleteCategory = async (category) => {
        if (!confirm(`Delete category "${category.name}"? Channels in this category will become uncategorized.`)) return;

        try {
            const res = await fetch(`/api/channel-categories/${category.id}`, { method: 'DELETE' });
            if (res.ok) {
                // Clear categoryId on all channels that were in this category
                this.channels().forEach(c => {
                    if (c.categoryId === category.id) c.categoryId = null;
                });
                this.channels.valueHasMutated();
                this.categories.remove(category);
                toast.success(`Category "${category.name}" deleted.`);
            } else {
                toast.error('Failed to delete category.');
            }
        } catch (e) {
            console.error('Error deleting category:', e);
            toast.error('Failed to delete category.');
        }
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new ChannelsViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadSettings();
    if (viewModel.enableChannelCategories()) {
        await viewModel.loadCategories();
    }
    await viewModel.loadChannels();
});
