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

export function isFlvMimeType(hint) {
    const mime = getVideoJsMimeType(hint);
    return mime === 'video/x-flv' || mime === 'video/flv';
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

/** Options for archived FLV files (not live streams). */
export const VIDEOJS_FLV_PLAYER_OPTIONS = {
    techOrder: ['html5', 'Flvjs'],
    flvjs: {
        mediaDataSource: {
            cors: true,
            withCredentials: false
        }
    }
};

/**
 * @param {Record<string, unknown>} options
 * @param {string | null | undefined} [mimeHint] When omitted, FLV tech stays enabled (playlist pages).
 * @returns {Record<string, unknown>}
 */
export function mergeVideoJsPlayerOptions(options, mimeHint) {
    const includeFlv = mimeHint === undefined || mimeHint === null || isFlvMimeType(mimeHint);
    const flvOptions = includeFlv ? VIDEOJS_FLV_PLAYER_OPTIONS : {};

    return {
        ...flvOptions,
        ...options,
        flvjs: includeFlv
            ? {
                ...VIDEOJS_FLV_PLAYER_OPTIONS.flvjs,
                ...(options.flvjs ?? {})
            }
            : options.flvjs
    };
}

/**
 * Server content type for stream URL (same source as playlist API streamContentType).
 * @param {number} videoId
 * @returns {Promise<string | null>}
 */
export async function fetchVideoPlaybackContentType(videoId) {
    try {
        const response = await fetch(`/api/videos/${videoId}/playback-info`);
        if (!response.ok) {
            return null;
        }

        const data = await response.json();
        return data.contentType ?? data.ContentType ?? null;
    } catch (error) {
        console.error('Error loading playback info:', error);
        return null;
    }
}
