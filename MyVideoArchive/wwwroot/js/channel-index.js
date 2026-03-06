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
        this.channelToDelete = ko.observable(null);
        this.adminDeleteMetadata = ko.observable(false);
        this.adminDeleteFiles = ko.observable(false);

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
    }

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

        await fetch('/odata/ChannelOData', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(newChannel)
        })
        .then(response => response.json())
        .then(data => {
            this.channels.push(data);
            this.newChannelUrl('');
            bootstrap.Modal.getInstance(document.getElementById('addChannelModal')).hide();
        })
        .catch(error => {
            console.error(`Error adding ${platform} channel:`, error);
            toast.error('Failed to add channel. Please try again.');
        });
    };

    addCustomChannel = async () => {
        const name = this.customChannelName().trim();
        if (!name) return;

        await fetch('/api/custom/channels', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name,
                description: this.customChannelDescription().trim() || null
            })
        })
        .then(async response => {
            if (!response.ok) throw new Error('Failed to create custom channel');
            const data = await response.json();
            // Redirect straight to the custom channel details page
            window.location.href = `/channels/${data.id}`;
        })
        .catch(error => {
            console.error('Error adding custom channel:', error);
            toast.error('Failed to create custom channel. Please try again.');
        });
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
            } else {
                toast.error('Failed to delete channel.');
            }
        })
        .catch(error => {
            console.error('Error deleting channel:', error);
            toast.error('Failed to delete channel.');
        });
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
