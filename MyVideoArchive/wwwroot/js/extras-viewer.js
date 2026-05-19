const textExtensions = new Set(['.txt', '.md', '.markdown', '.html', '.htm']);
const imageExtensions = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.svg']);

function getExtension(fileName) {
    if (!fileName) {
        return '';
    }
    const lower = fileName.toLowerCase();
    const dot = lower.lastIndexOf('.');
    if (dot < 0) {
        return '';
    }
    return lower.slice(dot);
}

/** @returns {'text' | 'html' | 'image' | null} */
function getExtraPreviewKind(fileName) {
    const ext = getExtension(fileName);
    if (!ext) {
        return null;
    }
    if (imageExtensions.has(ext)) {
        return 'image';
    }
    if (ext === '.html' || ext === '.htm') {
        return 'html';
    }
    if (textExtensions.has(ext)) {
        return 'text';
    }
    return null;
}

export function isPreviewableExtraFileName(fileName) {
    return getExtraPreviewKind(fileName) !== null;
}

let lastObjectUrl = null;

function cleanupViewer() {
    const iframeEl = document.getElementById('extraViewerIframe');
    const preEl = document.getElementById('extraViewerPre');
    const imgEl = document.getElementById('extraViewerImg');
    if (lastObjectUrl) {
        URL.revokeObjectURL(lastObjectUrl);
        lastObjectUrl = null;
    }
    if (iframeEl) {
        iframeEl.removeAttribute('src');
        iframeEl.removeAttribute('srcdoc');
    }
    if (preEl) {
        preEl.textContent = '';
    }
    if (imgEl) {
        imgEl.removeAttribute('src');
        imgEl.alt = '';
    }
}

function setViewerMode(kind) {
    document.getElementById('extraViewerIframeWrap')?.classList.toggle('d-none', kind !== 'html');
    document.getElementById('extraViewerPreWrap')?.classList.toggle('d-none', kind !== 'text');
    document.getElementById('extraViewerImgWrap')?.classList.toggle('d-none', kind !== 'image');
}

/**
 * Loads an additional-content file and shows it in the shared Bootstrap modal.
 */
export async function openExtraViewerModal(itemId, fileName) {
    const modalEl = document.getElementById('extraViewerModal');
    const titleEl = document.getElementById('extraViewerTitle');
    const preEl = document.getElementById('extraViewerPre');
    const iframeEl = document.getElementById('extraViewerIframe');
    const imgEl = document.getElementById('extraViewerImg');

    if (!modalEl || !titleEl || !preEl || !iframeEl || !imgEl) {
        console.error('Preview viewer modal DOM is missing.');
        return;
    }

    const kind = getExtraPreviewKind(fileName);
    if (!kind) {
        return;
    }

    titleEl.textContent = fileName || 'Extra';
    cleanupViewer();

    const url = `/api/additional-content/${itemId}/download`;
    const downloadBtn = document.getElementById('extraViewerDownloadBtn');
    if (downloadBtn) {
        downloadBtn.href = `${url}?isForDownload=true`;
    }

    try {
        const res = await fetch(url, { credentials: 'same-origin' });
        if (!res.ok) {
            if (typeof toast !== 'undefined' && toast?.error) {
                toast.error('Could not load file.');
            }
            return;
        }

        if (kind === 'image') {
            const blob = await res.blob();
            lastObjectUrl = URL.createObjectURL(blob);
            imgEl.src = lastObjectUrl;
            imgEl.alt = fileName || 'Image preview';
            setViewerMode('image');
        } else if (kind === 'html') {
            const text = await res.text();
            setViewerMode('html');
            iframeEl.setAttribute('sandbox', '');
            const blob = new Blob([text], { type: 'text/html;charset=utf-8' });
            lastObjectUrl = URL.createObjectURL(blob);
            iframeEl.src = lastObjectUrl;
        } else {
            const text = await res.text();
            setViewerMode('text');
            preEl.textContent = text;
        }

        modalEl.addEventListener('hidden.bs.modal', cleanupViewer, { once: true });

        const modal = window.bootstrap?.Modal?.getOrCreateInstance(modalEl);
        modal?.show();
    } catch (e) {
        console.error(e);
        if (typeof toast !== 'undefined' && toast?.error) {
            toast.error('Could not load file.');
        }
        cleanupViewer();
    }
}
