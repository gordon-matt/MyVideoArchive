export const delay = (ms) =>
    new Promise(resolve => setTimeout(resolve, ms));

export function formatDate(dateString) {
    if (!dateString) return 'N/A';
    var date = new Date(dateString);
    return date.toLocaleDateString();
}

export function formatDuration(duration) {
    if (!duration) return 'N/A';

    if (duration.startsWith('PT')) {
        let hours = 0, minutes = 0, seconds = 0;

        const match = duration.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
        if (match) {
            hours = parseInt(match[1]) || 0;
            minutes = parseInt(match[2]) || 0;
            seconds = parseInt(match[3]) || 0;
        }

        if (hours > 0) {
            return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
        } else {
            return `${minutes}:${seconds.toString().padStart(2, '0')}`;
        }
    }

    if (duration.includes(':')) {
        return duration.substring(0, 8);
    }

    return duration;
}

export function formatFileSize(bytes, decimals = 2) {
    if (!bytes && bytes !== 0) return 'N/A';
    if (bytes === 0) return '0 B';

    const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    const value = bytes / Math.pow(1024, i);

    return `${value.toFixed(decimals)} ${sizes[i]}`;
}

export function formatNumber(num) {
    if (!num) return '0';
    return num.toLocaleString();
}