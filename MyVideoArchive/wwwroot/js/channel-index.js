import { formatDate } from './utils.js';

class ChannelsViewModel {
    constructor() {
        this.channels = ko.observableArray([]);
        this.loading = ko.observable(true);
        this.newChannelUrl = ko.observable('');

        this.formatDate = formatDate;
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
        var url = this.newChannelUrl();
        if (!url) return;

        // Extract channel ID from URL
        var channelId = this.extractChannelId(url);

        var newChannel = {
            ChannelId: channelId,
            Name: 'Loading...',
            Url: url,
            SubscribedAt: new Date().toISOString()
        };

        await fetch('/odata/ChannelOData', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(newChannel)
        })
        .then(response => response.json())
        .then(data => {
            this.channels.push(data);
            this.newChannelUrl('');
            bootstrap.Modal.getInstance(document.getElementById('addChannelModal')).hide();
        })
        .catch(error => {
            console.error('Error adding channel:', error);
            alert('Failed to add channel. Please try again.');
        });
    };

    extractChannelId = (url) => {
        // Simple extraction - in production, you'd want to use yt-dlp to get proper channel info
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