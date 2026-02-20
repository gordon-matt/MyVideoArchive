function ChannelsViewModel() {
    var self = this;

    self.channels = ko.observableArray([]);
    self.loading = ko.observable(true);
    self.newChannelUrl = ko.observable('');

    self.loadChannels = function () {
        self.loading(true);

        fetch('/odata/ChannelOData?$orderby=Name')
            .then(response => response.json())
            .then(data => {
                self.channels(data.value || []);
                self.loading(false);
            })
            .catch(error => {
                console.error('Error loading channels:', error);
                self.loading(false);
            });
    };

    self.addChannel = function () {
        var url = self.newChannelUrl();
        if (!url) return;

        // Extract channel ID from URL
        var channelId = self.extractChannelId(url);

        var newChannel = {
            ChannelId: channelId,
            Name: 'Loading...',
            Url: url,
            SubscribedAt: new Date().toISOString()
        };

        fetch('/odata/ChannelOData', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(newChannel)
        })
            .then(response => response.json())
            .then(data => {
                self.channels.push(data);
                self.newChannelUrl('');
                bootstrap.Modal.getInstance(document.getElementById('addChannelModal')).hide();
            })
            .catch(error => {
                console.error('Error adding channel:', error);
                alert('Failed to add channel. Please try again.');
            });
    };

    self.extractChannelId = function (url) {
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

    self.viewChannel = function (channel) {
        window.location.href = '/channels/' + channel.Id;
    };

    self.deleteChannel = function (channel) {
        if (!confirm('Are you sure you want to unsubscribe from ' + channel.Name + '?')) {
            return;
        }

        fetch('/odata/ChannelOData(' + channel.Id + ')', {
            method: 'DELETE'
        })
            .then(response => {
                if (response.ok) {
                    self.channels.remove(channel);
                } else {
                    alert('Failed to delete channel.');
                }
            })
            .catch(error => {
                console.error('Error deleting channel:', error);
                alert('Failed to delete channel.');
            });
    };

    self.formatDate = function (dateString) {
        if (!dateString) return 'N/A';
        var date = new Date(dateString);
        return date.toLocaleDateString();
    };
}

var viewModel;

$(document).ready(function () {
    viewModel = new ChannelsViewModel();
    ko.applyBindings(viewModel);
    viewModel.loadChannels();
});