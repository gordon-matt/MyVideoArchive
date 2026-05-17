/**
 * Video.js helpers (including FLV via videojs-flvjs + flv.js).
 */

const EXTENSION_MIME = {
    '.mp4': 'video/mp4',
    '.m4v': 'video/mp4',
    '.webm': 'video/webm',
    '.mkv': 'video/x-matroska',
    '.avi': 'video/x-msvideo',
    '.mov': 'video/quicktime',
    '.flv': 'video/x-flv',
    '.wmv': 'video/x-ms-wmv'
};

/**
 * Resolve a Video.js source MIME type from a file path, extension, or server content type.
 * @param {string | null | undefined} hint
 * @returns {string}
 */
export function getVideoJsMimeType(hint) {
    if (!hint) {
        return 'video/mp4';
    }

    const trimmed = String(hint).trim();
    if (!trimmed) {
        return 'video/mp4';
    }

    if (trimmed.includes('/')) {
        return trimmed;
    }

    const ext = trimmed.startsWith('.')
        ? trimmed.toLowerCase()
        : (trimmed.includes('.') ? `.${trimmed.split('.').pop()}` : '').toLowerCase();

    return EXTENSION_MIME[ext] ?? 'video/mp4';
}

/**
 * @param {number} videoId
 * @param {string | null | undefined} mimeHint file path, extension, or content type
 * @returns {{ src: string, type: string }}
 */
export function buildVideoStreamSource(videoId, mimeHint) {
    return {
        src: `/api/videos/${videoId}/stream`,
        type: getVideoJsMimeType(mimeHint)
    };
}

/** Options required for archived FLV files (not live streams). */
export const VIDEOJS_FLV_PLAYER_OPTIONS = {
    techOrder: ['Html5', 'Flvjs'],
    flvjs: {
        mediaDataSource: {
            cors: true,
            withCredentials: false
        }
    }
};

/**
 * @param {Record<string, unknown>} options
 * @returns {Record<string, unknown>}
 */
export function mergeVideoJsPlayerOptions(options) {
    return {
        ...VIDEOJS_FLV_PLAYER_OPTIONS,
        ...options,
        flvjs: {
            ...VIDEOJS_FLV_PLAYER_OPTIONS.flvjs,
            ...(options.flvjs ?? {})
        }
    };
}
