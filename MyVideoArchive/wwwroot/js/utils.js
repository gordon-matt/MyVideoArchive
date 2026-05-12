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

export function formatSeconds(totalSeconds) {
    if (!totalSeconds && totalSeconds !== 0) return '';
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const s = Math.floor(totalSeconds % 60);
    if (h > 0) {
        return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
    }
    return `${m}:${s.toString().padStart(2, '0')}`;
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

/**
 * Encodes each segment of a local /archive/... path so it is safe in HTML img src (e.g. # in "C#", spaces).
 * Preserves an existing query string (e.g. ?t= for cache bust). Idempotent for already-encoded segments.
 * @param {string | null | undefined} url
 * @returns {string | null | undefined}
 */
export function encodeArchiveUrlForHtml(url) {
    if (url == null || typeof url !== 'string') return url;
    const prefix = '/archive/';
    if (!url.startsWith(prefix)) return url;

    const qIndex = url.indexOf('?');
    const path = qIndex >= 0 ? url.slice(0, qIndex) : url;
    const query = qIndex >= 0 ? url.slice(qIndex) : '';
    const rest = path.slice(prefix.length);
    const encoded = rest
        .split('/')
        .filter(Boolean)
        .map((part) => {
            try {
                return encodeURIComponent(decodeURIComponent(part));
            } catch {
                return encodeURIComponent(part);
            }
        })
        .join('/');

    return prefix + encoded + query;
}