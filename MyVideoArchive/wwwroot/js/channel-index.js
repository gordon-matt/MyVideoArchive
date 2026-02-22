import { formatDate } from './utils.js';

class ChannelsViewModel {
    constructor() {
        this.channels = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.selectedPlatform = ko.observable('YouTube');
        this.newChannelUrl = ko.observable('');
        this.customChannelName = ko.observable('');
        this.customChannelDescription = ko.observable('');

        this.formatDate = formatDate;

        this.canSubmit = ko.computed(() => {
            if (this.selectedPlatform() === 'YouTube') {
                return this.newChannelUrl().length > 0;
            }
            return this.customChannelName().length > 0;
        });
    }

    loadChannels = async () => {
        this.loading(true);

        await fetch('/odata/ChannelOData?$orderby=Name')
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
        if (this.selectedPlatform() === 'YouTube') {
            await this.addYouTubeChannel();
        } else {
            await this.addCustomChannel();
        }
    };

    addYouTubeChannel = async () => {
        const url = this.newChannelUrl();
        if (!url) return;

        const channelId = this.extractChannelId(url);

        const newChannel = {
            Platform: 'YouTube',
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
            console.error('Error adding YouTube channel:', error);
            alert('Failed to add channel. Please try again.');
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
            alert('Failed to create custom channel. Please try again.');
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

    deleteChannel = async (channel) => {
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
                alert('Failed to delete channel.');
            }
        })
        .catch(error => {
            console.error('Error deleting channel:', error);
            alert('Failed to delete channel.');
        });
    };
}

var viewModel;

document.addEventListener("DOMContentLoaded", async () => {
    viewModel = new ChannelsViewModel();
    ko.applyBindings(viewModel);
    await viewModel.loadChannels();
});
