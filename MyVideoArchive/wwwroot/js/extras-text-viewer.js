const textExtensions = new Set(['.txt', '.md', '.markdown', '.html', '.htm']);

export function isTextualExtraFileName(fileName) {
    if (!fileName) {
        return false;
    }
    const lower = fileName.toLowerCase();
    const dot = lower.lastIndexOf('.');
    if (dot < 0) {
        return false;
    }
    return textExtensions.has(lower.slice(dot));
}

let lastObjectUrl = null;

function cleanupViewer() {
    const iframeEl = document.getElementById('extraTextViewerIframe');
    const preEl = document.getElementById('extraTextViewerPre');
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
}

/**
 * Loads an additional-content file and shows it in the shared Bootstrap modal.
 */
export async function openExtraTextViewerModal(itemId, fileName) {
    const modalEl = document.getElementById('extraTextViewerModal');
    const titleEl = document.getElementById('extraTextViewerTitle');
    const preEl = document.getElementById('extraTextViewerPre');
    const iframeEl = document.getElementById('extraTextViewerIframe');
    const wrapPre = document.getElementById('extraTextViewerPreWrap');
    const wrapIframe = document.getElementById('extraTextViewerIframeWrap');

    if (!modalEl || !titleEl || !preEl || !iframeEl || !wrapPre || !wrapIframe) {
        console.error('Text viewer modal DOM is missing.');
        return;
    }

    titleEl.textContent = fileName || 'Extra';
    cleanupViewer();

    const lower = (fileName || '').toLowerCase();
    const isHtml = lower.endsWith('.html') || lower.endsWith('.htm');

    const url = `/api/additional-content/${itemId}/download`;
    const downloadBtn = document.getElementById('extraTextViewerDownloadBtn');
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

        const text = await res.text();

        if (isHtml) {
            wrapPre.classList.add('d-none');
            wrapIframe.classList.remove('d-none');
            iframeEl.setAttribute('sandbox', '');
            const blob = new Blob([text], { type: 'text/html;charset=utf-8' });
            lastObjectUrl = URL.createObjectURL(blob);
            iframeEl.src = lastObjectUrl;
        } else {
            wrapIframe.classList.add('d-none');
            wrapPre.classList.remove('d-none');
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
